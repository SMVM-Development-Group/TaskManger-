using System.Security.Cryptography;
using System.IO;
using System.Text;

namespace TaskManagerPlusPlus;

internal enum AiProcessActionMode
{
    AskBeforeTerminating,
    Automatic
}

internal static class AiSettingsStore
{
    private static readonly string KeyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskManager++",
        "ai-key.dat");
    private static readonly string ModelFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskManager++",
        "ai-model.txt");
    private static readonly string ProcessActionModeFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskManager++",
        "ai-process-action-mode.txt");
    public const string DefaultModel = "nvidia/nemotron-3.5-bolt-e4b";

    public static bool HasSavedApiKey => File.Exists(KeyFilePath);

    public static string LoadModel()
    {
        if (!File.Exists(ModelFilePath))
            return DefaultModel;

        var model = File.ReadAllText(ModelFilePath).Trim();
        return string.IsNullOrWhiteSpace(model)
            ? throw new InvalidDataException("The saved AI model name is empty. Choose a model in AI settings and save it.")
            : model;
    }

    public static void SaveModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Choose or enter a model name before saving.", nameof(model));

        var directory = Path.GetDirectoryName(ModelFilePath)
            ?? throw new InvalidOperationException("The AI model storage path is invalid.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(ModelFilePath, model.Trim());
    }

    public static AiProcessActionMode LoadProcessActionMode()
    {
        if (!File.Exists(ProcessActionModeFilePath))
            return AiProcessActionMode.AskBeforeTerminating;

        return File.ReadAllText(ProcessActionModeFilePath).Trim() switch
        {
            "ask" => AiProcessActionMode.AskBeforeTerminating,
            "automatic" => AiProcessActionMode.Automatic,
            _ => throw new InvalidDataException(
                "The saved AI process action mode is invalid. Open AI settings and choose a process action mode.")
        };
    }

    public static void SaveProcessActionMode(AiProcessActionMode mode)
    {
        var value = mode switch
        {
            AiProcessActionMode.AskBeforeTerminating => "ask",
            AiProcessActionMode.Automatic => "automatic",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var directory = Path.GetDirectoryName(ProcessActionModeFilePath)
            ?? throw new InvalidOperationException("The AI process action storage path is invalid.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(ProcessActionModeFilePath, value);
    }

    public static string? LoadApiKey()
    {
        if (!File.Exists(KeyFilePath))
            return null;

        var encryptedKey = File.ReadAllBytes(KeyFilePath);
        var keyBytes = ProtectedData.Unprotect(encryptedKey, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(keyBytes);
    }

    public static void SaveApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Enter an API key before saving.", nameof(apiKey));

        var directory = Path.GetDirectoryName(KeyFilePath)
            ?? throw new InvalidOperationException("The API key storage path is invalid.");
        Directory.CreateDirectory(directory);

        var keyBytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        var encryptedKey = ProtectedData.Protect(keyBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(KeyFilePath, encryptedKey);
    }

    public static void DeleteApiKey()
    {
        if (File.Exists(KeyFilePath))
            File.Delete(KeyFilePath);
    }
}
