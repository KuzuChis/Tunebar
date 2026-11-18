using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace MediaTray.Windows;

/// <summary>Reads and sets the volume of one app through the Windows Core Audio sessions.</summary>
public sealed class AppVolumeController
{
    /// <summary>The volume of the app (0..1), or null when it has no audio session.</summary>
    public double? GetVolume(string processName)
    {
        double? result = null;
        ForEachSession(processName, session =>
        {
            result ??= session.SimpleAudioVolume.Volume;
        });
        return result;
    }

    /// <summary>Sets the volume on every audio session of the app. Returns true if at least one was changed.</summary>
    public bool SetVolume(string processName, double volume)
    {
        var level = (float)Math.Clamp(volume, 0.0, 1.0);
        var changed = false;
        ForEachSession(processName, session =>
        {
            session.SimpleAudioVolume.Volume = level;
            changed = true;
        });
        return changed;
    }

    /// <summary>Runs <paramref name="action"/> for each session on the default speakers that belongs to the process name.</summary>
    private static void ForEachSession(string processName, Action<AudioSessionControl> action)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];
                    if (BelongsTo(session, processName)) action(session);
                }
                catch (Exception)
                {
                    // One broken session (for example a process that just exited) is skipped.
                }
            }
        }
        catch (Exception)
        {
            // No audio device or Core Audio not available: behave as "no sessions".
        }
    }

    private static bool BelongsTo(AudioSessionControl session, string processName)
    {
        try
        {
            var pid = (int)session.GetProcessID;
            if (pid == 0) return false; // system sounds
            using var process = Process.GetProcessById(pid);
            return string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false; // the process has exited
        }
    }
}
