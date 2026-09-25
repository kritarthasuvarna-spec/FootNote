using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FootNote.App.Settings;
using FootNote.Core;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace FootNote.App;

/// <summary>
/// Floating "search your notes" window, opened by the search hotkey. Modeled
/// on <see cref="OverlayBar"/> (borderless, topmost, acrylic-capable) but
/// centered rather than edge-docked, and takes real keyboard focus immediately
/// since it's a text box, not a passive readout.
/// Enter runs the search; Enter/click on a result reveals it in Explorer and
/// closes; Esc closes without acting and clears state so the next open starts fresh.
/// </summary>
public partial class SearchBar : Window
{
    /// <summary>Raised when a result couldn't be revealed in Explorer, so the
    /// app can show a toast (this window is about to close and can't show its own).</summary>
    public event Action? RevealFailed;

    private IntPtr _hwnd;
    private CancellationTokenSource? _introCts;

    public SearchBar()
    {
        InitializeComponent();
        FootNote.App.DarkTitleBar.Apply(this);
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            SetToolWindow();
            ApplySettings(); // acrylic needs the hwnd
        };

        ApplySettings();
        SettingsService.Instance.SettingsChanged += ApplySettings;

        Deactivated += (_, _) => HideAndClear();
        PreviewKeyDown += (_, e) =>
        {
            // Our own hotkey is Alt-chorded, so the Alt key can still be logically
            // "down" the instant Escape arrives — WPF then reports it as Key.System
            // with SystemKey == Escape rather than plain Key.Escape. Check both.
            bool isEscape = e.Key == Key.Escape || (e.Key == Key.System && e.SystemKey == Key.Escape);
            if (isEscape) { HideAndClear(); e.Handled = true; }
        };
        QueryBox.TextChanged += (_, _) =>
        {
            bool hasText = QueryBox.Text.Length > 0;
            HintRow.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            // The user started typing for real — abandon the decorative
            // blink/typewriter intro immediately rather than let it fight
            // with what they're actually entering.
            if (hasText) _introCts?.Cancel();
        };
    }

    /// <summary>Pulls live accent/panel/translucency values from AppSettings —
    /// same fields OverlayBar reads, so the search box matches the rest of the app.</summary>
    private void ApplySettings()
    {
        var s = SettingsService.Instance.Current;
        try
        {
            Resources["AccentBrush"] = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(s.AccentColor));
        }
        catch { /* invalid hex — keep previous accent */ }

        Color panel;
        try { panel = (Color)ColorConverter.ConvertFromString(s.PanelColor); }
        catch { panel = Color.FromRgb(0x1E, 0x1E, 0x2B); }
        panel.A = s.Translucency ? (byte)0xB0 : (byte)0xF5;
        var panelBrush = new SolidColorBrush(panel);
        InputPill.Background = panelBrush;
        ResultsCard.Background = panelBrush;

        // The pill stays fully rounded (its own CornerRadius is capsule-shaped
        // by construction — half its height); only the results card follows the
        // user's configured corner radius, clamped a bit tighter than the pill.
        int r = Math.Clamp(s.CornerRadius, 8, 24);
        ResultsCard.CornerRadius = new CornerRadius(r);

        // Deliberately no NativeMethods.SetAcrylic here: DWM's blur backdrop
        // applies to the whole hwnd, which would blur straight across the gap
        // between the pill and the results card and erase the two-card look.
        // The panel brush's own alpha already carries the translucency setting.
    }

    private void SetToolWindow()
    {
        if (_hwnd == IntPtr.Zero) return;
        int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_TOOLWINDOW; // keep it out of alt-tab, like OverlayBar
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
    }

    /// <summary>Shows the box fresh (empty query, no results), centered on the
    /// monitor under the cursor, and focuses the text box.</summary>
    public void ShowFresh()
    {
        ClearState();
        Show();
        Reposition();
        Activate();
        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
        PopIn();
    }

    private void ClearState()
    {
        _introCts?.Cancel();
        QueryBox.Text = "";
        ResultsList.ItemsSource = null;
        ResultsScroll.Visibility = Visibility.Collapsed;
        ResultsCard.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Collapsed;
        HintRow.Visibility = Visibility.Visible;
        HintText.Text = "";
        BlinkCaret.Visibility = Visibility.Collapsed;
        InputPill.BeginAnimation(WidthProperty, null);
        InputPill.Width = InputPill.Height; // reset to the circle starting shape
    }

    /// <summary>Hides the window and resets it so the next hotkey press starts fresh.</summary>
    public void HideAndClear()
    {
        if (!IsVisible) return;
        var fade = new DoubleAnimation(RootGrid.Opacity, 0, TimeSpan.FromMilliseconds(90));
        fade.Completed += (_, _) => { ClearState(); Hide(); };
        RootGrid.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Replicates the reference clip's choreography: the round icon
    /// button appears and holds still for a beat — long enough to actually
    /// register as "a circle appeared" — then expands into the full pill
    /// (visibly, not a snap), then — if the user hasn't started typing — an
    /// idle caret blinks a few times before "Search your notes" types itself
    /// in character by character.</summary>
    private void PopIn()
    {
        _introCts?.Cancel();
        var cts = new CancellationTokenSource();
        _introCts = cts;

        RootGrid.BeginAnimation(UIElement.OpacityProperty, null);
        RootGrid.Opacity = 1;
        RootGrid.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(90)));

        InputPill.BeginAnimation(WidthProperty, null);
        InputPill.Width = InputPill.Height; // stay a circle through the hold below

        _ = PlayOpenSequenceAsync(cts.Token);
    }

    /// <summary>Hold as a circle, then expand, then (if still empty) run the
    /// blink/typewriter intro. Each stage is awaited in order so the hold and
    /// the expand are both genuinely visible instead of racing each other.</summary>
    private async Task PlayOpenSequenceAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token); // circle-with-icon hold

            var widen = new DoubleAnimation(InputPill.Height, 560, TimeSpan.FromMilliseconds(450))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            InputPill.BeginAnimation(WidthProperty, widen);
            await Task.Delay(450, token); // let the expand actually play out

            await PlayIntroTextAsync(token);
        }
        catch (OperationCanceledException) { /* window closed or user typed mid-sequence */ }
    }

    /// <summary>The decorative blink-then-type sequence. Bails out instantly,
    /// at any point, the moment the user types something real or the box closes.
    /// Just one short blink — the typewriter should start right as the pill
    /// finishes expanding, not after a long idle pause.</summary>
    private async Task PlayIntroTextAsync(CancellationToken token)
    {
        try
        {
            const int blinkMs = 160;
            for (int i = 0; i < 1 && QueryBox.Text.Length == 0; i++)
            {
                BlinkCaret.Visibility = Visibility.Visible;
                await Task.Delay(blinkMs, token);
                if (QueryBox.Text.Length > 0) return;
                BlinkCaret.Visibility = Visibility.Collapsed;
                await Task.Delay(blinkMs, token);
            }
            if (QueryBox.Text.Length > 0) return;

            const string full = "Search your notes";
            BlinkCaret.Visibility = Visibility.Visible;
            for (int i = 1; i <= full.Length; i++)
            {
                if (QueryBox.Text.Length > 0) return;
                HintText.Text = full[..i];
                await Task.Delay(28, token);
            }
            BlinkCaret.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { /* window closed or user typed — abandon the intro */ }
    }

    /// <summary>Centers on the monitor containing the cursor — DPI-aware, same
    /// raw-pixel SetWindowPos approach as OverlayBar.Reposition.</summary>
    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero) return;

        NativeMethods.GetCursorPos(out var pt);
        IntPtr monitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref mi)) return;

        double scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _) == 0)
            scale = dpiX / 96.0;

        int workWidthPx = mi.rcWork.Right - mi.rcWork.Left;
        int workHeightPx = mi.rcWork.Bottom - mi.rcWork.Top;
        int boxWidthPx = (int)(Width * scale);
        int boxHeightPx = (int)Math.Ceiling(ActualHeight * scale);
        if (boxHeightPx <= 0) boxHeightPx = (int)(80 * scale);

        int x = mi.rcWork.Left + (workWidthPx - boxWidthPx) / 2;
        int y = mi.rcWork.Top + (workHeightPx - boxHeightPx) / 3; // upper third reads better than dead-center

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, boxWidthPx, boxHeightPx,
            NativeMethods.SWP_NOACTIVATE);
    }

    // ---- search / results ---------------------------------------------------

    private void QueryBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && ResultsList.Items.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            ResultsList.Focus();
            (ResultsList.ItemContainerGenerator.ContainerFromIndex(0) as System.Windows.Controls.ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }

    private void RunSearch()
    {
        string query = QueryBox.Text;
        var results = NotesSearch.Search(query)
            .Select(r => new ResultRow(r, query))
            .ToList();

        if (results.Count == 0)
        {
            ResultsScroll.Visibility = Visibility.Collapsed;
            bool showEmpty = !string.IsNullOrWhiteSpace(QueryBox.Text);
            EmptyText.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
            ResultsCard.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
            ResultsList.ItemsSource = null;
            Reposition();
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        ResultsScroll.Visibility = Visibility.Visible;
        ResultsCard.Visibility = Visibility.Visible;
        ResultsList.ItemsSource = results;
        ResultsList.SelectedIndex = 0;
        Dispatcher.BeginInvoke(Reposition, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ResultsList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ResultsList.SelectedItem is ResultRow row)
        {
            Reveal(row);
            e.Handled = true;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is ResultRow row) Reveal(row);
    }

    private void ResultItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBoxItem { DataContext: ResultRow row }) Reveal(row);
    }

    private void Reveal(ResultRow row)
    {
        bool ok = NativeMethods.RevealInExplorer(row.Path);
        HideAndClear();
        if (!ok) RevealFailed?.Invoke();
    }

    /// <summary>Search-result view model for the ListBox's DataTemplate. Splits
    /// the filename into three runs around the query match so the template can
    /// bold just the matching substring, echoing the reference design's
    /// highlighted-match look.</summary>
    private sealed class ResultRow
    {
        public string Path { get; }
        public string Snippet { get; }
        public string WhenText { get; }
        public string FileNamePrefix { get; }
        public string FileNameMatch { get; }
        public string FileNameSuffix { get; }

        public ResultRow(SearchResult r, string query)
        {
            Path = r.Path;
            Snippet = r.Snippet;
            WhenText = r.NoteAt is { } at ? at.ToLocalTime().ToString("d MMM yyyy") : "";

            string fileName = System.IO.Path.GetFileName(r.Path);
            int i = string.IsNullOrWhiteSpace(query)
                ? -1
                : fileName.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
            {
                FileNamePrefix = fileName;
                FileNameMatch = "";
                FileNameSuffix = "";
            }
            else
            {
                FileNamePrefix = fileName[..i];
                FileNameMatch = fileName.Substring(i, query.Length);
                FileNameSuffix = fileName[(i + query.Length)..];
            }
        }
    }
}
