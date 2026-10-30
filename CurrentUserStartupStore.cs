using System.IO;
using Microsoft.Win32;

namespace TaskManagerPlusPlus;

internal sealed record StartupEntry(
    string Name,
    string Location,
    string Command,
    string EntryName,
    bool IsEnabled,
    bool IsRegistry);

internal static class CurrentUserStartupStore
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledRunRegistryPath = @"Software\TaskManager++\DisabledStartup\Run";
    private static readonly string StartupFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    private static readonly string DisabledStartupFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskManager++",
        "DisabledStartup");
    private static readonly HashSet<string> StartupFileExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".lnk", ".url", ".exe", ".bat", ".cmd" };

    public static IReadOnlyList<StartupEntry> GetEntries()
    {
        var entries = new List<StartupEntry>();
        ReadRegistryEntries(RunRegistryPath, true, entries);
        ReadRegistryEntries(DisabledRunRegistryPath, false, entries);
        ReadStartupFiles(StartupFolder, true, entries);
        ReadStartupFiles(DisabledStartupFolder, false, entries);
        return entries
            .OrderBy(entry => entry.IsEnabled ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void Disable(StartupEntry entry)
    {
        if (!entry.IsEnabled)
            throw new InvalidOperationException("This startup item is already disabled.");

        if (entry.IsRegistry)
            MoveRegistryEntry(RunRegistryPath, DisabledRunRegistryPath, entry.EntryName);
        else
            MoveStartupFile(StartupFolder, DisabledStartupFolder, entry.EntryName);
    }

    public static void Restore(StartupEntry entry)
    {
        if (entry.IsEnabled)
            throw new InvalidOperationException("This startup item is already enabled.");

        if (entry.IsRegistry)
            MoveRegistryEntry(DisabledRunRegistryPath, RunRegistryPath, entry.EntryName);
        else
            MoveStartupFile(DisabledStartupFolder, StartupFolder, entry.EntryName);
    }

    private static void ReadRegistryEntries(
        string registryPath,
        bool isEnabled,
        ICollection<StartupEntry> entries)
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
        if (key is null)
            return;

        foreach (var name in key.GetValueNames().Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var command = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (command is null)
                continue;

            entries.Add(new StartupEntry(
                name,
                isEnabled ? "Current-user Run key" : "Disabled Run entry",
                Convert.ToString(command) ?? "",
                name,
                isEnabled,
                IsRegistry: true));
        }
    }

    private static void ReadStartupFiles(
        string folder,
        bool isEnabled,
        ICollection<StartupEntry> entries)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var file in Directory.EnumerateFiles(folder)
            .Where(file => StartupFileExtensions.Contains(Path.GetExtension(file))))
        {
            var name = Path.GetFileName(file);
            entries.Add(new StartupEntry(
                Path.GetFileNameWithoutExtension(name),
                isEnabled ? "Current-user Startup folder" : "Disabled Startup item",
                file,
                name,
                isEnabled,
                IsRegistry: false));
        }
    }

    private static void MoveRegistryEntry(string sourcePath, string destinationPath, string name)
    {
        using var source = Registry.CurrentUser.OpenSubKey(sourcePath, writable: true)
            ?? throw new InvalidOperationException("The startup registry entry no longer exists.");
        using var destination = Registry.CurrentUser.CreateSubKey(destinationPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the current-user disabled startup store.");

        if (destination.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new IOException($"A startup entry named \"{name}\" already exists at the destination.");
        if (!source.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The startup registry entry changed; refresh the list and try again.");

        var value = source.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            ?? throw new InvalidDataException($"The startup entry \"{name}\" has no command.");
        var kind = source.GetValueKind(name);
        destination.SetValue(name, value, kind);
        source.DeleteValue(name, throwOnMissingValue: true);
    }

    private static void MoveStartupFile(string sourceFolder, string destinationFolder, string name)
    {
        var sourcePath = Path.Combine(sourceFolder, name);
        var destinationPath = Path.Combine(destinationFolder, name);
        if (!File.Exists(sourcePath))
            throw new InvalidOperationException("The startup file no longer exists; refresh the list and try again.");
        if (File.Exists(destinationPath))
            throw new IOException($"A startup file named \"{name}\" already exists at the destination.");

        Directory.CreateDirectory(destinationFolder);
        File.Copy(sourcePath, destinationPath);
        try
        {
            File.Delete(sourcePath);
        }
        catch (IOException)
        {
            File.Delete(destinationPath);
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            File.Delete(destinationPath);
            throw;
        }
    }
}
