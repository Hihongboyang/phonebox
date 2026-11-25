using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PhoneBox.Agent;
using Xunit;

/// <summary>
/// One hotkey press: the clipboard is read, empty and Chinese text are skipped, long text is cut, Baidu is asked once,
/// and one translate panel event reaches the phone — a translation, or an error card. The clipboard and the HTTP are
/// faked, and the events are read back from the PushStore the service would talk to.
/// </summary>
[Collection("Macro recorders")]
public sealed class TranslateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phonebox-translate-" + Guid.NewGuid().ToString("N"));
    private readonly PushStore _push;

    public TranslateTests()
    {
        Directory.CreateDirectory(_dir);
        _push = new PushStore(NullLogger<PushStore>.Instance, Path.Combine(_dir, "data.json"));
    }

    public void Dispose()
    {
        Clipboard.TestBackend = null;
        _push.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>The clipboard of the test, or one that throws as a busy one does.</summary>
    private sealed class FakeClipboard(string? text) : Clipboard.IBackend
    {
        public int Reads;

        public string? Get() { Reads++; return text; }
        public void Set(string value, bool sensitive) { }
    }

    private sealed class BusyClipboard : Clipboard.IBackend
    {
        public string? Get() => throw new TimeoutException("the clipboard did not answer");
        public void Set(string value, bool sensitive) { }
    }

    /// <summary>Answers Baidu's request with the given response, records the form, and notes when each request started.</summary>
    private sealed class Baidu(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<string> Forms = [];
        public readonly List<long> Started = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Started.Add(Environment.TickCount64);
            if (request.Content is not null) Forms.Add(await request.Content.ReadAsStringAsync(ct));
            return answer();
        }
    }

    /// <summary>Answers nothing until the test says so; a cancelled request ends cancelled.</summary>
    private sealed class HeldBaidu : HttpMessageHandler
    {
        public readonly List<TaskCompletionSource<HttpResponseMessage>> Held = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            Held.Add(tcs);
            return tcs.Task;
        }
    }

    private static async Task Until(Func<bool> done, int ms = 5000)
    {
        var deadline = Environment.TickCount64 + ms;
        while (!done())
        {
            await Task.Delay(10);
            if (Environment.TickCount64 > deadline) Assert.Fail("timed out waiting for the service");
        }
    }


    private static HttpResponseMessage Ok(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Translated(string from, params string[] paragraphs) =>
        Ok(new { from, to = "zh", trans_result = paragraphs.Select(dst => new { src = "x", dst }).ToArray() });

    private TranslateService Service(HttpMessageHandler baidu,
        string config = """{"translate":{"enabled":true,"appId":"1","key":"s","maxChars":1000}}""",
        ILogger<TranslateService>? logger = null)
    {
        var configPath = Path.Combine(_dir, "config.json");
        File.WriteAllText(configPath, config);
        return new TranslateService(new ConfigStore(configPath), _push,
            new BaiduTranslate(NullLogger<BaiduTranslate>.Instance, baidu, () => "2"), new Hotkeys(),
            logger ?? NullLogger<TranslateService>.Instance);
    }

    /// <summary>Keeps every line the service writes, so a test can see what was (not) said.</summary>
    private sealed class Capture : ILogger<TranslateService>
    {
        public readonly List<string> Lines = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines) Lines.Add(formatter(state, exception));
        }
    }


    /// <summary>The translate panel events of this run, as the phone would deserialize them.</summary>
    private List<TranslateCardEvent> Cards() =>
        [.. _push.Since(0).Where(e => e.Kind == "translate").Select(e => e.Data!.Value.Deserialize<TranslateCardEvent>(ConfigStore.ReadOpts)!)];

    private string RawCard() => _push.Since(0).Single(e => e.Kind == "translate").Data!.Value.GetRawText();

    [Fact]
    public async Task RunAsync_emitsTheTranslation()
    {
        var clip = new FakeClipboard("The quick brown fox");
        Clipboard.TestBackend = clip;
        var baidu = new Baidu(() => Translated("en", "敏捷的棕色狐狸"));
        await Service(baidu).RunAsync();

        var card = Assert.Single(Cards());
        Assert.Equal("The quick brown fox", card.Source);
        Assert.Equal("敏捷的棕色狐狸", card.Target);
        Assert.Equal("en-US", card.Lang);
        Assert.Null(card.Error);
        Assert.Equal(TranslateService.SuccessSeconds, card.Seconds);
        Assert.Equal(1, clip.Reads);
        Assert.Equal(1, baidu.Forms.Count);
        // the wire shape: camelCase, and nulls left out
        Assert.Contains("\"seconds\": 30", RawCard()); // WriteOpts writes indented JSON
        Assert.DoesNotContain("error", RawCard());
    }
    [Fact]
    public async Task RunAsync_normalisesLineEndings()
    {
        Clipboard.TestBackend = new FakeClipboard("first\r\nsecond");
        var baidu = new Baidu(() => Translated("en", "第一段", "第二段"));
        await Service(baidu).RunAsync();

        Assert.StartsWith("q=first%0Asecond&", baidu.Forms.Single());
        var card = Assert.Single(Cards());
        Assert.Equal("first\nsecond", card.Source);
        Assert.Equal("第一段\n第二段", card.Target);
    }

    [Fact]
    public async Task RunAsync_doesNothingForEmptyText()
    {
        var clip = new FakeClipboard("   \r\n   ");
        Clipboard.TestBackend = clip;
        var baidu = new Baidu(() => Translated("en", "空"));
        await Service(baidu).RunAsync();

        Assert.Empty(baidu.Forms);
        Assert.Empty(Cards());
    }

    [Fact]
    public async Task RunAsync_skipsChineseText()
    {
        Clipboard.TestBackend = new FakeClipboard("今天的天气真好");
        var baidu = new Baidu(() => Translated("zh", ""));
        await Service(baidu).RunAsync();

        Assert.Empty(baidu.Forms);
        Assert.Empty(Cards());
    }

    [Fact]
    public async Task RunAsync_translatesJapaneseWithKanji()
    {
        Clipboard.TestBackend = new FakeClipboard("私は学生です");
        var baidu = new Baidu(() => Translated("jp", "我是学生"));
        await Service(baidu).RunAsync();

        Assert.Equal(1, baidu.Forms.Count);
        Assert.Equal("ja", Assert.Single(Cards()).Lang);
    }

    [Fact]
    public async Task RunAsync_translatesKorean()
    {
        Clipboard.TestBackend = new FakeClipboard("안녕하세요");
        var baidu = new Baidu(() => Translated("kor", "你好"));
        await Service(baidu).RunAsync();

        Assert.Equal(1, baidu.Forms.Count);
        Assert.Equal("ko", Assert.Single(Cards()).Lang);
    }

    [Fact]
    public async Task RunAsync_doesNothingWhenNotConfigured()
    {
        var clip = new FakeClipboard("hello");
        Clipboard.TestBackend = clip;
        var baidu = new Baidu(() => Translated("en", "你好"));
        // no key: the feature is not ready, so the hotkey has nothing to do
        await Service(baidu, """{"translate":{"enabled":true,"appId":"1"}}""").RunAsync();

        Assert.Empty(baidu.Forms);
        Assert.Empty(Cards());
        Assert.Equal(0, clip.Reads);
    }

    [Fact]
    public async Task RunAsync_endsQuietlyWhenTheClipboardIsBusy()
    {
        Clipboard.TestBackend = new BusyClipboard();
        var baidu = new Baidu(() => Translated("en", "你好"));
        await Service(baidu).RunAsync();

        Assert.Empty(baidu.Forms);
        Assert.Empty(Cards());
    }

    [Fact]
    public async Task RunAsync_cutsLongText()
    {
        var text = new string('a', 1500);
        Clipboard.TestBackend = new FakeClipboard(text);
        var baidu = new Baidu(() => Translated("en", "许多"));
        await Service(baidu).RunAsync();

        // "…" in UTF-8 is E2 80 A6
        Assert.StartsWith("q=" + new string('a', 999) + "%E2%80%A6&", baidu.Forms.Single());
        var card = Assert.Single(Cards());
        Assert.Equal(new string('a', 999) + "…", card.Source);
    }

    [Fact]
    public async Task RunAsync_emitsAProviderError()
    {
        Clipboard.TestBackend = new FakeClipboard("hello");
        var baidu = new Baidu(() => Ok(new { error_code = "52003", error_msg = "UNAUTHORIZED USER" }));
        await Service(baidu).RunAsync();

        var card = Assert.Single(Cards());
        Assert.Equal("provider", card.Error);
        Assert.Equal("52003 UNAUTHORIZED USER", card.Detail);
        Assert.Equal(TranslateService.ErrorSeconds, card.Seconds);
        Assert.Null(card.Target);
        Assert.DoesNotContain("source", RawCard()); // an error card shows no text
    }

    [Fact]
    public async Task RunAsync_emitsANetworkError()
    {
        Clipboard.TestBackend = new FakeClipboard("hello");
        var baidu = new Baidu(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Service(baidu).RunAsync();

        var card = Assert.Single(Cards());
        Assert.Equal("network", card.Error);
        Assert.Equal(TranslateService.ErrorSeconds, card.Seconds);
    }

    [Fact]
    public async Task RunAsync_neverThrows()
    {
        Clipboard.TestBackend = new FakeClipboard("hello");
        // a failure Baidu's own mapping does not know: the run still ends quietly, with no event
        var baidu = new Baidu(() => throw new InvalidOperationException("boom"));
        await Service(baidu).RunAsync();
        Assert.Empty(Cards());
    }

    [Fact]
    public async Task RunAsync_newPressSupersedesTheOneInFlight()
    {
        Clipboard.TestBackend = new FakeClipboard("first");
        var baidu = new HeldBaidu();
        var service = Service(baidu);

        var first = service.RunAsync();
        await Until(() => baidu.Held.Count == 1); // the first request is on its way to Baidu
        Clipboard.TestBackend = new FakeClipboard("second");
        var second = service.RunAsync();
        await Until(() => baidu.Held.Count == 2); // the new press has taken over

        baidu.Held[1].TrySetResult(Translated("en", "第二句"));
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(baidu.Held[0].Task.IsCanceled); // nobody waits for the text that was replaced
        var card = Assert.Single(Cards());
        Assert.Equal("second", card.Source);
    }

    [Fact]
    public async Task RunAsync_keepsRequestsASecondApart()
    {
        Clipboard.TestBackend = new FakeClipboard("first");
        var answers = new Queue<HttpResponseMessage>([Translated("en", "一"), Translated("en", "二")]);
        var baidu = new Baidu(() => answers.Dequeue());
        var service = Service(baidu);

        var gap = TranslateService.MinGap;
        TranslateService.MinGap = TimeSpan.FromMilliseconds(300);
        try
        {
            await service.RunAsync();
            Clipboard.TestBackend = new FakeClipboard("second");
            await service.RunAsync();
        }
        finally { TranslateService.MinGap = gap; }

        Assert.Equal(2, baidu.Forms.Count);
        Assert.True(baidu.Started[1] - baidu.Started[0] >= 250, $"only {baidu.Started[1] - baidu.Started[0]} ms apart");
    }

    [Fact]
    public async Task RunAsync_neverLogsTheTextOrTheKey()
    {
        Clipboard.TestBackend = new FakeClipboard("Hello secret word");
        var baidu = new Baidu(() => Translated("en", "机密词"));
        var logger = new Capture();
        await Service(baidu, logger: logger).RunAsync();

        // one line per run, with the length, the duration and the outcome — never the text
        var line = Assert.Single(logger.Lines, l => l.Contains("translate:"));
        Assert.Contains("17 chars", line);
        Assert.Contains("ms", line);
        Assert.All(logger.Lines, l => Assert.DoesNotContain("secret word", l));
        Assert.All(logger.Lines, l => Assert.DoesNotContain("机密词", l));

        // and the same for a failure
        logger = new Capture();
        baidu = new Baidu(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Service(baidu, logger: logger).RunAsync();
        line = Assert.Single(logger.Lines, l => l.Contains("translate:"));
        Assert.Contains("network", line);
        Assert.All(logger.Lines, l => Assert.DoesNotContain("secret word", l));
    }

    [Fact]
    public void HotkeySpec_returnsTheHotkeyWhenEnabledAndConfigured()
    {
        var cfg = JsonSerializer.Deserialize<AgentConfig>("""{"translate":{"enabled":true,"appId":"1","key":"s","hotkey":"ctrl+shift+t"}}""", ConfigStore.ReadOpts)!;
        Assert.Equal("ctrl+shift+t", TranslateService.HotkeySpec(cfg));
    }

    [Fact]
    public void HotkeySpec_isNullWithoutAppId()
    {
        var cfg = JsonSerializer.Deserialize<AgentConfig>("""{"translate":{"enabled":true,"key":"s"}}""", ConfigStore.ReadOpts)!;
        Assert.Null(TranslateService.HotkeySpec(cfg));
    }

    [Fact]
    public void HotkeySpec_isNullWithoutKey()
    {
        var cfg = JsonSerializer.Deserialize<AgentConfig>("""{"translate":{"enabled":true,"appId":"1"}}""", ConfigStore.ReadOpts)!;
        Assert.Null(TranslateService.HotkeySpec(cfg));
    }

    [Fact]
    public void HotkeySpec_isNullWhenDisabled()
    {
        var cfg = JsonSerializer.Deserialize<AgentConfig>("""{"translate":{"enabled":false,"appId":"1","key":"s"}}""", ConfigStore.ReadOpts)!;
        Assert.Null(TranslateService.HotkeySpec(cfg));
    }

    [Fact]
    public void Start_registersTheHotkey()
    {
        var applied = new List<string?>();
        var service = Service(new Baidu(() => Translated("en", "你好")));
        service.TestApply = spec => { applied.Add(spec); return null; }; // "registered"
        service.Start();
        Assert.Equal(["ctrl+alt+d"], applied);
    }

    [Fact]
    public void SettingsSaved_registersTheHotkeyAgain()
    {
        var applied = new List<string?>();
        var configPath = Path.Combine(_dir, "config.json");
        File.WriteAllText(configPath, """{"translate":{"enabled":true,"appId":"1","key":"s","hotkey":"ctrl+alt+d"}}""");
        var service = Service(new Baidu(() => Translated("en", "你好"))); // writes the same config once more
        service.TestApply = spec => { applied.Add(spec); return null; };
        service.Start();

        File.WriteAllText(configPath, """{"translate":{"enabled":true,"appId":"1","key":"s","hotkey":"ctrl+shift+t"}}""");
        // the store re-reads the file only when its stamp moved: make sure it did (the writes above can share one)
        File.SetLastWriteTimeUtc(configPath, DateTime.UtcNow.AddSeconds(5));
        service.SettingsSaved();
        Assert.Equal(["ctrl+alt+d", "ctrl+shift+t"], applied);
    }

    [Fact]
    public void Status_reportsActiveAndTheHotkeyState()
    {
        var service = Service(new Baidu(() => Translated("en", "你好")));
        service.TestApply = _ => null; // the hotkey was taken: registered
        service.Start();
        var json = JsonSerializer.Serialize(service.Status(), ConfigStore.WriteOpts);
        Assert.Contains("\"active\": true", json);
        Assert.Contains("registered", json);

        // off: the switch is off in the config
        service = Service(new Baidu(() => Translated("en", "你好")), """{"translate":{"enabled":false}}""");
        service.Start();
        json = JsonSerializer.Serialize(service.Status(), ConfigStore.WriteOpts);
        Assert.Contains("\"active\": false", json);
        Assert.Contains("off", json);
    }

    [Fact]
    public async Task TestAsync_returnsTheTranslation()
    {
        var baidu = new Baidu(() => Translated("en", "你好"));
        var (result, error) = await Service(baidu).TestAsync("1", "s", CancellationToken.None);
        Assert.Equal("你好", result);
        Assert.Null(error);
        // "hello" is what it asks Baidu for
        Assert.Contains("q=hello&", baidu.Forms.Single());

        // the given credentials are used; without them, the saved ones
        baidu = new Baidu(() => Translated("en", "你好"));
        await Service(baidu).TestAsync(null, null, CancellationToken.None);
        Assert.Contains("appid=1&", baidu.Forms.Single());
    }

    [Fact]
    public async Task TestAsync_reportsAnError()
    {
        var baidu = new Baidu(() => Ok(new { error_code = "52003", error_msg = "UNAUTHORIZED USER" }));
        var (result, error) = await Service(baidu).TestAsync("1", "s", CancellationToken.None);
        Assert.Null(result);
        Assert.Equal("52003 UNAUTHORIZED USER", error);
    }
}
