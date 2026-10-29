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
    private readonly List<RecommendationRow> _recommendations = [];

    internal AiDeepScanWindow(
        MainWindow mainWindow,
        string apiKey,
        IReadOnlyList<ProcessMemoryInfo> processes,
        AiProcessActionMode actionMode)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _apiKey = apiKey;
        _processes = processes;
        _actionMode = actionMode;
        ModeText.Text = actionMode == AiProcessActionMode.Automatic
            ? "Automatic mode is enabled. AI recommendations may be ended without another prompt; unsaved work can be lost."
            : "Confirmation mode is enabled. You will be asked before each recommended process is ended.";
        RecommendationsList.ItemsSource = _recommendations;
        Loaded += AnalyzeProcesses;
    }

    private async void AnalyzeProcesses(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Preparing sequential NVIDIA requests…";
        SummaryText.Text = $"Scanning {_processes.Count:N0} processes.";

        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var analysis = await NvidiaAiClient.AnalyzeProcessesForTerminationAsync(_apiKey, _processes, progress);
            SummaryText.Text = analysis.Summary;

            foreach (var recommendation in analysis.Recommendations)
            {
                _recommendations.Add(new RecommendationRow(
                    recommendation,
                    _actionMode == AiProcessActionMode.AskBeforeTerminating));
            }

            if (_recommendations.Count == 0)
            {
                StatusText.Text = "No process met the criteria for an ending recommendation.";
                return;
            }

            if (_actionMode == AiProcessActionMode.Automatic)
            {
                var endedCount = 0;
                foreach (var recommendation in _recommendations)
                {
                    var ended = _mainWindow.TryEndProcess(recommendation.Process, out var result);
                    recommendation.Result = result;
                    recommendation.CanEnd = false;
                    if (ended)
                        endedCount++;
                }

                StatusText.Text = $"Automatic deep scan finished. Ended {endedCount} of {_recommendations.Count} recommended processes. See each result below.";
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

    private void ShowAnalysisError(string message)
    {
        StatusText.Text = "Deep scan failed. No processes were ended.";
        SummaryText.Text = message;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class RecommendationRow : INotifyPropertyChanged
    {
        private bool _canEnd;
        private string _result = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public ProcessMemoryInfo Process { get; }
        public string ProcessLabel =>
            $"{Process.Name} (PID {Process.Id}) · {MainWindow.FormatBytes(Process.WorkingSetBytes)} RAM";
        public string Reason { get; }
        public Visibility ActionVisibility { get; }

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

        public RecommendationRow(AiProcessTerminationRecommendation recommendation, bool canEnd)
        {
            Process = recommendation.Process;
            Reason = recommendation.Reason;
            CanEnd = canEnd;
            ActionVisibility = canEnd ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
