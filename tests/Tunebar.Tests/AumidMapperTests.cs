using Tunebar.Windows;

namespace Tunebar.Tests;

public class AumidMapperTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify", "spotify", false)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify", "spotify", false)]
    [InlineData("OperaSoftware.OperaWebBrowser.1778830257", "Opera", "opera", true)]
    [InlineData("Chrome", "Chrome", "chrome", true)]
    [InlineData("MSEdge", "Edge", "msedge", true)]
    [InlineData("vlc.exe", "vlc", "vlc", false)]
    [InlineData("Foo.Bar!App", "App", null, false)]
    public void Maps_aumid(string aumid, string appName, string? processName, bool isBrowser)
    {
        Assert.Equal(appName, AumidMapper.AppName(aumid));
        Assert.Equal(processName, AumidMapper.ProcessName(aumid));
        Assert.Equal(isBrowser, AumidMapper.IsBrowser(aumid));
    }
}
