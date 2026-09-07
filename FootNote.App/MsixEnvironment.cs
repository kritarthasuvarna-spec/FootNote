namespace FootNote.App;

/// <summary>
/// Detects whether this process is running inside an MSIX package, and — if
/// so — provides the packaged-native equivalents of the things InstallHelper.
/// Uses Windows.ApplicationModel.StartupTask via the WinRT projections that
/// come free with the versioned TargetFramework (net8.0-windows10.0.19041.0)
/// — no separate WinRT/contracts NuGet package needed, and one shouldn't be
/// added: it conflicts with the SDK-provided projections and fails to build.
/// otherwise does by hand (Run-key write, manual Apps &amp; Features entry,
/// manual Start Menu shortcut). All three of those are either pointless or
/// actively wrong inside an MSIX container:
///   - a Run-key write is registry-virtualized per-package, so it doesn't
///     produce real system-wide startup behavior the way it does unpackaged
///   - MSIX/Store owns the Apps &amp; Features entry; a second manual one is a
///     duplicate the OS didn't ask for
///   - MSIX generates the Start Menu tile from the manifest automatically;
///     a hand-built .lnk is redundant
/// </summary>
internal static class MsixEnvironment
{
    private static bool? _isPackaged;

    /// <summary>True only when running as an installed MSIX (Store or sideloaded).
    /// A plain unpackaged EXE (the Git build, or this same EXE run straight from
    /// a folder) always gets false, cheaply and without throwing anywhere visible.</summary>
    public static bool IsPackaged
    {
        get
        {
            if (_isPackaged is bool cached) return cached;
            try
            {
                // Package.Current throws outside a packaged process — that
                // exception is the actual detection mechanism; there is no
                // public "am I packaged" bool from the platform.
                _ = Windows.ApplicationModel.Package.Current.Id.FullName;
                _isPackaged = true;
            }
            catch
            {
                _isPackaged = false;
            }
            return _isPackaged.Value;
        }
    }

    /// <summary>Task id must match the &lt;desktop:StartupTask TaskId="..."&gt;
    /// declared in Package.appxmanifest exactly.</summary>
    private const string StartupTaskId = "FootNoteStartup";

    /// <summary>Enables/disables the packaged startup task. No-op (returns
    /// false) if called unpackaged — callers should check IsPackaged first
    /// and fall back to InstallHelper.SetStartup there instead.</summary>
    public static async Task<bool> TrySetStartupAsync(bool enabled)
    {
        if (!IsPackaged) return false;
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
            if (enabled)
            {
                if (task.State is Windows.ApplicationModel.StartupTaskState.Disabled)
                    await task.RequestEnableAsync();
            }
            else
            {
                task.Disable();
            }
            return true;
        }
        catch
        {
            // Most commonly: the user disabled it from Task Manager's Startup
            // tab, which the app can't override — that's by design, not a bug.
            return false;
        }
    }
}
