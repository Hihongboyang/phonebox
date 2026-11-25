using System.Text.Json;
using System.Text.Json.Nodes;
using PhoneBox.Agent;
using Xunit;

/// <summary>
/// Settings › Translate: the section is off by default, its values are cleaned, its key reaches neither the phone nor a
/// settings GET from outside the settings page, and a save that leaves the key out keeps the stored one.
/// </summary>
public class TranslateConfigTests
{
    private static AgentConfig Load(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"phonebox-translate-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try { return new ConfigStore(path).Get(); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Translate_hasSafeDefaults()
    {
        var t = Load("{}").Translate;
        Assert.False(t.Enabled);
        Assert.Equal("ctrl+alt+d", t.Hotkey);
        Assert.Null(t.AppId);
        Assert.Null(t.Key);
        Assert.Equal(1000, t.MaxChars);
    }

    [Fact]
    public void Normalize_translate_cleansTheValues()
    {
        var t = Load("""{"translate":{"enabled":true,"hotkey":"  ","appId":" 123 ","key":" ","maxChars":50000}}""").Translate;
        Assert.Equal("ctrl+alt+d", t.Hotkey);
        Assert.Equal("123", t.AppId);
        Assert.Null(t.Key);
        Assert.Equal(6000, t.MaxChars);
        Assert.Equal(100, Load("""{"translate":{"maxChars":5}}""").Translate.MaxChars);
    }

    /// <summary>A guard: it passes from the start, because <see cref="AgentConfig.ToPublic"/> is an explicit list.</summary>
    [Fact]
    public void ToPublic_keepsTranslateOut()
    {
        var config = Load("""{"translate":{"enabled":true,"appId":"1","key":"secret"}}""");
        var pub = JsonSerializer.SerializeToElement(config.ToPublic(), ConfigStore.WriteOpts);
        Assert.False(pub.TryGetProperty("translate", out _));
        Assert.DoesNotContain("secret", pub.GetRawText());
    }

    [Fact]
    public void PreserveTranslateKey_keepsTheStoredKey()
    {
        // the section left out, as by a client that was given the settings without the key
        var proposed = JsonSerializer.Deserialize<AgentConfig>("{}", ConfigStore.ReadOpts)!;
        Assert.True(ConfigStore.PreserveTranslateKey(proposed, "stored-secret"));
        Assert.Equal("stored-secret", proposed.Translate.Key);

        proposed = JsonSerializer.Deserialize<AgentConfig>("""{"translate":{"key":null}}""", ConfigStore.ReadOpts)!;
        Assert.True(ConfigStore.PreserveTranslateKey(proposed, "stored-secret"));
        Assert.Equal("stored-secret", proposed.Translate.Key);
    }

    [Fact]
    public void PreserveTranslateKey_leavesOtherSavesAlone()
    {
        var proposed = JsonSerializer.Deserialize<AgentConfig>("""{"translate":{"key":"new"}}""", ConfigStore.ReadOpts)!;
        Assert.False(ConfigStore.PreserveTranslateKey(proposed, "stored-secret"));
        Assert.Equal("new", proposed.Translate.Key);

        proposed = JsonSerializer.Deserialize<AgentConfig>("{}", ConfigStore.ReadOpts)!;
        Assert.False(ConfigStore.PreserveTranslateKey(proposed, null));
        Assert.Null(proposed.Translate.Key);

        // an empty key clears it, as for Steam
        proposed.Translate.Key = "";
        Assert.False(ConfigStore.PreserveTranslateKey(proposed, "stored-secret"));
        Assert.Equal("", proposed.Translate.Key);
    }

    [Fact]
    public void RemoveSecrets_hidesTheSteamAndTranslateKeys()
    {
        var settings = (JsonSerializer.SerializeToNode(Load("""{"translate":{"appId":"1","key":"secret"},"steam":{"apiKey":"steam-secret","enabled":true}}"""), ConfigStore.WriteOpts) as JsonObject)!;
        ConfigStore.RemoveSecrets(settings);

        var translate = (JsonObject)settings["translate"]!;
        Assert.Null(translate["key"]);
        Assert.Equal("1", (string)translate["appId"]!);
        Assert.False((bool)translate["enabled"]!);
        Assert.Equal("ctrl+alt+d", (string)translate["hotkey"]!);

        var steam = (JsonObject)settings["steam"]!;
        Assert.Null(steam["apiKey"]);
        Assert.True((bool)steam["enabled"]!);

        ConfigStore.RemoveSecrets(null); // nothing to hand out: nothing happens
    }

    [Theory]
    [InlineData("config.default.json")]
    [InlineData("config.default.linux.json")]
    [InlineData("config.default.macos.json")]
    public void DefaultConfigFiles_carryTheTranslateSection(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        var json = File.ReadAllText(path);
        Assert.Contains("\"translate\"", json);
        var cfg = Load(json);
        Assert.False(cfg.Translate.Enabled);
        Assert.Equal("ctrl+alt+d", cfg.Translate.Hotkey);
        Assert.Equal(1000, cfg.Translate.MaxChars);
    }
}
