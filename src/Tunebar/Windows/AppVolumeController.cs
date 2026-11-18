using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace Tunebar.Windows;

/// <summary>
/// Reads and sets the volume of one app through the Windows Core Audio sessions.
/// Keeps one device enumerator for its whole life and remembers process names by pid,
/// so the 1 s refresh does not recreate COM objects and processes every time.
/// </summary>
public sealed class AppVolumeController : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<int, string> _processNames = new();
    private MMDeviceEnumerator? _enumerator;
    private bool _disposed;

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
    private void ForEachSession(string processName, Action<AudioSessionControl> action)
    {
        lock (_lock)
        {
            if (_disposed) return;
            try
            {
                _enumerator ??= new MMDeviceEnumerator();
                using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = device.AudioSessionManager.Sessions;
                var seenPids = new HashSet<int>();
                for (var i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        using var session = sessions[i];
                        seenPids.Add((int)session.GetProcessID);
                        if (BelongsTo(session, processName)) action(session);
                    }
                    catch (Exception)
                    {
                        // One broken session (for example a process that just exited) is skipped.
                    }
                }
                // Forget pids that no longer have a session (they may be reused by other programs).
                foreach (var pid in _processNames.Keys.Where(p => !seenPids.Contains(p)).ToList())
                    _processNames.Remove(pid);
            }
            catch (Exception)
            {
                // No audio device or Core Audio not available: behave as "no sessions".
            }
        }
    }

    private bool BelongsTo(AudioSessionControl session, string processName)
    {
        try
        {
            var pid = (int)session.GetProcessID;
            if (pid == 0) return false; // system sounds
            if (!_processNames.TryGetValue(pid, out var name))
            {
                using var process = Process.GetProcessById(pid);
                name = process.ProcessName;
                _processNames[pid] = name;
            }
            return string.Equals(name, processName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false; // the process has exited
        }
    }

    /// <summary>Releases the Core Audio device enumerator.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }
}
