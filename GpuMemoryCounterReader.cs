using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskManagerPlusPlus;

internal static class GpuMemoryCounterReader
{
    private const string DedicatedCounterPath = @"\GPU Process Memory(*)\Dedicated Usage";
    private const string SharedCounterPath = @"\GPU Process Memory(*)\Shared Usage";
    private const uint FormatLarge = 0x00000400;
    private static readonly Regex ProcessIdPattern = new(@"pid_(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryRead(
        out IReadOnlyDictionary<int, (ulong DedicatedBytes, ulong SharedBytes)> usage,
        out string error)
    {
        var mutableUsage = new Dictionary<int, (ulong DedicatedBytes, ulong SharedBytes)>();
        usage = mutableUsage;
        error = string.Empty;

        var status = NativeMethods.PdhOpenQuery(null, UIntPtr.Zero, out var query);
        if (status != 0)
        {
            error = GetErrorMessage(status);
            return false;
        }

        try
        {
            var dedicatedCounters = AddCounters(query, DedicatedCounterPath, isDedicated: true, mutableUsage, out error);
            if (dedicatedCounters is null)
                return false;

            var sharedCounters = AddCounters(query, SharedCounterPath, isDedicated: false, mutableUsage, out error);
            if (sharedCounters is null)
                return false;

            status = NativeMethods.PdhCollectQueryData(query);
            if (status != 0)
            {
                error = GetErrorMessage(status);
                return false;
            }

            foreach (var counter in dedicatedCounters.Concat(sharedCounters))
            {
                status = NativeMethods.PdhGetFormattedCounterValue(
                    counter.Handle, FormatLarge, out _, out var value);
                if (status != 0)
                {
                    error = GetErrorMessage(status);
                    return false;
                }

                if (value.CStatus is not 0 and not 1 || value.Value < 0)
                    continue;

                var existing = mutableUsage.GetValueOrDefault(counter.ProcessId);
                mutableUsage[counter.ProcessId] = counter.IsDedicated
                    ? (unchecked((ulong)value.Value), existing.SharedBytes)
                    : (existing.DedicatedBytes, unchecked((ulong)value.Value));
            }

            return true;
        }
        finally
        {
            NativeMethods.PdhCloseQuery(query);
        }
    }

    private static List<GpuCounter>? AddCounters(
        IntPtr query,
        string wildcardPath,
        bool isDedicated,
        Dictionary<int, (ulong DedicatedBytes, ulong SharedBytes)> usage,
        out string error)
    {
        error = string.Empty;
        var requiredCharacters = 0u;
        var status = NativeMethods.PdhExpandWildCardPath(null, wildcardPath, null, ref requiredCharacters, 0);
        if (status != NativeMethods.MoreData)
        {
            error = GetErrorMessage(status);
            return null;
        }

        var buffer = new StringBuilder(checked((int)requiredCharacters));
        status = NativeMethods.PdhExpandWildCardPath(null, wildcardPath, buffer, ref requiredCharacters, 0);
        if (status != 0)
        {
            error = GetErrorMessage(status);
            return null;
        }

        var counters = new List<GpuCounter>();
        foreach (var path in buffer.ToString().Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ProcessIdPattern.Match(path);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var processId))
                continue;

            status = NativeMethods.PdhAddEnglishCounter(query, path, UIntPtr.Zero, out var handle);
            if (status != 0)
                continue;

            counters.Add(new GpuCounter(handle, processId, isDedicated));
            usage.TryAdd(processId, (0, 0));
        }

        return counters;
    }

    private static string GetErrorMessage(uint status)
    {
        var code = unchecked((int)status);
        return new Win32Exception(code, $"Windows GPU memory counters are unavailable (PDH status 0x{status:X8}).").Message;
    }

    private sealed record GpuCounter(IntPtr Handle, int ProcessId, bool IsDedicated);

    [StructLayout(LayoutKind.Sequential)]
    private struct FormattedCounterValue
    {
        public uint CStatus;
        public long Value;
    }

    private static class NativeMethods
    {
        public const uint MoreData = 0x800007D2;

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhOpenQuery(string? dataSource, UIntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhExpandWildCardPathW")]
        public static extern uint PdhExpandWildCardPath(
            string? dataSource,
            string wildcardPath,
            StringBuilder? buffer,
            ref uint bufferLength,
            uint flags);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
        public static extern uint PdhAddEnglishCounter(
            IntPtr query,
            string fullCounterPath,
            UIntPtr userData,
            out IntPtr counter);

        [DllImport("pdh.dll")]
        public static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        public static extern uint PdhGetFormattedCounterValue(
            IntPtr counter,
            uint format,
            out uint counterType,
            out FormattedCounterValue value);

        [DllImport("pdh.dll")]
        public static extern uint PdhCloseQuery(IntPtr query);
    }
}
