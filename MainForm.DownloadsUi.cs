using System.Diagnostics;

namespace MBrowser;

internal sealed partial class MainForm
{
    private RoundedButton? _downloadsButton;
    private GlassPanel? _downloadsPanel;
    private FlowLayoutPanel? _downloadsRows;
    private readonly System.Windows.Forms.Timer _downloadUiTimer = new() { Interval = 850 };

    private void InitializeDownloadsToolbar()
    {
        _downloadUiTimer.Tick += (_, _) =>
        {
            if (_closing) return;
            UpdateDownloadButton();
            if (_downloadsPanel is { IsDisposed: false, Visible: true })
                RefreshDownloadsPanel();
            else if (!_cefDownloadList.Values.Any(x => x.Status is "Загрузка" or "Пауза"))
                _downloadUiTimer.Stop();
        };
        UpdateDownloadButton();
    }

    private void UpdateDownloadButton()
    {
        if (_downloadsButton is not { IsDisposed: false } button) return;
        int active = _cefDownloadList.Values.Count(x => x.Status is "Загрузка" or "Пауза");
        button.Text = active > 0 ? $"↓{active}" : "↓";
        button.AccessibleName = active > 0 ? $"Загрузки: {active} активных" : "Загрузки (Ctrl+J)";
        button.ForeColor = active > 0 ? Palette.Accent : Palette.Text;
        button.Invalidate();
        if (active > 0 && !_downloadUiTimer.Enabled) _downloadUiTimer.Start();
    }

    private void ShowCefDownloads()
    {
        if (_downloadsPanel is { IsDisposed: false })
        {
            CloseOverlay();
            return;
        }
        var panel = new GlassPanel
        {
            Size = new Size(480, 485),
            Padding = new Padding(12),
            UseArtwork = false
        };
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 42, ColumnCount = 3, RowCount = 1,
            BackColor = Color.Transparent, Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        var title = new Label
        {
            Text = "Загрузки",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = Palette.Text,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var folder = Palette.Button("Папка ↗", "Открыть папку загрузок", 100);
        folder.Dock = DockStyle.Fill;
        folder.TextAlign = ContentAlignment.MiddleCenter;
        folder.Font = new Font("Segoe UI", 9);
        folder.Margin = new Padding(2);
        folder.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(DownloadTargetDirectory);
                Process.Start(new ProcessStartInfo("explorer.exe", DownloadTargetDirectory)
                { UseShellExecute = true });
            }
            catch (Exception error) when (error is IOException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { CefDownloadError(error); }
        };
        var close = Palette.Button("×", "Закрыть загрузки", 36);
        close.Dock = DockStyle.Fill;
        close.Font = new Font("Segoe UI", 13);
        close.Margin = new Padding(2);
        close.Click += (_, _) => CloseOverlay();
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(folder, 1, 0);
        header.Controls.Add(close, 2, 0);

        var ask = new CheckBox
        {
            Dock = DockStyle.Top,
            Height = 33,
            Text = "Спрашивать, куда сохранять каждый файл",
            Checked = _preferences.AskWhereToSaveDownloads,
            ForeColor = Palette.Muted,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9)
        };
        ask.CheckedChanged += (_, _) =>
        {
            _preferences.AskWhereToSaveDownloads = ask.Checked;
            try { _preferences.Save(DataPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { CefDownloadError(error); }
        };
        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            BackColor = Color.Transparent,
            Margin = new Padding(0), Padding = new Padding(0, 4, 0, 2)
        };
        var clear = Palette.Button("Очистить завершённые загрузки из списка", "Очистить историю загрузок", 440);
        clear.Dock = DockStyle.Bottom;
        clear.Height = 32;
        clear.TextAlign = ContentAlignment.MiddleCenter;
        clear.Font = new Font("Segoe UI", 9);
        clear.Click += (_, _) => ClearCompletedDownloadHistory();
        panel.Controls.Add(list);
        panel.Controls.Add(clear);
        panel.Controls.Add(ask);
        panel.Controls.Add(header);

        ShowOverlay(panel, new Point(_viewport.Width - panel.Width - 14, 8));
        _downloadsPanel = panel;
        _downloadsRows = list;
        _downloadUiTimer.Start();
        RefreshDownloadsPanel();
    }

    private void RefreshDownloadsPanel()
    {
        if (_downloadsPanel is not { IsDisposed: false } panel ||
            _downloadsRows is not { IsDisposed: false } list) return;
        list.SuspendLayout();
        var previous = list.Controls.Cast<Control>().ToArray();
        list.Controls.Clear();
        foreach (var control in previous) control.Dispose();

        int rowWidth = Math.Max(240, panel.ClientSize.Width - panel.Padding.Horizontal - 24);
        var items = _cefDownloadList.Values
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Take(12)
            .ToArray();
        if (items.Length == 0)
        {
            list.Controls.Add(new Label
            {
                Text = "Здесь появятся скачанные файлы.\nCtrl+J — открыть загрузки.",
                ForeColor = Palette.Muted,
                TextAlign = ContentAlignment.MiddleCenter,
                Width = rowWidth, Height = 120, BackColor = Color.Transparent
            });
        }
        foreach (var item in items)
            list.Controls.Add(BuildDownloadCard(item, rowWidth));
        list.ResumeLayout(true);
    }

    private Control BuildDownloadCard(CefDownloadInfo item, int width)
    {
        var card = new Panel
        {
            Width = width, Height = 119, BackColor = Palette.Raised,
            Margin = new Padding(0, 0, 0, 7),
            Padding = new Padding(10, 6, 10, 4)
        };
        var name = new Label
        {
            Dock = DockStyle.Top, Height = 25, Text = item.Name,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Palette.Text,
            AutoEllipsis = true, BackColor = Color.Transparent
        };
        string received = DownloadUiPolicy.FormatSize(item.Received);
        string total = item.Total > 0 ? DownloadUiPolicy.FormatSize(item.Total) : "неизвестно";
        bool active = item.Status is "Загрузка" or "Пауза";
        string detail = active
            ? $"{received} / {total}  ·  {DownloadUiPolicy.FormatSpeed(item.Speed)}" +
              (item.Total > 0 && item.Speed > 0
                  ? "  ·  осталось " +
                    DownloadUiPolicy.FormatTimeRemaining(item.Received, item.Total, item.Speed)
                  : "")
            : $"{item.Status}  ·  {received}";
        var info = new Label
        {
            Dock = DockStyle.Top, Height = 23,
            Text = detail, ForeColor = Palette.Muted,
            Font = new Font("Segoe UI", 9),
            AutoEllipsis = true, BackColor = Color.Transparent
        };
        var progress = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 7,
            Minimum = 0, Maximum = 100,
            Value = item.Status == "Готово" ? 100 :
                DownloadUiPolicy.Percentage(item.Received, item.Total),
            Style = active && item.Total == 0
                ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous,
            MarqueeAnimationSpeed = 25
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 34,
            AutoScroll = false, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent
        };
        void Add(string caption, Action clicked)
        {
            var button = Palette.Button(caption, caption, 112);
            button.Font = new Font("Segoe UI", 9);
            button.Height = 29;
            button.Margin = new Padding(0, 2, 7, 0);
            button.BackColor = Palette.Surface;
            button.Click += (_, _) =>
            {
                try { clicked(); }
                catch (Exception error) when (error is IOException or
                    UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                { CefDownloadError(error); }
            };
            actions.Controls.Add(button);
        }
        if (active)
        {
            Add(item.Paused ? "Продолжить" : "Пауза",
                () => DownloadAction(item, item.Paused ? "resume" : "pause"));
            Add("Отменить", () => DownloadAction(item, "cancel"));
        }
        else if (item.Status == "Готово")
        {
            Add("Открыть", () => OpenDownloadedFile(item));
            Add("В папке", () => ShowDownloadedFileInFolder(item));
        }
        else Add("Открыть папку", () =>
        {
            Directory.CreateDirectory(DownloadTargetDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", DownloadTargetDirectory)
            { UseShellExecute = true });
        });
        card.Controls.Add(progress);
        card.Controls.Add(info);
        card.Controls.Add(name);
        card.Controls.Add(actions);
        actions.BringToFront();
        return card;
    }
}
