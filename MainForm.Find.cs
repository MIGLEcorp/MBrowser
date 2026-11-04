using CefSharp;
using CefSharp.Handler;

namespace MBrowser;

/// <summary>
/// Native Blink find-in-page. Unlike a DOM TextWalker this correctly handles
/// shadow trees and nested frame content that Chromium exposes to find.
/// </summary>
internal sealed class CefPageFindHandler(MainForm owner, Guid tabId) : FindHandler
{
    protected override void OnFindResult(
        IWebBrowser chromiumWebBrowser, IBrowser browser, int identifier, int count,
        CefSharp.Structs.Rect selectionRect, int activeMatchOrdinal, bool finalUpdate)
    {
        owner.DispatchCefUi(() =>
        {
            owner.CefFindResult(tabId, count, activeMatchOrdinal, finalUpdate);
            return Task.CompletedTask;
        });
    }
}

internal sealed partial class MainForm
{
    private GlassPanel? _findPanel;
    private TextBox? _findInput;
    private Label? _findMatchesLabel;
    private CheckBox? _findMatchCase;
    private Guid _findTabId;
    private readonly System.Windows.Forms.Timer _findDebounce = new() { Interval = 160 };
    private string _findCurrentQuery = "";

    internal bool FindBarVisibleForTest => _findPanel is { IsDisposed: false, Visible: true };

    internal async Task<bool> CefFindSmokeForTestAsync()
    {
        ShowFindBar();
        if (_findInput is null || _findMatchesLabel is null) return false;
        _findInput.Text = "CEF";
        bool matched = false;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            await Task.Delay(175);
            if (_findMatchesLabel is null) break;
            if (_findMatchesLabel.Text.Contains(" / ") &&
                !_findMatchesLabel.Text.EndsWith(" / 0", StringComparison.Ordinal) &&
                _findMatchesLabel.Text != "0 / 0")
            { matched = true; break; }
        }
        if (matched) FindNext(true);
        CloseFindBar();
        return matched && !FindBarVisibleForTest;
    }

    private void ShowFindBar()
    {
        if (_findPanel is { IsDisposed: false })
        {
            _findInput?.Focus();
            _findInput?.SelectAll();
            return;
        }

        // The native CEF find engine needs an instantiated view.
        CloseOverlay();
        var panel = new GlassPanel
        {
            UseArtwork = false,
            Size = new Size(472, 64),
            Padding = new Padding(9, 10, 9, 10)
        };
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent,
            ColumnCount = 6, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var input = new TextBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            BackColor = Palette.Raised, ForeColor = Palette.Text,
            Font = new Font("Segoe UI", 11),
            PlaceholderText = "Найти на странице",
            AccessibleName = "Поиск по странице"
        };
        var inputHost = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Raised, Padding = new Padding(9, 12, 5, 2), Margin = new Padding(0, 0, 4, 0) };
        inputHost.Controls.Add(input);
        var result = new Label
        {
            Text = "0 / 0", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Palette.Muted, BackColor = Color.Transparent,
            AutoEllipsis = true, Font = new Font("Segoe UI", 9)
        };
        var previous = Palette.Button("↑", "Предыдущее совпадение (Shift+Enter)", 36);
        var next = Palette.Button("↓", "Следующее совпадение (Enter)", 36);
        var close = Palette.Button("×", "Закрыть поиск (Escape)", 36);
        foreach (var button in new[] { previous, next, close })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(1);
            button.BackColor = Palette.Surface;
            button.Font = new Font("Segoe UI", 13);
        }
        row.Controls.Add(inputHost, 0, 0);
        row.Controls.Add(result, 1, 0);
        row.Controls.Add(previous, 3, 0);
        row.Controls.Add(next, 4, 0);
        row.Controls.Add(close, 5, 0);
        panel.Controls.Add(row);

        _findPanel = panel;
        _findInput = input;
        _findMatchesLabel = result;
        _findTabId = ActiveTab.Id;
        _findCurrentQuery = "";
        _findDebounce.Stop();
        _findDebounce.Tick -= OnFindDebounce;
        _findDebounce.Tick += OnFindDebounce;
        input.TextChanged += (_, _) =>
        {
            _findDebounce.Stop();
            _findDebounce.Start();
        };
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.F3)
            {
                FindNext(!e.Shift);
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                CloseFindBar();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        previous.Click += (_, _) => FindNext(false);
        next.Click += (_, _) => FindNext(true);
        close.Click += (_, _) => CloseFindBar();

        // A separate small case control does not consume the search input width.
        var caseToggle = new CheckBox
        {
            Text = "Aa", AutoSize = true, BackColor = Palette.Surface,
            ForeColor = Palette.Text, Appearance = Appearance.Button,
            AccessibleName = "Учитывать регистр"
        };
        caseToggle.Dock = DockStyle.Fill;
        caseToggle.Margin = new Padding(1);
        caseToggle.Font = new Font("Segoe UI", 9);
        row.Controls.Add(caseToggle, 2, 0);
        _findMatchCase = caseToggle;
        caseToggle.CheckedChanged += (_, _) => StartFind(false);
        // Add case sensitivity as a tooltip+context menu so the bar fits narrower windows.
        var caseMenu = new ContextMenuStrip();
        caseMenu.Items.Add("Учитывать регистр", null, (_, _) =>
        {
            caseToggle.Checked = !caseToggle.Checked;
            StartFind(false);
        });
        input.ContextMenuStrip = caseMenu;
        panel.Disposed += (_, _) => caseMenu.Dispose();
        _viewport.Controls.Add(panel);
        PositionFindBar();
        panel.BringToFront();
        input.Focus();
    }

    private void PositionFindBar()
    {
        if (_findPanel is not { IsDisposed: false } panel) return;
        panel.Width = Math.Min(472, Math.Max(260, _viewport.ClientSize.Width - 20));
        panel.Location = new Point(Math.Max(8, _viewport.ClientSize.Width - panel.Width - 14), 8);
    }

    private void OnFindDebounce(object? sender, EventArgs e)
    {
        _findDebounce.Stop();
        StartFind(false);
    }

    private void StartFind(bool findNext)
    {
        if (_findPanel is null || _findTabId != ActiveTab.Id || _findInput is null) return;
        string query = _findInput.Text;
        if (!_cefRuntimes.TryGetValue(_findTabId, out var runtime) ||
            !CefIsAlive(runtime) || !runtime.View.IsBrowserInitialized)
        {
            if (_findMatchesLabel is not null) _findMatchesLabel.Text = "—";
            return;
        }
        if (query.Length == 0)
        {
            _findCurrentQuery = "";
            runtime.View.StopFinding(true);
            if (_findMatchesLabel is not null) _findMatchesLabel.Text = "0 / 0";
            return;
        }
        if (!findNext || query != _findCurrentQuery)
        {
            _findCurrentQuery = query;
            if (_findMatchesLabel is not null) _findMatchesLabel.Text = "…";
            runtime.View.Find(query, true, _findMatchCase?.Checked ?? false, false);
        }
        else runtime.View.Find(query, true, _findMatchCase?.Checked ?? false, true);
    }

    private void FindNext(bool forward)
    {
        _findDebounce.Stop();
        if (_findPanel is null || _findInput is null || _findInput.Text.Length == 0) return;
        if (!_cefRuntimes.TryGetValue(_findTabId, out var runtime) ||
            !CefIsAlive(runtime) || !runtime.View.IsBrowserInitialized) return;
        string query = _findInput.Text;
        bool findNext = query == _findCurrentQuery;
        _findCurrentQuery = query;
        runtime.View.Find(query, forward, _findMatchCase?.Checked ?? false, findNext);
    }

    internal void CefFindResult(Guid tabId, int count, int activeOrdinal, bool finalUpdate)
    {
        if (tabId != _findTabId || _findPanel is null || _findMatchesLabel is null) return;
        if (!finalUpdate && count == 0) return;
        _findMatchesLabel.Text = count == 0 ? "0 / 0" : $"{activeOrdinal} / {count}";
        _findMatchesLabel.ForeColor = count == 0 ? Color.IndianRed : Palette.Muted;
    }

    private void CloseFindBar()
    {
        _findDebounce.Stop();
        if (_cefRuntimes.TryGetValue(_findTabId, out var runtime) &&
            CefIsAlive(runtime) && runtime.View.IsBrowserInitialized)
        {
            try { runtime.View.StopFinding(true); }
            catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
            { Program.LogError(DataPath, error); }
        }
        var old = _findPanel;
        _findPanel = null;
        _findInput = null;
        _findMatchesLabel = null;
        _findMatchCase = null;
        _findCurrentQuery = "";
        if (old is null) return;
        _viewport.Controls.Remove(old);
        old.Dispose();
    }
}
