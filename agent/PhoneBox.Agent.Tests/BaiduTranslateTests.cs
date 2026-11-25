using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PhoneBox.Agent;
using Xunit;

/// <summary>
/// The Baidu Translate request: the MD5 signature, the form it posts, the paragraphs it joins back, the voice tag, and
/// the two ways it fails. The HTTP is answered by a fake handler, so no request leaves the machine.
/// </summary>
public class BaiduTranslateTests
{
    private static TranslateConfig Cfg(string? key = "s") => new() { Enabled = true, Hotkey = "ctrl+alt+d", AppId = "1", Key = key, MaxChars = 1000 };

    /// <summary>Answers every request with the given response and remembers the request and the form it carried.</summary>
    private sealed class Baidu(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Form;

        // the form is read here: the provider disposes it once the answer is back
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            if (request.Content is not null) Form = await request.Content.ReadAsStringAsync(ct);
            return answer(request);
        }
    }

    private static HttpResponseMessage Ok(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static BaiduTranslate Provider(Baidu baidu) => new(NullLogger<BaiduTranslate>.Instance, baidu, () => "2");

    // the fixed expectations were computed outside the agent (Python's hashlib), so the oracle is independent
    [Fact]
    public void Sign_isTheLowercaseMd5OfAppidTextSaltAndKey()
    {
        Assert.Equal("8982ae6cb6d4b0e5cda44a2271a2772f", BaiduTranslate.Sign("1", "hello", "2", "s"));
        // the text is UTF-8: 你好 is not one character per byte
        Assert.Equal("a85cd578760637169724e10353a4c9a3", BaiduTranslate.Sign("1", "你好", "2", "s"));
    }

    [Fact]
    public async Task TranslateAsync_postsTheSignedForm()
    {
        var baidu = new Baidu(_ => Ok(new { from = "en", to = "zh", trans_result = new[] { new { src = "hello", dst = "你好" } } }));
        var (result, error) = await Provider(baidu).TranslateAsync("hello", Cfg(), CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("你好", result!.Target);
        Assert.Equal("en-US", result.Lang);

        Assert.Equal(BaiduTranslate.Endpoint, baidu.Request!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, baidu.Request.Method);
        Assert.Equal("q=hello&from=auto&to=zh&appid=1&salt=2&sign=" + BaiduTranslate.Sign("1", "hello", "2", "s"), baidu.Form);
    }

    [Fact]
    public async Task TranslateAsync_joinsParagraphsWithNewlines()
    {
        var baidu = new Baidu(_ => Ok(new { trans_result = new[] { new { src = "a", dst = "第一段" }, new { src = "b", dst = "第二段" } } }));
        var (result, _) = await Provider(baidu).TranslateAsync("a\nb", Cfg(), CancellationToken.None);
        Assert.Equal("第一段\n第二段", result!.Target);
    }

    [Fact]
    public void VoiceTag_mapsTheLanguagesBaiduReports()
    {
        Assert.Equal("en-US", BaiduTranslate.VoiceTag("en"));
        Assert.Equal("ja", BaiduTranslate.VoiceTag("jp"));
        Assert.Equal("ko", BaiduTranslate.VoiceTag("kor"));
        Assert.Null(BaiduTranslate.VoiceTag("xxx"));
        Assert.Null(BaiduTranslate.VoiceTag(null));
    }

    [Fact]
    public async Task TranslateAsync_reportsAProviderError()
    {
        var baidu = new Baidu(_ => Ok(new { error_code = "52003", error_msg = "UNAUTHORIZED USER" }));
        var (result, error) = await Provider(baidu).TranslateAsync("hello", Cfg(), CancellationToken.None);
        Assert.Null(result);
        Assert.Equal("provider", error!.Code);
        Assert.Equal("52003 UNAUTHORIZED USER", error.Detail);
    }

    [Fact]
    public async Task TranslateAsync_reportsNetworkFailures()
    {
        // an answer that cannot be used: a failure status...
        var baidu = new Baidu(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var (result, error) = await Provider(baidu).TranslateAsync("hello", Cfg(), CancellationToken.None);
        Assert.Null(result);
        Assert.Equal("network", error!.Code);

        // ... and a success status with a body that is not the answer we asked for
        baidu = new Baidu(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "text/plain") });
        (result, error) = await Provider(baidu).TranslateAsync("hello", Cfg(), CancellationToken.None);
        Assert.Null(result);
        Assert.Equal("network", error!.Code);
    }
}
