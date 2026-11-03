using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MBrowser;

internal static class NativeGpuDiagnostics
{
    [DllImport("MBrowser.GpuDiagnostics.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MB_QueryLocalVideoMemory")]
    private static extern int Query(out ulong used, out ulong budget, out ulong availableForReservation);

    [DllImport("MBrowser.GpuDiagnostics.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "MB_QuerySystemDedicatedVideoMemory")]
    private static extern int QuerySystemDedicated(out ulong bytes);

    internal static (ulong Used, ulong Budget)? Read()
    {
        try { return Query(out var used, out var budget, out _) != 0 ? (used, budget) : null; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return null; }
    }

    internal static ulong? SystemDedicatedUsage()
    {
        try { return QuerySystemDedicated(out var bytes) != 0 ? bytes : null; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return null; }
    }
}

internal sealed partial class MainForm
{
    private readonly HashSet<Guid> _hibernatedIds = [];
    private int _hibernateCount;
    private int _restoreCount;
    private double _restoreTotalMilliseconds;
    internal int HibernationCountForTest => _hibernateCount;
    internal int RestorationCountForTest => _restoreCount;

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("N0") + " МБ";

    private void SaveResourcePreferences()
    {
        try { _preferences.Save(DataPath); SetMemoryTargets(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex); }
    }

    private void ShowResourceSettings()
    {
        CloseOverlay();
        var panel = new GlassPanel { Size = new Size(435, 530) };
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(12)
        };
        Label TextRow(string text, int height = 29)
        {
            var label = new Label { Text = text, Width = 375, Height = height, ForeColor = Palette.Text,
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            content.Controls.Add(label);
            return label;
        }

        var heading = TextRow("Память и GPU", 45);
        heading.Font = new Font("Segoe UI", 15, FontStyle.Bold);
        TextRow("Режим работы вкладок:");
        var modes = new ComboBox
        {
            Width = 375, Height = 36, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Palette.Raised, ForeColor = Palette.Text, AccessibleName = "Режим экономии памяти"
        };
        modes.Items.AddRange(["Performance — быстродействие", "Balanced — баланс",
            "Low Memory — экономия RAM", "Extreme — минимум RAM"]);
        modes.SelectedIndex = (int)_preferences.MemoryMode;
        modes.SelectedIndexChanged += (_, _) =>
        {
            _preferences.MemoryMode = (MemoryMode)modes.SelectedIndex;
            SaveResourcePreferences();
        };
        content.Controls.Add(modes);
        TextRow("Процессы CEF (изменения после перезапуска):", 36);
        var subprocessModes = new ComboBox
        {
            Width = 375, Height = 36, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Palette.Raised, ForeColor = Palette.Text,
            AccessibleName = "Группировка процессов или минимальная память"
        };
        subprocessModes.Items.AddRange([
            "Авто — Compact в Low Memory / Extreme",
            "Grouped — все процессы под MBrowser.exe",
            "Compact — раздельные CEF, меньше RAM"
        ]);
        subprocessModes.SelectedIndex = (int)_preferences.SubprocessMode;
        subprocessModes.SelectedIndexChanged += (_, _) =>
        {
            _preferences.SubprocessMode = (CefSubprocessMode)subprocessModes.SelectedIndex;
            SaveResourcePreferences();
        };
        content.Controls.Add(subprocessModes);
        TextRow("Compact отключает группировку в диспетчере задач.", 30);
        TextRow("V8 / JavaScript (эксперимент, после перезапуска):", 36);
        var v8Modes = new ComboBox
        {
            Width = 375, Height = 36, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Palette.Raised, ForeColor = Palette.Text,
            AccessibleName = "Режим памяти движка JavaScript V8"
        };
        v8Modes.Items.AddRange([
            "V8 Standard — максимальная совместимость",
            "V8 Compact — экономия JS-памяти, возможны тормоза",
            "V8 Auto — Compact только в Extreme"
        ]);
        v8Modes.SelectedIndex = (int)_preferences.V8MemoryMode;
        v8Modes.SelectedIndexChanged += (_, _) =>
        {
            _preferences.V8MemoryMode = (V8MemoryMode)v8Modes.SelectedIndex;
            SaveResourcePreferences();
        };
        content.Controls.Add(v8Modes);
        TextRow("Время простоя (минуты):");
        var minutes = new NumericUpDown
        {
            Minimum = 1, Maximum = 120, Value = _preferences.UnloadAfterMinutes, Width = 130,
            BackColor = Palette.Raised, ForeColor = Palette.Text, AccessibleName = "Время до экономии памяти"
        };
        minutes.ValueChanged += (_, _) => { _preferences.UnloadAfterMinutes = (int)minutes.Value; SaveResourcePreferences(); };
        content.Controls.Add(minutes);

        CheckBox Toggle(string text, bool value, Action<bool> onChanged)
        {
            var control = new CheckBox
            {
                Text = text, Checked = value, Width = 375, Height = 39,
                BackColor = Palette.Surface, ForeColor = Palette.Text, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8), AccessibleName = text
            };
            control.CheckedChanged += (_, _) => onChanged(control.Checked);
            content.Controls.Add(control);
            return control;
        }
        Toggle("Лёгкий старт: запускать Chromium при первом сайте (после перезапуска)", _preferences.LazyCefStartup,
            value => { _preferences.LazyCefStartup = value; SaveResourcePreferences(); });
        Toggle("Очищать кэши CEF после 3 мин простоя (эксперимент)",
            _preferences.CollectJavaScriptGarbageWhenIdle,
            value => { _preferences.CollectJavaScriptGarbageWhenIdle = value; SaveResourcePreferences(); });
        Toggle("Автоматически выгружать неактивные вкладки", _preferences.AutomaticHibernate,
            value => { _preferences.AutomaticHibernate = value; SaveResourcePreferences(); });
        Toggle("Extreme: не держать страницы истории в BackForwardCache (перезапуск)",
            _preferences.ReduceHistoryPageCache,
            value => { _preferences.ReduceHistoryPageCache = value; SaveResourcePreferences(); });
        Toggle("GPU-ускорение Chromium (использовать VRAM)", _preferences.HardwareAcceleration,
            value => { _preferences.HardwareAcceleration = value; SaveResourcePreferences(); });
        if (Uri.TryCreate(ActiveTab.CurrentUrl, UriKind.Absolute, out var currentUri) &&
            currentUri.Scheme is "https" or "http")
        {
            string host = currentUri.Host.ToLowerInvariant();
            Toggle("Не выгружать автоматически: " + host,
                _preferences.NeverHibernateHosts.Contains(host, StringComparer.OrdinalIgnoreCase), enabled =>
                {
                    _preferences.NeverHibernateHosts.RemoveAll(item => item.Equals(host, StringComparison.OrdinalIgnoreCase));
                    if (enabled) _preferences.NeverHibernateHosts.Add(host);
                    SaveResourcePreferences();
                });
        }
        var restart = TextRow("После изменения GPU-ускорения перезапусти браузер.", 42);
        restart.ForeColor = Palette.Muted;
        var limitations = TextRow("VRAM используется для графики; память JS/DOM в неё не переносится.", 57);
        limitations.ForeColor = Palette.Muted;
        var diagnostics = Palette.Button("Диагностика RAM / VRAM ›", "Открыть диагностику ресурсов", 370);
        diagnostics.Height = 42;
        diagnostics.Click += (_, _) => ShowResourceDiagnostics();
        content.Controls.Add(diagnostics);
        panel.Controls.Add(content);
        ShowOverlay(panel, new Point(_viewport.Width - panel.Width - 12, 10));
        _menu = panel;
    }

    private void ShowResourceDiagnostics()
    {
        var memory = ReadMemory();
        long appWs = 0, appPrivate = 0;
        try
        {
            using var process = Process.GetCurrentProcess();
            appWs = process.WorkingSet64;
            appPrivate = process.PrivateMemorySize64;
        }
        catch (InvalidOperationException) { }
        var workerUsage = new List<(int Pid, string Name, long Working, long Private)>();
        foreach (int pid in CefProcessMetrics.GetDescendantIds(Environment.ProcessId))
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                workerUsage.Add((pid, process.ProcessName, process.WorkingSet64,
                    process.PrivateMemorySize64));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or
                System.ComponentModel.Win32Exception) { }
        }
        var topWorkers = workerUsage.OrderByDescending(p => p.Private).Take(5).ToArray();
        var gpu = NativeGpuDiagnostics.Read();
        var systemGpuUsage = NativeGpuDiagnostics.SystemDedicatedUsage();
        long available = AdaptiveResourceManager.AvailableRamBytes();
        int unloaded = _session.Tabs.Count(tab => tab.CurrentUrl is not null and not "about:blank" &&
                                                     !_cefRuntimes.ContainsKey(tab.Id));
        long sessionBytes = 0;
        try { sessionBytes = new FileInfo(Path.Combine(DataPath, "session.json")).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        ShowMenuPage("Диагностика ресурсов", rows =>
        {
            rows("Запросить освобождение кэшей CEF", () => RunUi(ShowManualMemoryReclaimAsync));
            rows("Режим: " + _preferences.MemoryMode, ShowResourceSettings);
            rows("CEF-процессы: " + CefSubprocessPolicy.Executable(_preferences.SubprocessMode,
                _preferences.MemoryMode), ShowResourceSettings);
            rows("Приложение RAM (Working Set): " + Mb(appWs), ShowResourceDiagnostics);
            rows("Приложение Private Bytes: " + Mb(appPrivate), ShowResourceDiagnostics);
            rows("Все процессы Working Set*: " + Mb(memory.WorkingSetBytes), ShowResourceDiagnostics);
            rows("Все процессы Private Bytes: " + Mb(memory.PrivateBytes), ShowResourceDiagnostics);
            rows("CEF процессы Working Set*: " + Mb(Math.Max(0, memory.WorkingSetBytes-appWs)), ShowResourceDiagnostics);
            rows("Состояние вкладок на SSD: " + (sessionBytes / 1024.0).ToString("N1") + " КБ", ShowResourceDiagnostics);
            rows("Процессов: " + memory.ProcessCount, ShowResourceDiagnostics);
            foreach (var (pid, name, working, privateBytes) in topWorkers)
                rows(name + " #" + pid + ": " + Mb(privateBytes) + " Private / " +
                    Mb(working) + " WS", ShowResourceDiagnostics);
            rows("Доступно RAM: " + (available >= 0 ? Mb(available) : "нет данных"), ShowResourceDiagnostics);
            rows("Активных CEF: " + _cefRuntimes.Count, ShowResourceDiagnostics);
            rows("Выгружено вкладок: " + unloaded, ShowResourceDiagnostics);
            rows("Выгрузок / восстановлений: " + _hibernateCount + " / " + _restoreCount, ShowResourceDiagnostics);
            rows("Среднее восстановление: " + (_restoreCount == 0 ? "—" :
                (_restoreTotalMilliseconds / _restoreCount).ToString("N0") + " мс"), ShowResourceDiagnostics);
            rows("VRAM системы (PDH)*: " + (systemGpuUsage is { } usage ? Mb((long)usage) : "нет данных"), ShowResourceDiagnostics);
            rows("Бюджет GPU для приложения (DXGI): " + (gpu is { } v ? Mb((long)v.Budget) : "нет данных"), ShowResourceDiagnostics);
            rows("* Сумма WS может учитывать общие страницы", ShowResourceDiagnostics);
            rows("* PDH — видеопамять по счётчикам GPU", ShowResourceDiagnostics);
            rows("DXGI — бюджет процесса, не всех процессов CEF", ShowResourceDiagnostics);
            rows("Обновить показатели", ShowResourceDiagnostics);
            rows("Настройки памяти и GPU", ShowResourceSettings);
        });
    }
}
