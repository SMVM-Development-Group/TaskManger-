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
        var performance = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (!NativeMethods.GetPerformanceInfo(ref performance, performance.Size))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read system memory counters.");

        var pageSize = performance.PageSize.ToUInt64();
        var total = performance.PhysicalTotal.ToUInt64() * pageSize;
        var available = performance.PhysicalAvailable.ToUInt64() * pageSize;
        var used = total >= available ? total - available : 0;
        var commit = performance.CommitTotal.ToUInt64() * pageSize;
        var commitLimit = performance.CommitLimit.ToUInt64() * pageSize;
        var cache = performance.SystemCache.ToUInt64() * pageSize;
        var paged = performance.KernelPaged.ToUInt64() * pageSize;
        var nonPaged = performance.KernelNonpaged.ToUInt64() * pageSize;
        var reservedText = NativeMethods.GetPhysicallyInstalledSystemMemory(out var installedMemoryKb)
            ? FormatBytes(installedMemoryKb * 1024 >= total ? installedMemoryKb * 1024 - total : 0)
            : "Unavailable";

        PhysicalUsedText.Text = $"{FormatBytes(used)} / {FormatBytes(total)}";
        PhysicalUsedDetailText.Text = $"{(total == 0 ? 0 : used * 100d / total):N1}% in use";
        PhysicalProgress.Value = total == 0 ? 0 : Math.Clamp(used * 100d / total, 0, 100);
        AvailableText.Text = FormatBytes(available);
        AvailableDetailText.Text = $"{(total == 0 ? 0 : available * 100d / total):N1}% of physical RAM free or reclaimable";
        CommitText.Text = $"{FormatBytes(commit)} / {FormatBytes(commitLimit)}";
        CommitDetailText.Text = $"{(commitLimit == 0 ? 0 : commit * 100d / commitLimit):N1}% of commit limit";
        InUseBreakdownText.Text = FormatBytes(used);
        AvailableBreakdownText.Text = FormatBytes(available);
        CacheBreakdownText.Text = FormatBytes(cache);
        PagedBreakdownText.Text = FormatBytes(paged);
        NonPagedBreakdownText.Text = FormatBytes(nonPaged);
        ReservedBreakdownText.Text = reservedText;
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

                    var counters = new ProcessMemoryCountersEx2
                    {
                        Base = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>() }
                    };
                    if (NativeMethods.GetProcessMemoryInfo(process.Handle, ref counters, counters.Base.Size))
                    {
                        var workingSet = counters.Base.WorkingSetSize.ToUInt64();
                        var privateWorkingSet = Math.Min(counters.PrivateWorkingSetSize.ToUInt64(), workingSet);
                        var sharedWorkingSet = workingSet - privateWorkingSet;
                        var privateBytes = counters.Base.PrivateUsage.ToUInt64();
                        var virtualBytes = process.VirtualMemorySize64 < 0 ? 0UL : (ulong)process.VirtualMemorySize64;
                        rows.Add(new ProcessRow(
                            process.ProcessName,
                            id,
                            cpuPercent,
                            workingSet,
                            privateWorkingSet,
                            sharedWorkingSet,
                            privateBytes,
                            virtualBytes,
                            process.Threads.Count));
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

    private sealed class ProcessRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; private set; }
        public int Id { get; }
        public double CpuPercent { get; private set; }
        public ulong WorkingSetBytes { get; private set; }
        public ulong PrivateWorkingSetBytes { get; private set; }
        public ulong SharedWorkingSetBytes { get; private set; }
        public ulong PrivateBytes { get; private set; }
        public ulong VirtualBytes { get; private set; }
        public int Threads { get; private set; }
        public string WorkingSetText => FormatBytes(WorkingSetBytes);
        public string PrivateWorkingSetText => FormatBytes(PrivateWorkingSetBytes);
        public string SharedWorkingSetText => FormatBytes(SharedWorkingSetBytes);
        public string PrivateBytesText => FormatBytes(PrivateBytes);
        public string VirtualBytesText => FormatBytes(VirtualBytes);

        public ProcessRow(string name, int id, double cpuPercent, ulong workingSet, ulong privateWorkingSet,
            ulong sharedWorkingSet, ulong privateBytes, ulong virtualBytes, int threads)
        {
            Name = name;
            Id = id;
            CpuPercent = cpuPercent;
            WorkingSetBytes = workingSet;
            PrivateWorkingSetBytes = privateWorkingSet;
            SharedWorkingSetBytes = sharedWorkingSet;
            PrivateBytes = privateBytes;
            VirtualBytes = virtualBytes;
            Threads = threads;
        }

        public void UpdateFrom(ProcessRow row)
        {
            Name = row.Name;
            CpuPercent = row.CpuPercent;
            WorkingSetBytes = row.WorkingSetBytes;
            PrivateWorkingSetBytes = row.PrivateWorkingSetBytes;
            SharedWorkingSetBytes = row.SharedWorkingSetBytes;
            PrivateBytes = row.PrivateBytes;
            VirtualBytes = row.VirtualBytes;
            Threads = row.Threads;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public ProcessMemoryCounters Base;
        public UIntPtr PrivateWorkingSetSize;
        public UIntPtr SharedCommitUsage;
    }

    private static class NativeMethods
    {
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPerformanceInfo(ref PerformanceInformation information, uint size);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCountersEx2 counters, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
    }
}
