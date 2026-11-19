using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Tunebar.Windows;

/// <summary>
/// Starts Tunebar at logon through a per-user Task Scheduler task (no admin needed).
/// The Run registry key is not used because Explorer can silently skip it.
/// </summary>
public sealed class ScheduledTaskStartup : IStartupEntry
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private const int TimeoutMs = 10_000;

    private readonly string _taskName;

    public ScheduledTaskStartup(string taskName = "Tunebar")
    {
        _taskName = taskName;
    }

    /// <summary>Builds the task definition XML. Built with XDocument so special characters are escaped.</summary>
    internal static string BuildTaskXml(string exePath, string userId)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Description", "Menjalankan Tunebar saat masuk ke Windows.")),
                new XElement(Ns + "Triggers",
                    new XElement(Ns + "LogonTrigger",
                        new XElement(Ns + "Enabled", "true"),
                        new XElement(Ns + "UserId", userId),
                        new XElement(Ns + "Delay", "PT10S"))),
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(Ns + "UserId", userId),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", "LeastPrivilege"))),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(Ns + "Enabled", "true")),
                new XElement(Ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", exePath)))));
        return doc.Declaration + Environment.NewLine + doc.Root;
    }

    /// <summary>True when the task exists and its command is this exe. Any failure counts as false.</summary>
    public bool IsRegisteredFor(string exePath)
    {
        try
        {
            var (exit, output, _) = RunSchtasks("/Query", "/TN", _taskName, "/XML");
            if (exit != 0)
                return false;
            var xml = output.Trim();
            var start = xml.IndexOf('<');
            if (start < 0)
                return false;
            var command = XDocument.Parse(xml[start..])
                .Descendants(Ns + "Exec").Elements(Ns + "Command").FirstOrDefault()?.Value;
            return command is not null
                && string.Equals(command.Trim().Trim('"'), exePath.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Creates or replaces the task. Throws when schtasks fails.</summary>
    public void Register(string exePath)
    {
        var userId = Environment.UserDomainName + "\\" + Environment.UserName;
        var file = Path.Combine(Path.GetTempPath(), "tunebar-task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(file, BuildTaskXml(exePath, userId), Encoding.Unicode);
            var (exit, output, error) = RunSchtasks("/Create", "/TN", _taskName, "/XML", file, "/F");
            if (exit != 0)
                throw new InvalidOperationException($"schtasks /Create gagal ({exit}): {error.Trim()} {output.Trim()}");
        }
        finally
        {
            try { File.Delete(file); } catch (Exception) { }
        }
    }

    /// <summary>Deletes the task. A missing task is fine; other failures throw.</summary>
    public void Unregister()
    {
        var (exit, output, error) = RunSchtasks("/Delete", "/TN", _taskName, "/F");
        if (exit == 0)
            return;
        // "The system cannot find the file specified": nothing to delete.
        if (!TaskExists())
            return;
        throw new InvalidOperationException($"schtasks /Delete gagal ({exit}): {error.Trim()} {output.Trim()}");
    }

    private bool TaskExists()
    {
        try
        {
            return RunSchtasks("/Query", "/TN", _taskName).ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static (int ExitCode, string Output, string Error) RunSchtasks(params string[] args)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("schtasks tidak bisa dijalankan.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            throw new TimeoutException("schtasks tidak selesai dalam 10 detik.");
        }
        process.WaitForExit();
        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}
