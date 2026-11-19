using Tunebar.Ui;

namespace Tunebar.Tests;

public class HotkeyMapTests
{
    [Fact]
    public void Table_has_the_five_actions_with_their_keys()
    {
        var keys = HotkeyMap.Bindings.ToDictionary(b => b.Action, b => b.VirtualKey);

        Assert.Equal(5, HotkeyMap.Bindings.Count);
        Assert.Equal(0x20u, keys[HotkeyAction.PlayPause]);
        Assert.Equal(0x27u, keys[HotkeyAction.Next]);
        Assert.Equal(0x25u, keys[HotkeyAction.Prev]);
        Assert.Equal(0x26u, keys[HotkeyAction.VolumeUp]);
        Assert.Equal(0x28u, keys[HotkeyAction.VolumeDown]);
    }

    [Fact]
    public void Modifiers_are_ctrl_alt_shift()
    {
        Assert.Equal(0x7u, HotkeyMap.Modifiers);
    }

    [Fact]
    public void NoRepeat_only_on_play_next_prev()
    {
        foreach (var b in HotkeyMap.Bindings)
        {
            var expected = b.Action is HotkeyAction.PlayPause or HotkeyAction.Next or HotkeyAction.Prev;
            Assert.Equal(expected, b.NoRepeat);
            Assert.Equal(expected, (HotkeyMap.FlagsFor(b) & HotkeyMap.ModNoRepeat) != 0);
            Assert.Equal(HotkeyMap.Modifiers, HotkeyMap.FlagsFor(b) & HotkeyMap.Modifiers);
        }
    }

    [Fact]
    public void Display_texts()
    {
        var texts = HotkeyMap.Bindings.ToDictionary(b => b.Action, b => b.DisplayText);

        Assert.Equal("Ctrl+Alt+Shift+Spasi", texts[HotkeyAction.PlayPause]);
        Assert.Equal("Ctrl+Alt+Shift+→", texts[HotkeyAction.Next]);
        Assert.Equal("Ctrl+Alt+Shift+←", texts[HotkeyAction.Prev]);
        Assert.Equal("Ctrl+Alt+Shift+↑", texts[HotkeyAction.VolumeUp]);
        Assert.Equal("Ctrl+Alt+Shift+↓", texts[HotkeyAction.VolumeDown]);
    }
}
