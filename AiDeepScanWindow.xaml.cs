using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace TaskManagerPlusPlus;

public partial class AiDeepScanWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly string _apiKey;
    private readonly IReadOnlyList<ProcessMemoryInfo> _processes;
    private readonly AiProcessActionMode _actionMode;
    private readonly bool _debloatMode;
    private readonly List<RecommendationRow> _recommendations = [];

    internal AiDeepScanWindow(
        MainWindow mainWindow,
        string apiKey,
        IReadOnlyList<ProcessMemoryInfo> processes,
        AiProcessActionMode actionMode,
        bool debloatMode = false)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _apiKey = apiKey;
        _processes = processes;
        _actionMode = actionMode;
        _debloatMode = debloatMode;
        ModeText.Text = debloatMode
            ? "Debloat review: suggestions are based on measured activity, not guesses about what you use. Nothing is changed until you select and confirm it."
            : "Deep scan review: nothing is changed until you choose an action.";
        EndSelectedButton.Visibility = actionMode == AiProcessActionMode.Autopilot
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecommendationsList.ItemsSource = _recommendations;
        RecommendationsList.SelectionChanged += (_, _) => UpdateSelectedActionState();
        Loaded += AnalyzeProcesses;
    }

    private async void AnalyzeProcesses(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Preparing sequential NVIDIA requests…";
        SummaryText.Text = $"Scanning {_processes.Count:N0} processes.";

        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var analysis = await NvidiaAiClient.AnalyzeProcessesForTerminationAsync(
                _apiKey, _processes, progress, _debloatMode);
            SummaryText.Text = analysis.Summary;

            foreach (var recommendation in analysis.Recommendations)
            {
                _recommendations.Add(new RecommendationRow(
                    recommendation,
                    _actionMode == AiProcessActionMode.AskBeforeTerminating,
                    _actionMode == AiProcessActionMode.Autopilot));
            }

            if (_recommendations.Count == 0)
            {
                StatusText.Text = _debloatMode
                    ? "No process met the measured low-activity criteria for a debloat suggestion."
                    : "No process met the criteria for an ending recommendation.";
                return;
            }

            if (_actionMode == AiProcessActionMode.Autopilot)
            {
                StatusText.Text = "Select the recommendations you approve, then choose End selected tasks… to review and confirm.";
            }
            else
            {
                StatusText.Text = "Review each recommendation and choose End task… to confirm it individually.";
            }
        }
        catch (HttpRequestException ex)
        {
            ShowAnalysisError(ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            ShowAnalysisError($"The request timed out or was canceled. {ex.Message}");
        }
        catch (JsonException ex)
        {
            ShowAnalysisError($"NVIDIA returned an unreadable response. {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            ShowAnalysisError(ex.Message);
        }
        catch (IOException ex)
        {
            ShowAnalysisError($"Could not load the saved AI model setting: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowAnalysisError($"Could not load the saved AI model setting: {ex.Message}");
        }
        catch (CryptographicException ex)
        {
            ShowAnalysisError($"The saved API key could not be decrypted. {ex.Message}");
        }
        finally
        {
            CloseButton.IsEnabled = true;
            UpdateSelectedActionState();
        }
    }

    private void EndRecommendedProcessButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecommendationRow recommendation })
            return;

        var answer = MessageBox.Show(
            this,
            $"The AI recommends ending \"{recommendation.Process.Name}\" (PID {recommendation.Process.Id}).\n\n"
                + $"{recommendation.Reason}\n\nUnsaved work may be lost. End this process?",
            "Confirm AI-recommended end task",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        _mainWindow.TryEndProcess(recommendation.Process, out var result);
        recommendation.Result = result;
        recommendation.CanEnd = false;
    }

    private void EndSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _recommendations
            .Where(recommendation => recommendation.IsSelectedForAutopilot && recommendation.CanEnd)
            .ToArray();
        if (selected.Length == 0)
            return;

        var processList = string.Join(Environment.NewLine, selected.Select(item =>
            $"• {item.Process.Name} (PID {item.Process.Id})"));
        var answer = MessageBox.Show(
            this,
            $"End these {selected.Length} selected processes? Unsaved work may be lost.\n\n{processList}",
            "Confirm selected AI actions",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        foreach (var recommendation in selected)
        {
            _mainWindow.TryEndProcess(recommendation.Process, out var result);
            recommendation.Result = result;
            recommendation.CanEnd = false;
            recommendation.IsSelectedForAutopilot = false;
        }

        UpdateSelectedActionState();
    }

    private void AutopilotSelection_Changed(object sender, RoutedEventArgs e) =>
        UpdateSelectedActionState();

    private void UpdateSelectedActionState()
    {
        EndSelectedButton.IsEnabled = CloseButton.IsEnabled
            && _recommendations.Any(recommendation =>
                recommendation.IsSelectedForAutopilot && recommendation.CanEnd);
    }

    private void ShowAnalysisError(string message)
    {
        StatusText.Text = "Deep scan failed. No processes were ended.";
        SummaryText.Text = message;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class RecommendationRow : INotifyPropertyChanged
    {
        private bool _canEnd;
        private bool _isSelectedForAutopilot;
        private string _result = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public ProcessMemoryInfo Process { get; }
        public string ProcessLabel =>
            $"{Process.Name} (PID {Process.Id}) · {MainWindow.FormatBytes(Process.WorkingSetBytes)} RAM";
        public string Reason { get; }
        public Visibility ActionVisibility { get; }
        public Visibility SelectionVisibility { get; }

        public bool IsSelectedForAutopilot
        {
            get => _isSelectedForAutopilot;
            set
            {
                if (_isSelectedForAutopilot == value)
                    return;
                _isSelectedForAutopilot = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedForAutopilot)));
            }
        }

        public bool CanEnd
        {
            get => _canEnd;
            set
            {
                if (_canEnd == value)
                    return;
                _canEnd = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEnd)));
            }
        }

        public string Result
        {
            get => _result;
            set
            {
                if (_result == value)
                    return;
                _result = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Result)));
            }
        }

        public RecommendationRow(
            AiProcessTerminationRecommendation recommendation,
            bool showIndividualAction,
            bool showAutopilotSelection)
        {
            Process = recommendation.Process;
            Reason = recommendation.Reason;
            CanEnd = true;
            ActionVisibility = showIndividualAction ? Visibility.Visible : Visibility.Collapsed;
            SelectionVisibility = showAutopilotSelection ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
