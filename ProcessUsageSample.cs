namespace TaskManagerPlusPlus;

internal readonly record struct ProcessUsageSample(
    DateTime Timestamp,
    double CpuPercent,
    ulong WorkingSetBytes,
    ulong? DedicatedGpuBytes,
    ulong? SharedGpuBytes);
