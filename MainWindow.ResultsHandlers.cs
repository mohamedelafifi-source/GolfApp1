// MainWindow.ResultsHandlers.cs
// Handlers for Results menu and Enter Results header
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        // Results menu: show import dialog (Import Results -> Import PDF)
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

        // Show the Enter Results header UI
        private void OnEnterResultsClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Enter Results selected.");

            // Hide club editor and show results area
            if (EditorArea is not null) EditorArea.Visibility = Visibility.Collapsed;
            if (ResultsArea is not null) ResultsArea.Visibility = Visibility.Visible;

            // Initialize header
            if (ResultsDatePicker is not null) ResultsDatePicker.Date = DateTimeOffset.Now.Date;

            // Refresh local clubs and populate club/venue lists
            RefreshLocalClubsFromVm();
            if (ResultsClubCombo is not null) ResultsClubCombo.ItemsSource = _clubs.Select(c => c.ShortName).ToList();
            if (ResultsVenueCombo is not null) ResultsVenueCombo.ItemsSource = _clubs.Select(c => c.LongName).ToList();

            // hide entry panel until Proceed
            if (ResultsEntryPanel is not null) ResultsEntryPanel.Visibility = Visibility.Collapsed;

            UpdateProceedButtonState();
        }

        // Called when the club combo selection changes in the header.
        private async void OnResultsClubChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateProceedButtonState();

            try
            {
                if (sender is not ComboBox cb) return;
                var selected = cb.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(selected))
                {
                    if (PlayerNameCombo is not null) PlayerNameCombo.ItemsSource = null;
                    if (PartnerCombo is not null) PartnerCombo.ItemsSource = null;
                    UpdateStatus("Club selection cleared.");
                    return;
                }

                UpdateStatus($"Loading players for club '{selected}'...");
                await LoadPlayersForResultsAsync(selected);
                UpdateStatus($"Loaded {_vm?.Players.Count ?? 0} players for '{selected}'.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Failed to load players: " + ex.Message);
                if (PlayerNameCombo is not null) PlayerNameCombo.ItemsSource = null;
                if (PartnerCombo is not null) PartnerCombo.ItemsSource = null;
            }
        }

        // Called when the date picker value changes.
        private void OnResultsDateChanged(object? sender, DatePickerValueChangedEventArgs e)
        {
            UpdateProceedButtonState();
        }

        // Called when venue selection changes.
        private void OnResultsVenueChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateProceedButtonState();
        }

        // Helper: enable Proceed only when date, club and venue are set.
        private void UpdateProceedButtonState()
        {
            try
            {
                var dateOk = ResultsDatePicker is not null && ResultsDatePicker.Date != DateTimeOffset.MinValue;
                var clubOk = ResultsClubCombo is not null && ResultsClubCombo.SelectedItem != null;
                var venueOk = ResultsVenueCombo is not null && ResultsVenueCombo.SelectedItem != null;

                if (ProceedResultsButton is not null) ProceedResultsButton.IsEnabled = dateOk && clubOk && venueOk;
            }
            catch
            {
                if (ProceedResultsButton is not null) ProceedResultsButton.IsEnabled = false;
            }
        }

        private Task InvokeImportHandlerAsync()
        {
            OnImportPdfClicked(this, new RoutedEventArgs());
            return Task.CompletedTask;
        }

        // Cancel/Exit from Results entry — return to main menu with an empty screen
        private void OnCancelResultsClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                // Hide results UI and ensure club editor is hidden so the main area is empty
                if (ResultsEntryPanel is not null) ResultsEntryPanel.Visibility = Visibility.Collapsed;
                if (ResultsArea is not null) ResultsArea.Visibility = Visibility.Collapsed;
                if (EditorArea is not null) EditorArea.Visibility = Visibility.Collapsed;

                // Clear header selections
                if (ResultsClubCombo is not null) ResultsClubCombo.SelectedIndex = -1;
                if (ResultsVenueCombo is not null) ResultsVenueCombo.SelectedIndex = -1;
                if (ResultsDatePicker is not null) ResultsDatePicker.Date = DateTimeOffset.MinValue;

                // Clear lower-entry fields
                if (PlayerNameCombo is not null) PlayerNameCombo.SelectedIndex = -1;
                if (PartnerCombo is not null) PartnerCombo.SelectedIndex = -1;
                if (HcpTextBox is not null) HcpTextBox.Text = string.Empty;
                if (ResultTextBox is not null) ResultTextBox.Text = string.Empty;
                if (PositionTextBox is not null) PositionTextBox.Text = string.Empty;

                // Reset buffer/state
                _resultBuffer.Clear();
                _resultIndex = -1;

                // Disable proceed until header is filled next time
                if (ProceedResultsButton is not null) ProceedResultsButton.IsEnabled = false;

                UpdateStatus("Returned to main menu.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Error closing results UI: " + ex.Message);
            }
        }
        
    }

}