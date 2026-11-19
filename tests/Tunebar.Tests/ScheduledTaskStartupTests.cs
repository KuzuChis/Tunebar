using System.Xml.Linq;
using Tunebar.Windows;

namespace Tunebar.Tests;

public sealed class ScheduledTaskStartupTests : IDisposable
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly string _taskName = "TunebarTest-" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        try { new ScheduledTaskStartup(_taskName).Unregister(); } catch (Exception) { }
    }

    [Fact]
    public void BuildTaskXml_has_expected_shape()
    {
        var xml = ScheduledTaskStartup.BuildTaskXml(@"C:\A & B\<Tunebar>.exe", @"PC\me");
        var doc = XDocument.Parse(xml);

        Assert.Equal(@"C:\A & B\<Tunebar>.exe", doc.Descendants(Ns + "Command").Single().Value);
        Assert.Equal("PT10S", doc.Descendants(Ns + "Delay").Single().Value);
        Assert.Equal("LeastPrivilege", doc.Descendants(Ns + "RunLevel").Single().Value);
        Assert.Equal("InteractiveToken", doc.Descendants(Ns + "LogonType").Single().Value);
        Assert.All(doc.Descendants(Ns + "UserId"), e => Assert.Equal(@"PC\me", e.Value));
        Assert.Contains("&amp;", xml);
    }

    [Fact]
    public void Register_query_and_unregister_roundtrip()
    {
        var entry = new ScheduledTaskStartup(_taskName);
        const string exe = @"C:\Apps\Tunebar\Tunebar.exe";

        Assert.False(entry.IsRegisteredFor(exe));
        try
        {
            entry.Register(exe);

            Assert.True(entry.IsRegisteredFor(exe));
            Assert.False(entry.IsRegisteredFor(@"C:\Other\Tunebar.exe"));

            entry.Unregister();

            Assert.False(entry.IsRegisteredFor(exe));
            entry.Unregister(); // a missing task is not an error
        }
        finally
        {
            try { entry.Unregister(); } catch (Exception) { }
        }
    }
}
