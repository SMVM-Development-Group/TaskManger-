using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace TaskManagerPlusPlus;

public partial class AiChatWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly string _apiKey;
    private readonly IReadOnlyList<ProcessMemoryInfo> _processes;
    private ProcessMemoryInfo? _process;
    private bool _includeAllProcesses;
    private readonly List<(string Role, string Content)> _conversation = [];

    internal AiChatWindow(
        MainWindow mainWindow,
        string apiKey,
        IReadOnlyList<ProcessMemoryInfo> processes,
        ProcessMemoryInfo? selectedProcess)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _apiKey = apiKey;
        _processes = processes;
        _process = selectedProcess;
        var targets = processes
            .Select(process => new AiChatTarget(process.DisplayLabel, process))
            .Prepend(new AiChatTarget(
                $"All running processes ({processes.Count:N0}; RAM/CPU/GPU snapshot)",
                null,
                IncludeAllProcesses: true))
            .Prepend(new AiChatTarget("General chat (no process selected)", null))
            .ToArray();
        ProcessBox.ItemsSource = targets;
        ProcessBox.SelectedItem = selectedProcess is null
            ? targets.First(target => target.IncludeAllProcesses)
            : targets.FirstOrDefault(target => target.Process?.Id == selectedProcess.Id) ?? targets[0];
        if (selectedProcess is null)
            _includeAllProcesses = true;
        ModelBox.Items.Add(AiSettingsStore.DefaultModel);
        ModelBox.Items.Add("deepseek-ai/deepseek-v4.1-flash");
        try
        {
            var savedModel = AiSettingsStore.LoadModel();
            if (!ModelBox.Items.Contains(savedModel))
                ModelBox.Items.Add(savedModel);
            ModelBox.Text = savedModel;
        }
        catch (IOException ex)
        {
            ModelBox.Text = AiSettingsStore.DefaultModel;
            StatusText.Text = $"Could not load the saved model; using the default. {ex.Message}";
        }
        ConversationText.Text = selectedProcess is null
            ? $"Chat ready with a read-only snapshot of {_processes.Count:N0} running processes. Ask about RAM, CPU, or GPU usage."
            : $"Chatting about {selectedProcess.Name} (PID {selectedProcess.Id}).";
        StateChanged += (_, _) => UpdateMaximizeIcon();
    }

    private void ProcessBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessBox.SelectedItem is not AiChatTarget target
            || (target.Process?.Id == _process?.Id
                && target.IncludeAllProcesses == _includeAllProcesses))
            return;

        _process = target.Process;
        _includeAllProcesses = target.IncludeAllProcesses;
        _conversation.Clear();
        ConversationText.Text = target.IncludeAllProcesses
            ? $"All-process snapshot selected ({_processes.Count:N0} processes). Ask a new question to begin."
            : target.Process is null
            ? "General chat selected; no local process data will be sent. Ask a new question to begin."
            : $"Now chatting about {target.Process.Name} (PID {target.Process.Id}). Ask a new question to begin.";
        StatusText.Text = "Process changed; the previous conversation was cleared.";
    }

    private async void LoadModelsButton_Click(object sender, RoutedEventArgs e)
    {
        LoadModelsButton.IsEnabled = false;
        StatusText.Text = "Loading models available to this API key…";
        try
        {
            var selectedModel = ModelBox.Text;
            var models = await NvidiaAiClient.GetAvailableModelsAsync(_apiKey);
            ModelBox.Items.Clear();
            foreach (var model in models)
                ModelBox.Items.Add(model);

            if (!string.IsNullOrWhiteSpace(selectedModel))
            {
                if (!models.Contains(selectedModel, StringComparer.OrdinalIgnoreCase))
                    ModelBox.Items.Add(selectedModel);
                ModelBox.Text = selectedModel;
            }
            else if (models.Count > 0)
            {
                ModelBox.SelectedIndex = 0;
            }

            StatusText.Text = models.Count == 0
                ? "The API returned no model IDs."
                : $"Loaded {models.Count} models. Select one before sending your next message.";
        }
        catch (HttpRequestException ex)
        {
            StatusText.Text = $"Could not load models: {ex.Message}";
        }
        catch (TaskCanceledException ex)
        {
            StatusText.Text = $"Loading models timed out or was canceled. {ex.Message}";
        }
        catch (JsonException ex)
        {
            StatusText.Text = $"The model list response could not be read. {ex.Message}";
        }
        catch (InvalidDataException ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            LoadModelsButton.IsEnabled = true;
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        var question = QuestionBox.Text.Trim();
        if (question.Length == 0)
        {
            StatusText.Text = "Type a question first.";
            return;
        }

        if (question.StartsWith("/ask", StringComparison.OrdinalIgnoreCase))
        {
            if (question.Length == 4 || !char.IsWhiteSpace(question[4]))
            {
                StatusText.Text = "Usage: /ask <your question>";
                return;
            }

            question = question[5..].Trim();
            if (question.Length == 0)
            {
                StatusText.Text = "Usage: /ask <your question>";
                return;
            }
        }
        else if (question.StartsWith("/", StringComparison.Ordinal))
        {
            if (HandleSlashCommand(question))
                return;
        }

        var model = ModelBox.Text.Trim();
        if (model.Length == 0)
        {
            StatusText.Text = "Choose or enter a model before sending.";
            return;
        }

        _conversation.Add(("user", question));
        AppendMessage("You", question);
        QuestionBox.Clear();
        SendButton.IsEnabled = false;
        ProcessBox.IsEnabled = false;
        ModelBox.IsEnabled = false;
        LoadModelsButton.IsEnabled = false;
        StatusText.Text = "Waiting for NVIDIA AI…";
        try
        {
            var answer = await NvidiaAiClient.ChatAboutProcessAsync(
                _apiKey, model, _process, _processes, _includeAllProcesses, _conversation);
            _conversation.Add(("assistant", answer));
            AppendMessage("AI", answer);
            StatusText.Text = $"Ready · {model}";
        }
        catch (HttpRequestException ex)
        {
            StatusText.Text = $"Request failed: {ex.Message}";
        }
        catch (TaskCanceledException ex)
        {
            StatusText.Text = $"The request timed out or was canceled. {ex.Message}";
        }
        catch (JsonException ex)
        {
            StatusText.Text = $"NVIDIA returned an unreadable response. {ex.Message}";
        }
        catch (InvalidDataException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (IOException ex)
        {
            StatusText.Text = $"Could not load the saved AI model setting: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText.Text = $"Could not load the saved AI model setting: {ex.Message}";
        }
        finally
        {
            SendButton.IsEnabled = true;
            ProcessBox.IsEnabled = true;
            ModelBox.IsEnabled = true;
            LoadModelsButton.IsEnabled = true;
            QuestionBox.Focus();
        }
    }

    private void SlashCommandSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Content: string command })
            return;

        QuestionBox.Text = command;
        QuestionBox.CaretIndex = QuestionBox.Text.Length;
        QuestionBox.Focus();
    }

    private bool HandleSlashCommand(string commandLine)
    {
        var separator = commandLine.IndexOf(' ');
        var command = (separator < 0 ? commandLine : commandLine[..separator])
            .ToLowerInvariant();
        var argument = separator < 0 ? "" : commandLine[(separator + 1)..].Trim();

        if (command == "/agents")
        {
            if (argument.Length > 0)
            {
                StatusText.Text = "Usage: /agents";
                return true;
            }

            OpenAgentCoordinator();
            return true;
        }

        string? result = command switch
        {
            "/help" => GetSlashCommandHelp(),
            "/ram" => GetRamSummary(),
            "/top" => GetTopProcesses(argument),
            "/cpu" => GetCpuSummary(),
            "/gpu" => GetGpuSummary(),
            "/process" => GetProcessSummary(argument),
            _ => null
        };

        if (result is null)
        {
            StatusText.Text = $"Unknown command \"{command}\". Try /help.";
            AppendMessage("Local", $"Unknown command \"{command}\". Type /help to see available commands.");
            return true;
        }

        _conversation.Add(("user", commandLine));
        AppendMessage("You", commandLine);
        _conversation.Add(("assistant", result));
        AppendMessage("Local", result);
        QuestionBox.Clear();
        StatusText.Text = "Read-only local snapshot · no request sent to NVIDIA.";
        return true;
    }

    private void OpenAgentCoordinator()
    {
        var model = ModelBox.Text.Trim();
        if (model.Length == 0)
        {
            StatusText.Text = "Choose or enter a model before starting agents.";
            return;
        }

        try
        {
            var startupEntries = CurrentUserStartupStore.GetEntries();
            new AiAgentCoordinatorWindow(
                _mainWindow, _apiKey, model, _processes, startupEntries)
            {
                Owner = this
            }.ShowDialog();
        }
        catch (IOException ex)
        {
            StatusText.Text = $"Could not load current-user startup inventory: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText.Text = $"Could not load current-user startup inventory: {ex.Message}";
        }
    }

    private static string GetSlashCommandHelp() =>
        """
        Available commands:
        /ram — show the accessible process count and summed process working sets
        /top [count] — list the biggest RAM users (1–20; default 10)
        /cpu — list the highest current CPU users
        /gpu — list processes with GPU memory data
        /process <name or PID> — inspect a process in this snapshot
        /ask <question> — send a question to the AI with the selected snapshot
        /agents — open five specialist agents and a lead summarizer; approve actions individually
        /help — show this list

        Snapshot commands run locally and do not send a request. Working sets can include shared pages, so their sum is not total physical RAM use.
        """;

    private string GetRamSummary()
    {
        var totalWorkingSet = _processes.Aggregate(
            0UL,
            (total, process) => total + process.WorkingSetBytes);
        var topProcesses = _processes
            .OrderByDescending(process => process.WorkingSetBytes)
            .Take(5)
            .Select(process =>
                $"• {process.Name} (PID {process.Id}): {MainWindow.FormatBytes(process.WorkingSetBytes)}");

        return $"Read-only process snapshot: {_processes.Count:N0} accessible processes; "
            + $"summed working sets {MainWindow.FormatBytes(totalWorkingSet)}.\n"
            + "This is not total physical RAM usage because process working sets can contain shared pages.\n"
            + "Largest working sets:\n" + string.Join("\n", topProcesses);
    }

    private string GetTopProcesses(string argument)
    {
        var count = 10;
        if (argument.Length > 0
            && (!int.TryParse(argument, out count) || count is < 1 or > 20))
            return "Usage: /top [count], where count is between 1 and 20.";

        var rows = _processes
            .OrderByDescending(process => process.WorkingSetBytes)
            .Take(count)
            .Select((process, index) =>
                $"{index + 1}. {process.Name} (PID {process.Id}) — "
                + $"{MainWindow.FormatBytes(process.WorkingSetBytes)} RAM; "
                + $"{process.CpuPercent:N1}% CPU");
        return $"Top {Math.Min(count, _processes.Count)} processes by working set:\n"
            + string.Join("\n", rows);
    }

    private string GetCpuSummary()
    {
        var rows = _processes
            .OrderByDescending(process => process.CpuPercent)
            .Take(10)
            .Select((process, index) =>
                $"{index + 1}. {process.Name} (PID {process.Id}) — "
                + $"{process.CpuPercent:N1}% CPU; "
                + $"{MainWindow.FormatBytes(process.WorkingSetBytes)} RAM");
        return "Top processes by current CPU sample:\n" + string.Join("\n", rows);
    }

    private string GetGpuSummary()
    {
        var processesWithGpuData = _processes
            .Where(process => process.DedicatedGpuBytes.HasValue || process.SharedGpuBytes.HasValue)
            .OrderByDescending(process =>
                (process.DedicatedGpuBytes ?? 0) + (process.SharedGpuBytes ?? 0))
            .Take(10)
            .ToArray();
        if (processesWithGpuData.Length == 0)
            return "Per-process GPU memory data is unavailable in this snapshot.";

        var rows = processesWithGpuData.Select((process, index) =>
            $"{index + 1}. {process.Name} (PID {process.Id}) — "
            + $"dedicated {FormatOptionalBytes(process.DedicatedGpuBytes)}, "
            + $"shared {FormatOptionalBytes(process.SharedGpuBytes)}");
        return "Processes with the highest reported GPU memory:\n" + string.Join("\n", rows);
    }

    private string GetProcessSummary(string query)
    {
        if (query.Length == 0)
            return "Usage: /process <process name or PID>.";

        var matches = int.TryParse(query, out var processId)
            ? _processes.Where(process => process.Id == processId).ToArray()
            : _processes
                .Where(process => process.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(process => process.WorkingSetBytes)
                .Take(10)
                .ToArray();
        if (matches.Length == 0)
            return $"No process matching \"{query}\" was found in the current snapshot.";

        return string.Join("\n\n", matches.Select(process =>
            $"{process.Name} (PID {process.Id})\n"
            + $"RAM: {MainWindow.FormatBytes(process.WorkingSetBytes)} working set; "
            + $"{MainWindow.FormatBytes(process.PrivateCommitBytes)} private commit\n"
            + $"CPU: {process.CpuPercent:N1}%"
            + (process.DedicatedGpuBytes.HasValue || process.SharedGpuBytes.HasValue
                ? $"\nGPU memory: dedicated {FormatOptionalBytes(process.DedicatedGpuBytes)}, "
                    + $"shared {FormatOptionalBytes(process.SharedGpuBytes)}"
                : "")));
    }

    private static string FormatOptionalBytes(ulong? value) =>
        value.HasValue ? MainWindow.FormatBytes(value.Value) : "unavailable";

    private void AppendMessage(string speaker, string message)
    {
        ConversationText.Text = ConversationText.Text.StartsWith("General chat is ready.", StringComparison.Ordinal)
            || ConversationText.Text.StartsWith("Chatting about ", StringComparison.Ordinal)
            || ConversationText.Text.StartsWith("General chat selected.", StringComparison.Ordinal)
            ? $"{speaker}: {message}"
            : $"{ConversationText.Text}{Environment.NewLine}{Environment.NewLine}{speaker}: {message}";
        ConversationText.CaretIndex = ConversationText.Text.Length;
        ConversationText.ScrollToEnd();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void WindowMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void WindowMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
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
}
