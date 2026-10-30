using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;

namespace TaskManagerPlusPlus;

public partial class StartupManagerWindow : Window
{
    public StartupManagerWindow()
    {
        InitializeComponent();
        ReloadEntries();
    }

    private void ReloadEntries()
    {
        try
        {
            StartupEntriesGrid.ItemsSource = CurrentUserStartupStore.GetEntries();
            StatusText.Text = "Only current-user Run and Startup-folder entries are managed. Disabled entries can be restored here.";
            UpdateActionButtons();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            ShowError("Could not read current-user startup entries", ex);
        }
    }

    private void StartupEntriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateActionButtons();

    private void UpdateActionButtons()
    {
        if (StartupEntriesGrid.SelectedItem is not StartupEntry entry)
        {
            DisableButton.IsEnabled = false;
            RestoreButton.IsEnabled = false;
            return;
        }

        DisableButton.IsEnabled = entry.IsEnabled;
        RestoreButton.IsEnabled = !entry.IsEnabled;
    }

    private void DisableButton_Click(object sender, RoutedEventArgs e)
    {
        if (StartupEntriesGrid.SelectedItem is not StartupEntry { IsEnabled: true } entry)
            return;

        var answer = MessageBox.Show(
            this,
            $"Disable \"{entry.Name}\" from starting automatically for your Windows account?\n\n"
                + "You can restore it later from this window.",
            "Confirm startup change",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        ChangeStartupEntry(entry, disable: true);
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (StartupEntriesGrid.SelectedItem is not StartupEntry { IsEnabled: false } entry)
            return;

        var answer = MessageBox.Show(
            this,
            $"Restore \"{entry.Name}\" to startup? It may launch automatically the next time you sign in.",
            "Confirm startup change",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        ChangeStartupEntry(entry, disable: false);
    }

    private void ChangeStartupEntry(StartupEntry entry, bool disable)
    {
        try
        {
            if (disable)
                CurrentUserStartupStore.Disable(entry);
            else
                CurrentUserStartupStore.Restore(entry);

            ReloadEntries();
            StatusText.Text = disable
                ? $"Disabled \"{entry.Name}\". You can restore it from this list."
                : $"Restored \"{entry.Name}\" to startup.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or SecurityException or InvalidOperationException or InvalidDataException)
        {
            ShowError("Could not update the startup entry", ex);
            ReloadEntries();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => ReloadEntries();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string heading, Exception exception)
    {
        StatusText.Text = $"{heading}: {exception.Message}";
        MessageBox.Show(this, $"{heading}:\n{exception.Message}",
            "Startup apps", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
