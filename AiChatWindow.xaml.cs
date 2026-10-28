using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace TaskManagerPlusPlus;

public partial class AiChatWindow : Window
{
    private readonly string _apiKey;
    private ProcessMemoryInfo? _process;
    private readonly List<(string Role, string Content)> _conversation = [];

    internal AiChatWindow(
        string apiKey,
        IReadOnlyList<ProcessMemoryInfo> processes,
        ProcessMemoryInfo? selectedProcess)
    {
        InitializeComponent();
        _apiKey = apiKey;
        _process = selectedProcess;
        var targets = processes
            .Select(process => new AiChatTarget(process.DisplayLabel, process))
            .Prepend(new AiChatTarget("General chat (no process selected)", null))
            .ToArray();
        ProcessBox.ItemsSource = targets;
        ProcessBox.SelectedItem = selectedProcess is null
            ? targets[0]
            : targets.FirstOrDefault(target => target.Process?.Id == selectedProcess.Id) ?? targets[0];
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
            ? "General chat is ready. Ask a question, or choose a running process above for focused context."
            : $"Chatting about {selectedProcess.Name} (PID {selectedProcess.Id}).";
    }

    private void ProcessBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessBox.SelectedItem is not AiChatTarget target
            || target.Process?.Id == _process?.Id)
            return;

        _process = target.Process;
        _conversation.Clear();
        ConversationText.Text = target.Process is null
            ? "General chat selected. Ask a new question to begin."
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
            var answer = await NvidiaAiClient.ChatAboutProcessAsync(_apiKey, model, _process, _conversation);
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
}
