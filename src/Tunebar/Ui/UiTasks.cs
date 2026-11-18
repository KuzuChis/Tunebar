using System.Diagnostics;

namespace Tunebar.Ui;

public static class UiTasks
{
    /// <summary>
    /// Starts a command without waiting for it. Errors go to the debug output
    /// instead of crashing the app.
    /// </summary>
    public static async void Fire(Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Command failed: {ex}");
        }
    }
}
