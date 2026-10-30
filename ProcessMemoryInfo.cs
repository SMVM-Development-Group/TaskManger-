namespace TaskManagerPlusPlus;

internal sealed record ProcessMemoryInfo(
    string Name,
    int Id,
    double CpuPercent,
    ulong WorkingSetBytes,
    ulong PrivateCommitBytes,
    ulong? DedicatedGpuBytes = null,
    ulong? SharedGpuBytes = null,
    ProcessHistorySummary? History = null,
    DateTime? StartTimeUtc = null)
{
    public string DisplayLabel =>
        $"{Name} (PID {Id}) · {MainWindow.FormatBytes(WorkingSetBytes)} RAM";
}

internal sealed record AiProcessTerminationRecommendation(ProcessMemoryInfo Process, string Reason);

internal sealed record AiProcessAnalysis(
    string Summary,
    IReadOnlyList<AiProcessTerminationRecommendation> Recommendations);

internal sealed record ProcessHistorySummary(
    int SampleCount,
    double DurationMinutes,
    double AverageCpuPercent,
    double PeakCpuPercent,
    double AverageWorkingSetBytes,
    double PeakWorkingSetBytes,
    double? AverageDedicatedGpuBytes,
    double? PeakDedicatedGpuBytes,
    double? AverageSharedGpuBytes,
    double? PeakSharedGpuBytes);

internal sealed record AiChatTarget(
    string DisplayLabel,
    ProcessMemoryInfo? Process,
    bool IncludeAllProcesses = false);

internal enum AiAgentActionType
{
    EndProcess,
    DisableStartup,
    RestoreStartup
}

internal sealed record AiAgentAction(
    AiAgentActionType Type,
    ProcessMemoryInfo? Process,
    StartupEntry? StartupEntry,
    string Reason);

internal sealed record AiAgentResult(
    string AgentName,
    string Summary,
    IReadOnlyList<AiAgentAction> Actions);
