//MainWindow.ResultsHandler.cs
//============================
using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        // Re-added Results menu functionality.
        private async void OnResultsClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Results menu opened.");

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = "Choose an action for results:",
                TextWrapping = TextWrapping.Wrap
            });

            var dlg = new ContentDialog
            {
                Title = "Import Results",
                Content = panel,
                PrimaryButtonText = "Import PDF",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot == null)
            {
                UpdateStatus("Results action: UI unavailable.");
                return;
            }

            // IMPORTANT: ShowAsync() returns IAsyncOperation<T> — use AsTask() before awaiting
            var result = await dlg.ShowAsync().AsTask();
            if (result == ContentDialogResult.Primary)
            {
                await InvokeImportHandlerAsync();
            }
            else
            {
                UpdateStatus("Results action cancelled.");
            }
        }

        // Show the new Enter Results UI
        private void OnEnterResultsClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Enter Results selected.");

            // Hide editor and show ResultsArea
            EditorArea.Visibility = Visibility.Collapsed;
            ResultsArea.Visibility = Visibility.Visible;

            // initialize header values
            ResultsDatePicker.Date = DateTime.Now.Date;
            ResultsClubCombo.ItemsSource = null;
            ResultsVenueCombo.ItemsSource = null;
            PlayerNameCombo.ItemsSource = null;
            PartnerCombo.ItemsSource = null;

            // populate clubs from local cache
            RefreshLocalClubsFromVm();
            ResultsClubCombo.ItemsSource = _clubs.ConvertAll(c => c.ShortName);

            // set venues to same list by default
            ResultsVenueCombo.ItemsSource = _clubs.ConvertAll(c => c.LongName);

            ResultsEntryPanel.Visibility = Visibility.Collapsed;
            UpdateStatus("Enter Results: set header fields, then Proceed.");
        }

        // SelectionChanged handler for the club combo in the Enter Results header.
        // Loads players for the selected club into the Player/Partner combo boxes.
        private async void OnResultsClubChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is not ComboBox cb) return;
                var selected = cb.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(selected))
                {
                    PlayerNameCombo.ItemsSource = null;
                    PartnerCombo.ItemsSource = null;
                    UpdateStatus("Club selection cleared.");
                    return;
                }

                UpdateStatus($"Loading players for club '{selected}'...");
                // Use existing loader (defined in EnterResults partial)
                await LoadPlayersForResultsAsync(selected);
                UpdateStatus($"Loaded {_vm?.Players.Count ?? 0} players for '{selected}'.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Failed to load players: " + ex.Message);
                PlayerNameCombo.ItemsSource = null;
                PartnerCombo.ItemsSource = null;
            }
        }

        private Task InvokeImportHandlerAsync()
        {
            OnImportPdfClicked(this, new RoutedEventArgs());
            return Task.CompletedTask;
        }
    }
}