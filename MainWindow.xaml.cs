using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;

namespace TaskManagerPlusPlus;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> AiTerminationBlockedProcessNames = new(
        [
            "csrss", "wininit", "winlogon", "services", "lsass", "smss",
            "system", "registry", "secure system", "svchost", "msmpeng",
            "taskmanagerplusplus"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly ObservableCollection<ProcessRow> _processRows = [];
    private readonly Dictionary<int, CpuSample> _cpuSamples = [];
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ICollectionView _processView = null!;
    private DateTime _lastCpuSample = DateTime.UtcNow;
    private string _gpuCounterStatus = "GPU memory counters have not been sampled yet.";

    public MainWindow()
    {
        InitializeComponent();
        _processView = CollectionViewSource.GetDefaultView(_processRows);
        ProcessesGrid.ItemsSource = _processView;
        ProcessesGrid.LoadingRow += ProcessesGrid_LoadingRow;
        _refreshTimer.Tick += (_, _) => RefreshData();
        Loaded += (_, _) =>
        {
            RefreshData();
            _refreshTimer.Start();
        };
        SourceInitialized += (_, _) => EnableRoundedCorners();
        StateChanged += (_, _) => UpdateMaximizeIcon();
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateMaximizeIcon()
    {
        if (FindName("MaximizeIcon") is System.Windows.Shapes.Path icon)
        {
            icon.Data = (System.Windows.Media.Geometry)FindResource(
                WindowState == WindowState.Maximized ? "IconRestore" : "IconMaximize");
        }
        if (FindName("MaximizeButton") is System.Windows.Controls.Button btn)
        {
            btn.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
        }
    }

    private void EnableRoundedCorners()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshData();

    private void AiAssistantButton_Click(object sender, RoutedEventArgs e)
    {
        AiAssistantButton.ContextMenu!.PlacementTarget = AiAssistantButton;
        AiAssistantButton.ContextMenu.IsOpen = true;
    }

    private void AiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        new AiSettingsWindow { Owner = this }.ShowDialog();
    }

    private void AnalyzeProcessesButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        var topProcesses = _processRows
            .Where(row => row.WorkingSetBytes > 0)
            .OrderByDescending(row => row.WorkingSetBytes)
            .Take(5)
            .Select(CreateProcessMemoryInfo)
            .ToArray();

        if (topProcesses.Length == 0)
        {
            MessageBox.Show(this, "No process memory data is currently available.", "AI analysis",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new AiAnalysisWindow(apiKey, topProcesses) { Owner = this }.ShowDialog();
    }

    private void AnalyzeSelectedProcessButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedProcess();
        if (selected is null)
            return;

        var apiKey = GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
            new AiAnalysisWindow(apiKey, [CreateProcessMemoryInfo(selected)]) { Owner = this }.ShowDialog();
    }

    private void DeepScanProcessesButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        AiProcessActionMode actionMode;
        try
        {
            actionMode = AiSettingsStore.LoadProcessActionMode();
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"Could not load the AI process action preference: {ex.Message}",
                "AI settings", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(this, $"Could not load the AI process action preference: {ex.Message}",
                "AI settings", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var processes = _processRows
            .OrderByDescending(row => row.WorkingSetBytes)
            .Take(40)
            .Select(CreateProcessMemoryInfo)
            .ToArray();
        if (processes.Length == 0)
        {
            MessageBox.Show(this, "No process data is currently available.", "AI deep scan",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new AiDeepScanWindow(this, apiKey, processes, actionMode) { Owner = this }.ShowDialog();
    }

    private void ChatAboutSelectedProcessButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var processes = _processRows
                .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Id)
                .Select(CreateProcessMemoryInfo)
                .ToArray();
            var selectedProcess = ProcessesGrid.SelectedItem is ProcessRow selected
                ? CreateProcessMemoryInfo(selected)
                : null;
            new AiChatWindow(apiKey, processes, selectedProcess) { Owner = this }.ShowDialog();
        }
    }

    private ProcessRow? GetSelectedProcess()
    {
        if (ProcessesGrid.SelectedItem is ProcessRow selected)
            return selected;

        MessageBox.Show(this, "Select a process in the list first, then reopen AI assistant.",
            "AI assistant", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private static ProcessMemoryInfo CreateProcessMemoryInfo(ProcessRow row) =>
        new(
            row.Name,
            row.Id,
            row.CpuPercent,
            row.WorkingSetBytes,
            row.PrivateCommitBytes,
            row.DedicatedGpuBytes,
            row.SharedGpuBytes,
            row.CreateHistorySummary(),
            row.StartTimeUtc);

    private string? GetApiKey()
    {
        string? apiKey;
        try
        {
            apiKey = AiSettingsStore.LoadApiKey();
        }
        catch (CryptographicException ex)
        {
            MessageBox.Show(this, $"Could not read the saved API key: {ex.Message}", "AI settings",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"Could not read the saved API key: {ex.Message}", "AI settings",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(this, $"Could not read the saved API key: {ex.Message}", "AI settings",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (new AiSettingsWindow { Owner = this }.ShowDialog() != true)
                return null;

            try
            {
                apiKey = AiSettingsStore.LoadApiKey();
            }
            catch (CryptographicException ex)
            {
                MessageBox.Show(this, $"Could not read the saved API key: {ex.Message}", "AI settings",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, $"Could not read the saved API key: {ex.Message}", "AI settings",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, $"Could not read the saved API key: {ex.Message}", "AI settings",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _processView?.Refresh();
        UpdateProcessSummary();
    }

    private bool MatchesFilter(object item)
    {
        if (item is not ProcessRow row || string.IsNullOrWhiteSpace(SearchBox.Text))
            return true;

        return row.Name.Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            || row.Id.ToString().Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshData()
    {
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = Math.Max((now - _lastCpuSample).TotalSeconds, 0.001);
            var processorCount = Math.Max(Environment.ProcessorCount, 1);

            UpdateMemory();
            UpdateProcesses(elapsed, processorCount);
            _lastCpuSample = now;
            UpdatedText.Text = $"Updated {DateTime.Now:HH:mm:ss}";
        }
        catch (Win32Exception ex)
        {
            UpdatedText.Text = "Refresh failed";
            ProcessCountText.Text = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            UpdatedText.Text = "Refresh failed";
            ProcessCountText.Text = ex.Message;
        }
    }

    private void UpdateMemory()
    {
        var memory = ReadMemoryUsage();
        var total = memory.TotalBytes;
        var available = memory.AvailableBytes;
        var used = memory.UsedBytes;

        PhysicalUsedText.Text = $"{FormatBytes(used)} / {FormatBytes(total)}";
        PhysicalUsedDetailText.Text = $"{(total == 0 ? 0 : used * 100d / total):N1}% in use";
        PhysicalProgress.Value = total == 0 ? 0 : Math.Clamp(used * 100d / total, 0, 100);
        AvailableText.Text = FormatBytes(available);
        AvailableDetailText.Text = $"{(total == 0 ? 0 : available * 100d / total):N1}% of physical RAM free or reclaimable";
    }

    private static MemoryUsage ReadMemoryUsage()
    {
        var performance = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (!NativeMethods.GetPerformanceInfo(ref performance, performance.Size))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read system memory counters.");

        var pageSize = performance.PageSize.ToUInt64();
        var total = performance.PhysicalTotal.ToUInt64() * pageSize;
        var available = Math.Min(performance.PhysicalAvailable.ToUInt64() * pageSize, total);
        return new MemoryUsage(total, total - available, available);
    }

    private void PhysicalMemoryCard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var memory = ReadMemoryUsage();
            var processMemory = _processRows
                .Where(row => row.WorkingSetBytes > 0)
                .OrderByDescending(row => row.WorkingSetBytes)
                .Select(row => (Name: row.Name, Id: row.Id, Bytes: row.WorkingSetBytes))
                .ToArray();
            new MemoryChartWindow(memory.UsedBytes, processMemory) { Owner = this }.ShowDialog();
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(this, ex.Message, "RAM usage unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ProcessRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            row.IsSelected = true;
            ProcessesGrid.SelectedItem = row.Item;
        }
    }

    private void ProcessesGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.PreviewMouseRightButtonDown -= ProcessRow_PreviewMouseRightButtonDown;
        e.Row.PreviewMouseRightButtonDown += ProcessRow_PreviewMouseRightButtonDown;
        e.Row.ContextMenu = CreateProcessContextMenu();
    }

    private ContextMenu CreateProcessContextMenu()
    {
        var contextMenu = new ContextMenu
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x08, 0x0B, 0x12)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x35, 0x43, 0x5A)),
            BorderThickness = new Thickness(1)
        };
        contextMenu.Items.Add(CreateMenuItem(
            "History",
            null,
            CreateMenuItem("RAM history", (_, _) => ShowProcessHistory(ProcessHistoryMetric.Ram))));

        var cpuMenu = CreateMenuItem("CPU",
            null,
            CreateMenuItem("Current CPU details…", CpuDetailsMenuItem_Click),
            CreateMenuItem("CPU history chart", (_, _) => ShowProcessHistory(ProcessHistoryMetric.Cpu)));
        contextMenu.Items.Add(cpuMenu);

        var gpuMenu = CreateMenuItem("GPU / VRAM",
            null,
            CreateMenuItem("Current VRAM details…", GpuDetailsMenuItem_Click),
            CreateMenuItem("Dedicated VRAM history", (_, _) => ShowProcessHistory(ProcessHistoryMetric.DedicatedGpu)),
            CreateMenuItem("Shared GPU memory history", (_, _) => ShowProcessHistory(ProcessHistoryMetric.SharedGpu)));
        contextMenu.Items.Add(gpuMenu);
        return contextMenu;
    }

    private static MenuItem CreateMenuItem(
        string header,
        RoutedEventHandler? click = null,
        params MenuItem[] children)
    {
        var item = new MenuItem
        {
            Header = header,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xED, 0xF3, 0xFC)),
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x08, 0x0B, 0x12)),
            Padding = new Thickness(14, 8, 14, 8)
        };
        if (click is not null)
            item.Click += click;
        foreach (var child in children)
            item.Items.Add(child);
        return item;
    }

    private void CpuHistoryMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowProcessHistory(ProcessHistoryMetric.Cpu);

    private void RamHistoryMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowProcessHistory(ProcessHistoryMetric.Ram);

    private void DedicatedGpuHistoryMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowProcessHistory(ProcessHistoryMetric.DedicatedGpu);

    private void SharedGpuHistoryMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowProcessHistory(ProcessHistoryMetric.SharedGpu);

    private void ShowProcessHistory(ProcessHistoryMetric metric)
    {
        if (ProcessesGrid.SelectedItem is not ProcessRow selected)
            return;

        new ProcessHistoryWindow(selected.Name, selected.Id, selected.History.ToArray(), metric)
        {
            Owner = this
        }.ShowDialog();
    }

    private void CpuDetailsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessesGrid.SelectedItem is not ProcessRow selected)
            return;

        var samples = selected.History.Select(sample => sample.CpuPercent).ToArray();
        var average = samples.Length == 0 ? 0 : samples.Average();
        var peak = samples.Length == 0 ? 0 : samples.Max();
        MessageBox.Show(this,
            $"{selected.Name} (PID {selected.Id})\n\n"
            + $"Current CPU: {selected.CpuPercent:N1}%\n"
            + $"Average since app start: {average:N1}%\n"
            + $"Peak since app start: {peak:N1}%\n"
            + $"Samples: {samples.Length:N0}",
            "CPU details", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void GpuDetailsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessesGrid.SelectedItem is not ProcessRow selected)
            return;

        var latestSample = selected.History.LastOrDefault();
        var gpuInfo = latestSample?.DedicatedGpuBytes is ulong dedicated
            && latestSample.SharedGpuBytes is ulong shared
            ? $"Dedicated VRAM: {FormatBytes(dedicated)}\nShared GPU memory: {FormatBytes(shared)}"
            : $"Per-process GPU memory is unavailable.\n{_gpuCounterStatus}";
        MessageBox.Show(this,
            $"{selected.Name} (PID {selected.Id})\n\n{gpuInfo}\n\n"
            + "Dedicated VRAM and shared GPU memory are read from Windows GPU Process Memory counters. "
            + "They may be unavailable on systems without GPU performance counters.",
            "GPU / VRAM details", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UpdateProcesses(double elapsedSeconds, int processorCount)
    {
        var currentIds = new HashSet<int>();
        var rows = new List<ProcessRow>();
        var inaccessibleCount = 0;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    process.Refresh();
                    var id = process.Id;
                    var cpuTime = process.TotalProcessorTime;
                    DateTime? startTimeUtc = null;
                    try
                    {
                        startTimeUtc = process.StartTime.ToUniversalTime();
                    }
                    catch (Win32Exception)
                    {
                        inaccessibleCount++;
                    }
                    catch (InvalidOperationException)
                    {
                        inaccessibleCount++;
                    }
                    var cpuPercent = 0d;
                    if (_cpuSamples.TryGetValue(id, out var previous))
                    {
                        var delta = (cpuTime - previous.TotalCpuTime).TotalSeconds;
                        cpuPercent = Math.Clamp(delta / (elapsedSeconds * processorCount) * 100d, 0, 100);
                    }
                    _cpuSamples[id] = new CpuSample(cpuTime);
                    currentIds.Add(id);

                    var counters = new ProcessMemoryCounters
                    {
                        Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>()
                    };
                    if (NativeMethods.GetProcessMemoryInfo(process.Handle, ref counters, counters.Size))
                    {
                        rows.Add(new ProcessRow(
                            process.ProcessName,
                            id,
                            cpuPercent,
                            counters.WorkingSetSize.ToUInt64(),
                            counters.PrivateUsage.ToUInt64(),
                            startTimeUtc));
                    }
                    else
                    {
                        inaccessibleCount++;
                    }
                }
                catch (Win32Exception)
                {
                    inaccessibleCount++;
                }
                catch (InvalidOperationException)
                {
                    inaccessibleCount++;
                }
            }
        }

        foreach (var oldId in _cpuSamples.Keys.Where(id => !currentIds.Contains(id)).ToArray())
            _cpuSamples.Remove(oldId);

        if (GpuMemoryCounterReader.TryRead(out var gpuUsage, out var gpuError))
        {
            _gpuCounterStatus = "Windows GPU memory counters are available.";
            foreach (var row in rows)
            {
                if (gpuUsage.TryGetValue(row.Id, out var memory))
                    row.SetGpuUsage(memory.DedicatedBytes, memory.SharedBytes);
            }
        }
        else
        {
            _gpuCounterStatus = gpuError;
        }

        var byId = _processRows.ToDictionary(row => row.Id);
        foreach (var row in rows)
        {
            if (byId.TryGetValue(row.Id, out var existing))
                existing.UpdateFrom(row);
            else
                _processRows.Add(row);
        }

        var liveIds = rows.Select(row => row.Id).ToHashSet();
        for (var index = _processRows.Count - 1; index >= 0; index--)
        {
            if (!liveIds.Contains(_processRows[index].Id))
                _processRows.RemoveAt(index);
        }

        _processView.Filter = MatchesFilter;
        _processView.Refresh();
        ProcessCountText.Text = $"{rows.Count:N0} processes"
            + (inaccessibleCount > 0 ? $" · {inaccessibleCount:N0} inaccessible or exited during refresh" : "")
            + " · sort by clicking a column";
        UpdateProcessSummary();
    }

    private void UpdateProcessSummary()
    {
        var visibleRows = _processView.Cast<ProcessRow>().ToArray();
        if (visibleRows.Length == 0)
        {
            ProcessSummaryText.Text = "Filtered total: no matching processes";
            return;
        }

        var workingSet = visibleRows.Aggregate(0UL, (total, row) => total + row.WorkingSetBytes);
        ProcessSummaryText.Text = $"Filtered total ({visibleRows.Length:N0}): "
            + $"{FormatBytes(workingSet)} RAM in use · "
            + $"{FormatBytes(visibleRows.Aggregate(0UL, (total, row) => total + row.PrivateCommitBytes))} memory allocated";
    }

    private void EndTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessesGrid.SelectedItem is not ProcessRow selected)
        {
            MessageBox.Show(this, "Select a process first.", "End task", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"End process \"{selected.Name}\" (PID {selected.Id})? Unsaved work in that process may be lost.",
            "Confirm end task",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        var target = CreateProcessMemoryInfo(selected);
        TryEndProcess(target, out var message);
        ProcessCountText.Text = message;
    }

    internal bool TryEndProcess(ProcessMemoryInfo target, out string message)
    {
        if (target.Id <= 4
            || target.Id == Environment.ProcessId
            || AiTerminationBlockedProcessNames.Contains(target.Name)
            || !target.StartTimeUtc.HasValue)
        {
            message = $"Ending {target.Name} (PID {target.Id}) is blocked for safety.";
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(target.Id);
            if (!string.Equals(process.ProcessName, target.Name, StringComparison.OrdinalIgnoreCase)
                || process.StartTime.ToUniversalTime() != target.StartTimeUtc.Value)
            {
                message = $"Process {target.Id} changed or exited after the scan; it was not ended.";
                return false;
            }

            process.Kill();
            message = $"End-task request sent to {target.Name} (PID {target.Id}).";
            return true;
        }
        catch (ArgumentException)
        {
            message = $"Process {target.Id} has already exited.";
            return false;
        }
        catch (Win32Exception ex)
        {
            message = $"Could not end {target.Name} (PID {target.Id}): {ex.Message}";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            message = $"Could not end {target.Name} (PID {target.Id}): {ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            message = $"Could not end {target.Name} (PID {target.Id}): {ex.Message}";
            return false;
        }
    }

    internal static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:N1} {units[unit]}";
    }

    private readonly record struct CpuSample(TimeSpan TotalCpuTime);
    private readonly record struct MemoryUsage(ulong TotalBytes, ulong UsedBytes, ulong AvailableBytes);

    private sealed class ProcessRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; private set; }
        public int Id { get; }
        public DateTime? StartTimeUtc { get; private set; }
        public double CpuPercent { get; private set; }
        public ulong WorkingSetBytes { get; private set; }
        public string WorkingSetText => FormatBytes(WorkingSetBytes);
        public ulong PrivateCommitBytes { get; private set; }
        public string PrivateCommitText => FormatBytes(PrivateCommitBytes);
        public ulong? DedicatedGpuBytes { get; private set; }
        public ulong? SharedGpuBytes { get; private set; }
        public List<ProcessUsageSample> History { get; } = [];

        public ProcessRow(
            string name,
            int id,
            double cpuPercent,
            ulong workingSet,
            ulong privateCommitBytes,
            DateTime? startTimeUtc)
        {
            Name = name;
            Id = id;
            StartTimeUtc = startTimeUtc;
            CpuPercent = cpuPercent;
            WorkingSetBytes = workingSet;
            PrivateCommitBytes = privateCommitBytes;
            AddHistorySample();
        }

        public void UpdateFrom(ProcessRow row)
        {
            Name = row.Name;
            StartTimeUtc = row.StartTimeUtc;
            CpuPercent = row.CpuPercent;
            WorkingSetBytes = row.WorkingSetBytes;
            PrivateCommitBytes = row.PrivateCommitBytes;
            DedicatedGpuBytes = row.DedicatedGpuBytes;
            SharedGpuBytes = row.SharedGpuBytes;
            AddHistorySample();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        public void SetGpuUsage(ulong dedicatedGpuBytes, ulong sharedGpuBytes)
        {
            DedicatedGpuBytes = dedicatedGpuBytes;
            SharedGpuBytes = sharedGpuBytes;
            if (History.Count > 0)
                History[^1] = History[^1] with
                {
                    DedicatedGpuBytes = dedicatedGpuBytes,
                    SharedGpuBytes = sharedGpuBytes
                };
        }

        public ProcessHistorySummary? CreateHistorySummary()
        {
            if (History.Count == 0)
                return null;

            var dedicatedGpuSamples = History
                .Where(sample => sample.DedicatedGpuBytes.HasValue)
                .Select(sample => (double)sample.DedicatedGpuBytes!.Value)
                .ToArray();
            var sharedGpuSamples = History
                .Where(sample => sample.SharedGpuBytes.HasValue)
                .Select(sample => (double)sample.SharedGpuBytes!.Value)
                .ToArray();
            return new ProcessHistorySummary(
                History.Count,
                Math.Max(0, (History[^1].Timestamp - History[0].Timestamp).TotalMinutes),
                History.Average(sample => sample.CpuPercent),
                History.Max(sample => sample.CpuPercent),
                History.Average(sample => (double)sample.WorkingSetBytes),
                History.Max(sample => (double)sample.WorkingSetBytes),
                dedicatedGpuSamples.Length == 0 ? null : dedicatedGpuSamples.Average(),
                dedicatedGpuSamples.Length == 0 ? null : dedicatedGpuSamples.Max(),
                sharedGpuSamples.Length == 0 ? null : sharedGpuSamples.Average(),
                sharedGpuSamples.Length == 0 ? null : sharedGpuSamples.Max());
        }

        private void AddHistorySample()
        {
            var now = DateTime.Now;
            History.Add(new ProcessUsageSample(now, CpuPercent, WorkingSetBytes, DedicatedGpuBytes, SharedGpuBytes));
            var cutoff = now.AddMinutes(-30);
            while (History.Count > 900 || (History.Count > 1 && History[0].Timestamp < cutoff))
                History.RemoveAt(0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
    }

    private static class NativeMethods
    {
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPerformanceInfo(ref PerformanceInformation information, uint size);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);
    }
}
