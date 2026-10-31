namespace TaskManagerPlusPlus;

internal readonly record struct SystemUsageSample(
    DateTime Timestamp,
    double CpuPercent,
    ulong TotalRamBytes,
    ulong UsedRamBytes,
    ulong AvailableRamBytes);
