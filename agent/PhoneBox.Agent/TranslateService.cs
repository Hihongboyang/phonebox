using System.Diagnostics;
using System.Text.Json;

namespace PhoneBox.Agent;

/// <summary>Why a clipboard translation gave no translation: "provider" (Baidu answered with an error) or "network".</summary>
public sealed record TranslateFailure(string Code, string? Detail);

/// <summary>
/// The data of a "translate" panel event: a translation (source, target, lang), or an error with its detail. The
/// source, target and lang are null on an error, so <see cref="ConfigStore.WriteOpts"/> leaves them out of the JSON.
/// </summary>
public sealed record TranslateCardEvent(string? Source, string? Target, string? Lang, string? Error, string? Detail, int Seconds);

/// <summary>The body of POST /api/translate/test: the credentials in the settings page's form, or empty to use the saved ones.</summary>
public sealed record TranslateTestRequest(string? AppId, string? Key);

/// <summary>
/// The Translate hotkey: one press reads the clipboard's text, translates it with Baidu Translate and shows it on the
/// phone as a Translate card. Off until Settings › Translate is enabled with an APPID and a key. The text goes to
/// Baidu and to the phone and nowhere else: it is never logged, and neither is the key.
/// </summary>
public sealed class TranslateService(ConfigStore config, PushStore push, BaiduTranslate baidu, Hotkeys hotkeys, ILogger<TranslateService> log)
{
    public const int SuccessSeconds = 30;
    public const int ErrorSeconds = 10;

    /// <summary>The shortest time between two requests to Baidu (an unverified account gets one a second).</summary>
    public static TimeSpan MinGap { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Tests take the place of <see cref="Hotkeys.Apply"/>: what to register, and what it answered.</summary>
    internal Func<string?, string?>? TestApply;

    /// <summary>The run in flight: a newer hotkey press cancels it, so only the latest text is translated and shown.</summary>
    private volatile CancellationTokenSource? _inFlight;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastRequest;

    /// <summary>At the agent's start: follow the hotkey and register it from the saved settings.</summary>
    public void Start()
    {
        hotkeys.Pressed += () => _ = RunAsync();
        ApplyHotkey();
    }

    /// <summary>After a settings save: register the hotkey again, or unregister it.</summary>
    public void SettingsSaved() => ApplyHotkey();

    /// <summary>Where the last registration left the hotkey, for the settings page.</summary>
    private volatile string _hotkeyState = Hotkeys.Off;

    private void ApplyHotkey()
    {
        var spec = HotkeySpec(config.Get());
        // tests stand in for Hotkeys.Apply: what was asked for, and what it answered
        var answer = (TestApply ?? hotkeys.Apply)(spec);
        _hotkeyState = spec is null ? Hotkeys.Off : answer ?? Hotkeys.Registered;
    }

    /// <summary>One hotkey press.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        var cfg = config.Get().Translate;
        if (!Active(cfg)) return;

        // a clipboard read can block for seconds (another program holds it): never on a request thread
        string text;
        try { text = await Task.Run(() => Clipboard.GetText(), CancellationToken.None); }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException) { return; } // busy: another time

        text = Normalize(text);
        if (text.Length == 0 || IsChinese(text)) return;
        text = Cut(text, cfg.MaxChars);

        // a newer press takes over: the older request is cancelled and its event never emitted
        _inFlight?.Cancel();
        var mine = _inFlight = new CancellationTokenSource();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, mine.Token);
            await _gate.WaitAsync(linked.Token);
            try
            {
                // one request at a time, and no less than MinGap between two: an unverified account is allowed one a second
                var wait = MinGap - TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - Interlocked.Read(ref _lastRequest)));
                if (wait > TimeSpan.Zero) await Task.Delay(wait, linked.Token);
                if (linked.IsCancellationRequested) return;
                Interlocked.Exchange(ref _lastRequest, Environment.TickCount64);

                var started = Stopwatch.StartNew();
                var (result, failure) = await baidu.TranslateAsync(text, cfg, linked.Token);
                push.Emit("translate", JsonSerializer.SerializeToElement(
                    result is null ? ErrorEvent(failure!) : SuccessEvent(text, result), ConfigStore.WriteOpts));
                // the outcome, the length and the duration only: the text and the key never reach a log (D24)
                log.LogInformation("translate: {Chars} chars, {Ms} ms, {Outcome}", text.Length, started.ElapsedMilliseconds,
                    result is null ? $"{failure!.Code} {failure.Detail}".TrimEnd() : "ok");
            }
            finally { _gate.Release(); }
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            // the caller gave up (a newer hotkey press), or something nobody mapped failed: end quietly (D24)
        }
        finally
        {
            if (ReferenceEquals(_inFlight, mine)) _inFlight = null;
            mine.Dispose();
        }
    }

    /// <summary>For the settings page: whether the feature is ready, and where its hotkey is.</summary>
    public object Status() => new { active = Active(config.Get().Translate), hotkey = _hotkeyState };

    /// <summary>Settings › Translate › Test: translates "hello" with the given credentials, or the saved ones.</summary>
    public async Task<(string? Result, string? Error)> TestAsync(string? appId, string? key, CancellationToken ct)
    {
        var cfg = config.Get().Translate;
        // the fields in the form, which may differ from the saved settings; empty fields fall back to them
        var credentials = new TranslateConfig
        {
            Enabled = true,
            Hotkey = cfg.Hotkey,
            AppId = string.IsNullOrWhiteSpace(appId) ? cfg.AppId : appId.Trim(),
            Key = string.IsNullOrWhiteSpace(key) ? cfg.Key : key.Trim(),
            MaxChars = cfg.MaxChars,
        };
        var started = Stopwatch.StartNew();
        var (result, failure) = await baidu.TranslateAsync("hello", credentials, ct);
        log.LogInformation("translate: test, {Ms} ms, {Outcome}", started.ElapsedMilliseconds,
            result is null ? $"{failure!.Code} {failure.Detail}".TrimEnd() : "ok");
        // the settings page shows the raw answer: Baidu's own message, or where it failed
        return (result?.Target, result is null ? failure!.Detail ?? failure.Code : null);
    }

    /// <summary>Whether the hotkey is to be registered: enabled, with an APPID and a key.</summary>
    public static bool Active(TranslateConfig? cfg) => cfg is { Enabled: true, AppId: { Length: > 0 }, Key: { Length: > 0 } };

    /// <summary>The hotkey spec to register for this config, or null when there is to be none.</summary>
    public static string? HotkeySpec(AgentConfig cfg) => cfg.Translate is { } t && Active(t) ? t.Hotkey : null;

    /// <summary>The Chinese-skip rule: text with Han characters and no kana or Hangul. Japanese and Korean with kanji in them stay translatable.</summary>
    public static bool IsChinese(string text)
    {
        var han = false;
        foreach (var r in text.EnumerateRunes())
        {
            var c = r.Value;
            if (IsHan(c)) han = true;
            else if (IsKana(c) || IsHangul(c)) return false;
        }
        return han;
    }

    /// <summary>Trims the text and turns its line endings into \n, the separator Baidu's paragraphs come back with.</summary>
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    /// <summary>At most <paramref name="maxChars"/> characters in all, ending with … when something was cut off.</summary>
    public static string Cut(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        // the ellipsis is one of the maxChars, and the text must not end between the two halves of a character
        // outside the BMP (an emoji): a lone half cannot be shown
        var cut = Math.Max(0, maxChars - 1);
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    internal static TranslateCardEvent SuccessEvent(string source, BaiduTranslation translation) =>
        new(source, translation.Target, translation.Lang, null, null, SuccessSeconds);

    internal static TranslateCardEvent ErrorEvent(TranslateFailure failure) =>
        new(null, null, null, failure.Code, failure.Detail, ErrorSeconds);

    // Han: the CJK ideographs, the extension A and the compatibility ideographs
    private static bool IsHan(int c) => In(c, 0x3400, 0x4DBF) || In(c, 0x4E00, 0x9FFF) || In(c, 0xF900, 0xFAFF) || In(c, 0x20000, 0x2FA1F);
    // Kana: hiragana, katakana and the halfwidth katakana
    private static bool IsKana(int c) => In(c, 0x3041, 0x30FF) || In(c, 0x31F0, 0x31FF) || In(c, 0xFF66, 0xFF9D);
    // Hangul: the jamo, the compatibility jamo and the syllables
    private static bool IsHangul(int c) => In(c, 0x1100, 0x11FF) || In(c, 0x3130, 0x318F) || In(c, 0xAC00, 0xD7AF);

    private static bool In(int value, int low, int high) => (uint)(value - low) <= (uint)(high - low);
}
