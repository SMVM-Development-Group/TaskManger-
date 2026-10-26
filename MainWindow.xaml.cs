using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace TaskManagerPlusPlus;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ProcessRow> _processRows = [];
    private readonly Dictionary<int, CpuSample> _cpuSamples = [];
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ICollectionView _processView = null!;
    private DateTime _lastCpuSample = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();
        _processView = CollectionViewSource.GetDefaultView(_processRows);
        ProcessesGrid.ItemsSource = _processView;
        _refreshTimer.Tick += (_, _) => RefreshData();
        Loaded += (_, _) =>
        {
            RefreshData();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshData();

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
                            counters.PrivateUsage.ToUInt64()));
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

        try
        {
            using var process = Process.GetProcessById(selected.Id);
            process.Kill();
            ProcessCountText.Text = $"End-task request sent to {selected.Name} (PID {selected.Id}).";
        }
        catch (ArgumentException)
        {
            ProcessCountText.Text = $"Process {selected.Id} has already exited.";
        }
        catch (Win32Exception ex)
        {
            ProcessCountText.Text = $"Could not end {selected.Name}: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            ProcessCountText.Text = $"Could not end {selected.Name}: {ex.Message}";
        }
    }

    private static string FormatBytes(ulong bytes)
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
        public double CpuPercent { get; private set; }
        public ulong WorkingSetBytes { get; private set; }
        public string WorkingSetText => FormatBytes(WorkingSetBytes);
        public ulong PrivateCommitBytes { get; private set; }
        public string PrivateCommitText => FormatBytes(PrivateCommitBytes);

        public ProcessRow(string name, int id, double cpuPercent, ulong workingSet, ulong privateCommitBytes)
        {
            Name = name;
            Id = id;
            CpuPercent = cpuPercent;
            WorkingSetBytes = workingSet;
            PrivateCommitBytes = privateCommitBytes;
        }

        public void UpdateFrom(ProcessRow row)
        {
            Name = row.Name;
            CpuPercent = row.CpuPercent;
            WorkingSetBytes = row.WorkingSetBytes;
            PrivateCommitBytes = row.PrivateCommitBytes;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
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
