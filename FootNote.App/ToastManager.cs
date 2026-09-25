using FootNote.Core;

namespace FootNote.App;

/// <summary>
/// Failure feedback (spec: "every time, not once"). All toasts here fire on
/// every occurrence and only ever from an active hotkey attempt or a save —
/// never from passive browsing, which would spam on every click.
/// The toast disappears in seconds; the matching ERROR log line doesn't.
/// </summary>
internal sealed class ToastManager
{
    private readonly TrayIcon _tray;

    public ToastManager(TrayIcon tray) => _tray = tray;

    private void Toast(string text, string logDetail)
    {
        _tray.ShowBalloon("FootNote", text);
        Logger.Error($"{text}{(logDetail.Length > 0 ? $" ({logDetail})" : "")}");
    }

    public void SelectSingleFile(int selectedCount) =>
        Toast("Select a single file or folder to add a comment",
            $"hotkey with {selectedCount} items selected");

    public void LocationUnsupported() =>
        Toast("This location isn't supported yet", "no available backend");

    public void SaveFailed(string reason) =>
        Toast($"Couldn't save comment: {reason}", "save failure");

    public void DataDamaged() =>
        Toast("Comment data looks damaged for this file", "unreadable comment data");

    public void HotkeyConflict(string combo) =>
        Toast($"{combo} is taken by another app — change it in Settings.", "hotkey registration failed");

    public void RevealInExplorerFailed() =>
        Toast("Couldn't open Explorer for that item", "SHOpenFolderAndSelectItems failed");
}
