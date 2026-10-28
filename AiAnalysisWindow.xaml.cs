using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace TaskManagerPlusPlus;

public partial class AiAnalysisWindow : Window
{
    private readonly string _apiKey;
    private readonly IReadOnlyList<ProcessMemoryInfo> _processes;

    internal AiAnalysisWindow(string apiKey, IReadOnlyList<ProcessMemoryInfo> processes)
    {
        InitializeComponent();
        _apiKey = apiKey;
        _processes = processes;
        Loaded += AnalyzeProcesses;
    }

    private async void AnalyzeProcesses(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Analyzing process data with NVIDIA AI…";
        AnalysisText.Text = string.Join(Environment.NewLine, _processes.Select((process, index) =>
            $"{index + 1}. {process.Name} (PID {process.Id}) — {MainWindow.FormatBytes(process.WorkingSetBytes)} RAM"));

        try
        {
            var analysis = await NvidiaAiClient.AnalyzeProcessesAsync(_apiKey, _processes);
            if (analysis.Trim().Equals("NO_ACTIONABLE_FINDINGS", StringComparison.OrdinalIgnoreCase))
            {
                AnalysisText.Text = "No unusually high usage or clear optimization opportunities were found in this snapshot.";
                StatusText.Text = "Nothing needs attention.";
            }
            else
            {
                AnalysisText.Text = analysis;
                StatusText.Text = "Actionable findings";
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

    private void ShowAnalysisError(string message)
    {
        StatusText.Text = "Analysis failed.";
        AnalysisText.Text = message;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
