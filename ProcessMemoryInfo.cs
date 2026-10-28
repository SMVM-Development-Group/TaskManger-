namespace TaskManagerPlusPlus;

internal sealed record ProcessMemoryInfo(
    string Name,
    int Id,
    double CpuPercent,
    ulong WorkingSetBytes,
    ulong PrivateCommitBytes)
{
    public string DisplayLabel =>
        $"{Name} (PID {Id}) · {MainWindow.FormatBytes(WorkingSetBytes)} RAM";
}

internal sealed record AiChatTarget(string DisplayLabel, ProcessMemoryInfo? Process);
