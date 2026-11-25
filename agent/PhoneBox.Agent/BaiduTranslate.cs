using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhoneBox.Agent;

/// <summary>A translation and the voice tag of its source text.</summary>
public sealed record BaiduTranslation(string Target, string? Lang);

/// <summary>
/// Baidu Translate's general text translation API, with the APPID and key of Settings › Translate. Each request is
/// signed with MD5(appid + q + salt + key), the language is detected on Baidu's side, and the answer's paragraphs come
/// back joined with newlines. The key is never sent and never logged.
/// </summary>
public sealed class BaiduTranslate(ILogger<BaiduTranslate> log, HttpMessageHandler? handler = null, Func<string>? salt = null) : IDisposable
{
    public const string Endpoint = "https://fanyi-api.baidu.com/api/trans/vip/translate";

    private readonly Func<string> _salt = salt ?? NewSalt;

    // no UseProxy = false here, unlike the radio and feed clients: Baidu is reached through the system proxy when one is set
    private readonly HttpClient _http = new(handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    public void Dispose() => _http.Dispose();

    /// <summary>The request's signature: the lowercase hexadecimal MD5 of UTF-8 (appId + q + salt + key).</summary>
    public static string Sign(string appId, string q, string salt, string key) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(appId + q + salt + key)));

    private static string NewSalt() => Guid.NewGuid().ToString("N");

    /// <summary>The BCP 47 tag of the voice that reads a language Baidu reports in its answer, or null when it has none.</summary>
    public static string? VoiceTag(string? baiduLanguage) => baiduLanguage switch
    {
        "en" => "en-US", "jp" => "ja", "kor" => "ko", "fra" => "fr-FR", "de" => "de-DE", "spa" => "es-ES", "pt" => "pt-PT",
        "it" => "it-IT", "ru" => "ru-RU", "th" => "th-TH", "vie" => "vi-VN", "ara" => "ar-SA",
        _ => null,
    };

    /// <summary>Translates the text into Chinese; Baidu detects its language. Never throws: a failure comes back as the error.</summary>
    public async Task<(BaiduTranslation? Result, TranslateFailure? Error)> TranslateAsync(string text, TranslateConfig cfg, CancellationToken ct)
    {
        try
        {
            var saltValue = _salt();
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = text,
                ["from"] = "auto",
                ["to"] = "zh",
                ["appid"] = cfg.AppId ?? "",
                ["salt"] = saltValue,
                ["sign"] = Sign(cfg.AppId ?? "", text, saltValue, cfg.Key ?? ""),
            });
            using var response = await _http.PostAsync(Endpoint, form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) return (null, new TranslateFailure("network", $"HTTP {(int)response.StatusCode}"));

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            // a proxy or a captive portal can answer with anything; only an object can be Baidu's answer
            if (root.ValueKind != JsonValueKind.Object) return (null, new TranslateFailure("network", null));

            // Baidu answers an error as {"error_code": "52003", "error_msg": "..."}
            if (root.TryGetProperty("error_code", out var code))
            {
                var message = root.TryGetProperty("error_msg", out var msg) ? msg.GetString() : null;
                return (null, new TranslateFailure("provider", message is null ? code.GetString() : $"{code.GetString()} {message}"));
            }

            // the paragraphs come back as one result each; \n in the request is what separates them
            if (root.TryGetProperty("trans_result", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                var target = string.Join("\n", results.EnumerateArray()
                    .Select(r => r.TryGetProperty("dst", out var dst) ? dst.GetString() : null)
                    .Where(d => d is not null));
                if (target.Length > 0)
                {
                    var from = root.TryGetProperty("from", out var f) ? f.GetString() : null;
                    return (new BaiduTranslation(target, VoiceTag(from)), null);
                }
            }
            return (null, new TranslateFailure("network", null)); // an answer without a translation in it
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller gave up (a newer hotkey press); that is not Baidu's failure
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // TaskCanceledException without the caller's token: the 8 s timeout. Nothing about the request is logged: its
            // body is the text (D24)
            return (null, new TranslateFailure("network", ex is TaskCanceledException ? "timeout" : null));
        }
    }
}
