// MainWindow.ResultsHandlers.cs
//===============================

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        // Context for results entry (set by New/Existing Results workflows)
        private string? _currentResultsVenue;
        private DateTime? _currentResultsDate;
        private string? _currentResultsClub;
        private bool _isEditingExistingResults;

        // Re-entrancy guard for initialization
        private bool _isInitializingResults = false;

        // Legacy handler: "Enter Results" menu item (can be removed if menu is updated)
        private void OnEnterResultsClicked(object sender, RoutedEventArgs e)
        {
            if (_isInitializingResults) return;

            try
            {
                _isInitializingResults = true;
                UpdateStatus("Enter Results selected.");

                if (EditorArea is not null) EditorArea.Visibility = Visibility.Collapsed;
                if (ResultsArea is not null) ResultsArea.Visibility = Visibility.Visible;
                if (ResultsDatePicker is not null) ResultsDatePicker.Date = DateTimeOffset.Now.Date;

                // Detach handlers during initialization
                if (ResultsClubCombo is not null)
                    ResultsClubCombo.SelectionChanged -= OnResultsClubChanged;
                if (ResultsVenueCombo is not null)
                    ResultsVenueCombo.SelectionChanged -= OnResultsVenueChanged;
                if (ResultsDatePicker is not null)
                    ResultsDatePicker.DateChanged -= OnResultsDateChanged;

                try
                {
                    RefreshLocalClubsFromVm();

                    if (ResultsClubCombo is not null)
                    {
                        ResultsClubCombo.SelectedIndex = -1;
                        ResultsClubCombo.SelectedItem = null;
                        ResultsClubCombo.ItemsSource = _clubs.Select(cl => cl.ShortName).ToList();
                    }

                    if (ResultsVenueCombo is not null)
                    {
                        ResultsVenueCombo.SelectedIndex = -1;
                        ResultsVenueCombo.SelectedItem = null;
                        ResultsVenueCombo.ItemsSource = _clubs.Select(cl => cl.LongName).ToList();
                    }
                }
                finally
                {
                    // Re-attach handlers
                    if (ResultsClubCombo is not null)
                        ResultsClubCombo.SelectionChanged += OnResultsClubChanged;
                    if (ResultsVenueCombo is not null)
                        ResultsVenueCombo.SelectionChanged += OnResultsVenueChanged;
                    if (ResultsDatePicker is not null)
                        ResultsDatePicker.DateChanged += OnResultsDateChanged;
                }

                if (ResultsEntryPanel is not null)
                    ResultsEntryPanel.Visibility = Visibility.Collapsed;

                UpdateProceedButtonState();
            }
            finally
            {
                _isInitializingResults = false;
            }
        }

        // Called when the club combo selection changes in the header
        private void OnResultsClubChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingResults) return;

            UpdateProceedButtonState();

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
            // NOTE: LoadPlayersForResultsAsync is defined elsewhere in another partial file
            _ = LoadPlayersForResultsAsync(selected);
        }

        // Called when the date picker value changes
        private void OnResultsDateChanged(object? sender, DatePickerValueChangedEventArgs e)
        {
            if (_isInitializingResults) return;
            UpdateProceedButtonState();
        }

        // Called when venue selection changes
        private void OnResultsVenueChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingResults) return;
            UpdateProceedButtonState();
        }

        // Helper: enable Proceed and Import PDF only when date, club and venue are set
        private void UpdateProceedButtonState()
        {
            if (ProceedResultsButton == null || ImportPdfButton == null) return;
            if (ResultsDatePicker == null || ResultsClubCombo == null || ResultsVenueCombo == null) return;

            try
            {
                var dateOk = ResultsDatePicker.Date != DateTimeOffset.MinValue;
                var clubOk = ResultsClubCombo.SelectedIndex != -1;
                var venueOk = ResultsVenueCombo.SelectedIndex != -1;

                var enabled = dateOk && clubOk && venueOk;

                if (ProceedResultsButton.IsEnabled != enabled)
                    ProceedResultsButton.IsEnabled = enabled;

                if (ImportPdfButton.IsEnabled != enabled)
                    ImportPdfButton.IsEnabled = enabled;
            }
            catch
            {
                // Swallow errors
            }
        }

        // Check for duplicate results before saving
        private async Task<bool> CheckAndConfirmDuplicateAsync(string venue, DateTime date, string playerId, string playerName)
        {
            if (_db is null || string.IsNullOrEmpty(playerId)) return true; // Allow if no player ID

            try
            {
                var (exists, existingId) = await _db.CheckResultExistsAsync(venue, date, playerId);

                if (exists)
                {
                    // Show confirmation dialog
                    var dialog = new ContentDialog
                    {
                        Title = "Duplicate Result Found",
                        Content = $"Results for player '{playerName}' at '{venue}' on {date:yyyy-MM-dd} already exist.\n\nDo you want to replace the existing result?",
                        PrimaryButtonText = "Yes, Replace",
                        CloseButtonText = "No, Cancel",
                        XamlRoot = this.Content?.XamlRoot
                    };

                    if (this.Content?.XamlRoot == null) return false;

                    var result = await dialog.ShowAsync();
                    return result == ContentDialogResult.Primary;
                }

                return true; // No duplicate, proceed
            }
            catch
            {
                return true; // On error, allow operation
            }
        }

        // Cancel/Exit from Results entry
        private void OnCancelResultsClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ResultsEntryPanel is not null) ResultsEntryPanel.Visibility = Visibility.Collapsed;
                if (ResultsArea is not null) ResultsArea.Visibility = Visibility.Collapsed;
                if (EditorArea is not null) EditorArea.Visibility = Visibility.Collapsed;

                // Clear context
                _currentResultsVenue = null;
                _currentResultsDate = null;
                _currentResultsClub = null;
                _isEditingExistingResults = false;

                // Clear UI
                if (ResultsClubCombo is not null) ResultsClubCombo.SelectedIndex = -1;
                if (ResultsVenueCombo is not null) ResultsVenueCombo.SelectedIndex = -1;
                if (ResultsDatePicker is not null) ResultsDatePicker.Date = DateTimeOffset.MinValue;

                if (PlayerNameCombo is not null) PlayerNameCombo.SelectedIndex = -1;
                if (PartnerCombo is not null) PartnerCombo.SelectedIndex = -1;
                if (HcpTextBox is not null) HcpTextBox.Text = string.Empty;
                if (ResultTextBox is not null) ResultTextBox.Text = string.Empty;
                if (PositionTextBox is not null) PositionTextBox.Text = string.Empty;

                _resultBuffer.Clear();
                _resultIndex = -1;

                UpdateStatus("Returned to main menu.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Error closing results UI: " + ex.Message);
            }
        }

        private Task InvokeImportHandlerAsync()
        {
            OnImportPdfClicked(this, new RoutedEventArgs());
            return Task.CompletedTask;
        }
    }
}