using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace TaskManagerPlusPlus;

public partial class AiAgentCoordinatorWindow : Window
{
    private static readonly (string Name, string Instructions)[] AgentDefinitions =
    [
        ("RAM analyst", "Focus on the largest working sets, private commit, shared-page caveats, and memory history. Suggest an end-process action only for a clear, significant, measured problem."),
        ("CPU analyst", "Focus on measured current CPU and CPU history. A single instantaneous sample is weak evidence; report uncertainty and suggest an end-process action only for a clearly justified noncritical process."),
        ("GPU / VRAM analyst", "Focus on dedicated/shared GPU memory and history. If GPU counters are unavailable, say so. Never infer GPU usage from a process name."),
        ("Startup analyst", "Review only the supplied current-user startup inventory. Names alone cannot prove an item is unused. Prefer no action unless there is clear evidence; any disable/restore proposal must cite the exact supplied startup item ID."),
        ("Debloat analyst", "Look for low-activity candidates only when history covers at least 10 minutes and average CPU is at most 1%. A process being quiet does not prove it is unused. Suggest at most one reversible user-startup change or end-process action, and explain uncertainty.")
    ];

    private readonly MainWindow _mainWindow;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly IReadOnlyList<ProcessMemoryInfo> _processes;
    private readonly IReadOnlyList<StartupEntry> _startupEntries;
    private readonly ObservableCollection<AgentRow> _agents = [];
    private CancellationTokenSource? _runCancellation;

    internal AiAgentCoordinatorWindow(
        MainWindow mainWindow,
        string apiKey,
        string model,
        IReadOnlyList<ProcessMemoryInfo> processes,
        IReadOnlyList<StartupEntry> startupEntries)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _apiKey = apiKey;
        _model = model;
        _processes = processes;
        _startupEntries = startupEntries;

        foreach (var (name, instructions) in AgentDefinitions)
            _agents.Add(new AgentRow(name, instructions));
        AgentsList.ItemsSource = _agents;
        StatusText.Text = $"Ready · {_processes.Count:N0} processes · {_startupEntries.Count:N0} current-user startup entries";
    }

    private async void StartSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAgentsAsync(_agents.Where(agent => agent.IsSelected).ToArray());
    }

    private async void StartAgentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: AgentRow agent })
            await RunAgentsAsync([agent]);
    }

    private async Task RunAgentsAsync(IReadOnlyList<AgentRow> selectedAgents)
    {
        if (_runCancellation is not null)
            return;
        if (selectedAgents.Count == 0)
        {
            StatusText.Text = "Select at least one specialist.";
            return;
        }

        _runCancellation = new CancellationTokenSource();
        var cancellationToken = _runCancellation.Token;
        StartSelectedButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        LeadSummaryText.Text = "Specialists are analyzing in parallel; the lead agent will summarize their results.";
        foreach (var agent in selectedAgents)
        {
            agent.CanStart = false;
            agent.Status = "Analyzing…";
            agent.Summary = "";
            agent.Actions.Clear();
        }

        var tasks = selectedAgents.Select(agent => RunAgentAsync(agent, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks);
        var successfulResults = results.Where(result => result is not null).Cast<AiAgentResult>().ToArray();

        if (successfulResults.Length > 0 && !cancellationToken.IsCancellationRequested)
        {
            StatusText.Text = "Lead agent is preparing a concise summary…";
            try
            {
                LeadSummaryText.Text = await NvidiaAiClient.SummarizeAgentResultsAsync(
                    _apiKey, _model, successfulResults, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                LeadSummaryText.Text = "Lead summary canceled.";
            }
            catch (HttpRequestException ex)
            {
                LeadSummaryText.Text = $"Could not generate the lead summary: {ex.Message}";
            }
            catch (TaskCanceledException ex)
            {
                LeadSummaryText.Text = $"Lead summary timed out or was canceled: {ex.Message}";
            }
            catch (JsonException ex)
            {
                LeadSummaryText.Text = $"Lead summary response could not be read: {ex.Message}";
            }
            catch (InvalidDataException ex)
            {
                LeadSummaryText.Text = ex.Message;
            }
            catch (IOException ex)
            {
                LeadSummaryText.Text = $"Could not load the saved model: {ex.Message}";
            }
            catch (UnauthorizedAccessException ex)
            {
                LeadSummaryText.Text = $"Could not load the saved model: {ex.Message}";
            }
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            LeadSummaryText.Text = "Agent run stopped.";
        }

        foreach (var agent in selectedAgents)
            agent.CanStart = true;
        _runCancellation.Dispose();
        _runCancellation = null;
        StartSelectedButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = cancellationToken.IsCancellationRequested
            ? "Run stopped. Completed results are retained."
            : $"Finished · {successfulResults.Length} of {selectedAgents.Count} specialists returned results.";
    }

    private async Task<AiAgentResult?> RunAgentAsync(AgentRow agent, CancellationToken cancellationToken)
    {
        try
        {
            var result = await NvidiaAiClient.RunSpecialistAgentAsync(
                _apiKey, _model, agent.Name, agent.Instructions,
                _processes, _startupEntries, cancellationToken);
            agent.Summary = result.Summary;
            foreach (var action in result.Actions)
                agent.Actions.Add(new AgentActionRow(action));
            agent.Status = result.Actions.Count == 0
                ? "Finished · no local actions proposed"
                : $"Finished · {result.Actions.Count} proposed action(s), each needs confirmation";
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            agent.Status = "Stopped.";
            return null;
        }
        catch (HttpRequestException ex)
        {
            agent.Status = $"Request failed: {ex.Message}";
        }
        catch (TaskCanceledException ex)
        {
            agent.Status = $"Request timed out or was canceled: {ex.Message}";
        }
        catch (JsonException ex)
        {
            agent.Status = $"Response could not be read: {ex.Message}";
        }
        catch (InvalidDataException ex)
        {
            agent.Status = ex.Message;
        }
        catch (IOException ex)
        {
            agent.Status = $"Could not load the saved model: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            agent.Status = $"Could not load the saved model: {ex.Message}";
        }

        return null;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _runCancellation?.Cancel();
        StatusText.Text = "Stopping agent requests…";
        StopButton.IsEnabled = false;
    }

    private void ApproveActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: AgentActionRow row }
            || !row.CanApprove)
            return;

        var action = row.Action;
        var prompt = action.Type switch
        {
            AiAgentActionType.EndProcess =>
                $"End \"{action.Process!.Name}\" (PID {action.Process.Id})?\n\n"
                + $"{action.Reason}\n\nUnsaved work may be lost.",
            AiAgentActionType.DisableStartup =>
                $"Disable \"{action.StartupEntry!.Name}\" from starting automatically?\n\n"
                + $"{action.Reason}\n\nThis change is reversible for your Windows account.",
            AiAgentActionType.RestoreStartup =>
                $"Restore \"{action.StartupEntry!.Name}\" to startup?\n\n"
                + $"{action.Reason}",
            _ => throw new InvalidOperationException("Unsupported AI agent action.")
        };
        var answer = MessageBox.Show(
            this,
            prompt,
            "Confirm AI agent action",
            MessageBoxButton.YesNo,
            action.Type == AiAgentActionType.EndProcess
                ? MessageBoxImage.Warning
                : MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            if (action.Type == AiAgentActionType.EndProcess)
            {
                _mainWindow.TryEndProcess(action.Process!, out var message);
                row.Status = message;
            }
            else
            {
                ApplyStartupAction(action);
                row.Status = action.Type == AiAgentActionType.DisableStartup
                    ? $"Disabled \"{action.StartupEntry!.Name}\". It can be restored in the Autostart window."
                    : $"Restored \"{action.StartupEntry!.Name}\" to current-user startup.";
            }

            row.CanApprove = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.Security.SecurityException)
        {
            row.Status = $"Action failed: {ex.Message}";
        }
    }

    private static void ApplyStartupAction(AiAgentAction action)
    {
        var snapshot = action.StartupEntry
            ?? throw new InvalidOperationException("Startup action has no target.");
        var current = CurrentUserStartupStore.GetEntries().FirstOrDefault(entry =>
            entry.Name == snapshot.Name
            && entry.Location == snapshot.Location
            && entry.Command == snapshot.Command
            && entry.IsRegistry == snapshot.IsRegistry
            && entry.IsEnabled == snapshot.IsEnabled);
        if (current is null)
            throw new InvalidOperationException(
                "The startup entry changed after the analysis. Refresh and rerun the agents.");

        if (action.Type == AiAgentActionType.DisableStartup)
            CurrentUserStartupStore.Disable(current);
        else if (action.Type == AiAgentActionType.RestoreStartup)
            CurrentUserStartupStore.Restore(current);
        else
            throw new InvalidOperationException("Unsupported startup action.");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _runCancellation?.Cancel();
        Close();
    }

    private sealed class AgentRow : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private bool _canStart = true;
        private string _status = "Ready.";
        private string _summary = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        public string Name { get; }
        public string Instructions { get; }
        public ObservableCollection<AgentActionRow> Actions { get; } = [];

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public bool CanStart
        {
            get => _canStart;
            set => SetField(ref _canStart, value);
        }

        public string Status
        {
            get => _status;
            set => SetField(ref _status, value);
        }

        public string Summary
        {
            get => _summary;
            set => SetField(ref _summary, value);
        }

        public AgentRow(string name, string instructions)
        {
            Name = name;
            Instructions = instructions;
        }

        private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class AgentActionRow : INotifyPropertyChanged
    {
        private bool _canApprove = true;
        private string _status = "Waiting for your approval.";

        public event PropertyChangedEventHandler? PropertyChanged;
        public AiAgentAction Action { get; }
        public string TargetLabel { get; }
        public string Reason => Action.Reason;
        public string ButtonLabel => Action.Type switch
        {
            AiAgentActionType.EndProcess => "End task…",
            AiAgentActionType.DisableStartup => "Disable…",
            AiAgentActionType.RestoreStartup => "Restore…",
            _ => "Review…"
        };

        public bool CanApprove
        {
            get => _canApprove;
            set
            {
                if (_canApprove == value)
                    return;
                _canApprove = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanApprove)));
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status == value)
                    return;
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }

        public AgentActionRow(AiAgentAction action)
        {
            Action = action;
            TargetLabel = action.Type switch
            {
                AiAgentActionType.EndProcess =>
                    $"{action.Process!.Name} (PID {action.Process.Id})",
                AiAgentActionType.DisableStartup =>
                    $"Disable startup: {action.StartupEntry!.Name} · {action.StartupEntry.Location}",
                AiAgentActionType.RestoreStartup =>
                    $"Restore startup: {action.StartupEntry!.Name} · {action.StartupEntry.Location}",
                _ => "Unsupported action"
            };
        }
    }
}
