using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FootNote.Core;

namespace FootNote.App;

/// <summary>Tray icon + context menu (the app's only always-visible surface).</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private string? _balloonUrl;
    private Action? _balloonAction;

    public TrayIcon(IndexStore index, Action onExit, Action onSettings, Action onTutorial, Action onWhatsNew, Action onRecoverNotes)
    {
        string version = typeof(TrayIcon).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"FootNote v{version}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => onSettings()));
        menu.Items.Add(new ToolStripMenuItem("Show tutorial", null, (_, _) => onTutorial()));
        menu.Items.Add(new ToolStripMenuItem("What's New", null, (_, _) => onWhatsNew()));
        if (ProFeatures.Current.Unlocked)
            menu.Items.Add(new ToolStripMenuItem("Recover Notes…", null, (_, _) => onRecoverNotes()));
        menu.Items.Add(new ToolStripMenuItem("Open logs folder", null, (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(FootNote.Core.Logger.LogsDirectory);
                Process.Start(new ProcessStartInfo(FootNote.Core.Logger.LogsDirectory) { UseShellExecute = true });
            }
            catch { }
        }));

        var startup = new ToolStripMenuItem("Start with Windows")
        {
            Checked = index.StartWithWindows,
            CheckOnClick = true,
        };
        startup.CheckedChanged += (_, _) =>
        {
            index.StartWithWindows = startup.Checked;
            InstallHelper.SetStartup(startup.Checked);
        };
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripMenuItem("Check for updates…", null,
            (_, _) => OpenUrl(UpdateChecker.ReleasesPage)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => onExit()));

        _icon = new NotifyIcon
        {
            Text = "FootNote — " + FootNote.App.Settings.SettingsService.Instance.Current.HotkeyDisplay + " to tag the selected file",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_balloonAction is not null) _balloonAction();
            else if (_balloonUrl is not null) OpenUrl(_balloonUrl);
        };
    }

    private static Icon LoadIcon()
    {
        try
        {
            var fromExe = Icon.ExtractAssociatedIcon(InstallHelper.ExePath);
            if (fromExe is not null) return fromExe;
        }
        catch { }
        return SystemIcons.Application;
    }

    public void ShowBalloon(string title, string text, string? openUrlOnClick = null, Action? onClick = null)
    {
        _balloonUrl = openUrlOnClick;
        _balloonAction = onClick;
        _icon.ShowBalloonTip(8000, title, text, ToolTipIcon.Info);
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
