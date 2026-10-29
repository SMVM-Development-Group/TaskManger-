namespace TaskManagerPlusPlus;

internal sealed record ProcessUsageSample(
    DateTime Timestamp,
    double CpuPercent,
    ulong WorkingSetBytes,
    ulong? DedicatedGpuBytes,
    ulong? SharedGpuBytes);
