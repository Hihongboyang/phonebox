using PhoneBox.Agent;
using Xunit;

/// <summary>
/// The Translate hotkey's spec: what counts as a hotkey (KeySender's key names, at least one modifier, exactly one
/// other key), and what the agent answers for one that does not.
/// </summary>
public class HotkeysTests
{
    [Fact]
    public void Parse_acceptsCtrlAltD()
    {
        var key = Hotkeys.Parse("ctrl+alt+d");
        Assert.NotNull(key);
        Assert.Equal(0x0002u | 0x0001u, key!.Value.Modifiers); // MOD_CONTROL | MOD_ALT
        Assert.Equal(0x44u, key.Value.VirtualKey);             // 'D'
    }

    [Fact]
    public void Parse_rejectsAKeyWithoutAModifier() => Assert.Null(Hotkeys.Parse("d"));

    [Theory]
    [InlineData("ctrl+")]
    [InlineData("ctrl+alt")]
    [InlineData("bananas")]
    [InlineData("")]
    [InlineData("ctrl+d+d")]
    [InlineData("ctrl+d, ctrl+e")]
    public void Parse_rejectsIncompleteOrUnknownKeys(string spec) => Assert.Null(Hotkeys.Parse(spec));

    [Fact]
    public void Apply_reportsAnInvalidSpec()
    {
        var hotkeys = new Hotkeys();
        Assert.Equal(Hotkeys.Invalid, hotkeys.Apply("ctrl+"));
        Assert.Equal(Hotkeys.Invalid, hotkeys.State);
    }
}
