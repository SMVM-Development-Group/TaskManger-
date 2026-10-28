using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows;

namespace TaskManagerPlusPlus;

public partial class AiSettingsWindow : Window
{
    public AiSettingsWindow()
    {
        InitializeComponent();
        try
        {
            ModelBox.Text = AiSettingsStore.LoadModel();
            StatusText.Text = AiSettingsStore.HasSavedApiKey
                ? "A key is saved. Enter a new key only if you want to replace it."
                : "No API key is saved yet.";
        }
        catch (IOException ex)
        {
            StatusText.Text = $"Could not load AI settings: {ex.Message}";
        }
    }

    private void SaveKeyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AiSettingsStore.SaveModel(ModelBox.Text);
            if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password))
                AiSettingsStore.SaveApiKey(ApiKeyBox.Password);
            else if (!AiSettingsStore.HasSavedApiKey)
            {
                StatusText.Text = "Enter an NVIDIA API key before saving settings.";
                return;
            }
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (CryptographicException ex)
        {
            ShowStorageError(ex);
        }
        catch (IOException ex)
        {
            ShowStorageError(ex);
        }
        catch (InvalidDataException ex)
        {
            ShowStorageError(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowStorageError(ex);
        }
    }

    private async void LoadModelsButton_Click(object sender, RoutedEventArgs e)
    {
        string? apiKey = ApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                apiKey = AiSettingsStore.LoadApiKey();
            }
            catch (CryptographicException ex)
            {
                ShowStorageError(ex);
                return;
            }
            catch (IOException ex)
            {
                ShowStorageError(ex);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                ShowStorageError(ex);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusText.Text = "Enter an NVIDIA API key or save one before loading available models.";
            return;
        }

        LoadModelsButton.IsEnabled = false;
        StatusText.Text = "Loading models available to this API key…";
        try
        {
            var models = await NvidiaAiClient.GetAvailableModelsAsync(apiKey);
            var selectedModel = ModelBox.Text;
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
                : $"Loaded {models.Count} models for this API key. Choose one and save settings.";
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

    private void RemoveKeyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AiSettingsStore.DeleteApiKey();
            ApiKeyBox.Clear();
            StatusText.Text = "Saved API key removed.";
        }
        catch (IOException ex)
        {
            ShowStorageError(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowStorageError(ex);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowStorageError(Exception exception)
    {
        MessageBox.Show(this, $"Could not save API key: {exception.Message}", "AI settings",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
