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
        // Re-entrancy guard flags
        private bool _isInitializingResults = false;
        private bool _isUpdatingVenue = false;
        private bool _isUpdatingClub = false;

        // Track last known selection to detect changes
        private object? _lastKnownVenueSelection = null;

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
            // Guard against re-entrancy
            if (_isInitializingResults) return;

            try
            {
                _isInitializingResults = true;

                UpdateStatus("Enter Results selected.");

                // Hide club editor and show results area
                if (EditorArea is not null) EditorArea.Visibility = Visibility.Collapsed;
                if (ResultsArea is not null) ResultsArea.Visibility = Visibility.Visible;

                // Initialize header
                if (ResultsDatePicker is not null) ResultsDatePicker.Date = DateTimeOffset.Now.Date;

                // Temporarily detach event handlers to prevent premature firing during initialization
                if (ResultsClubCombo is not null)
                {
                    ResultsClubCombo.SelectionChanged -= OnResultsClubChanged;
                    ResultsClubCombo.DropDownClosed -= OnResultsClubDropDownClosed;
                }
                if (ResultsVenueCombo is not null)
                {
                    ResultsVenueCombo.SelectionChanged -= OnResultsVenueChanged;
                    ResultsVenueCombo.DropDownClosed -= OnResultsVenueDropDownClosed;
                }

                try
                {
                    // Refresh local clubs and populate club/venue lists
                    RefreshLocalClubsFromVm();
                    if (ResultsClubCombo is not null)
                    {
                        ResultsClubCombo.SelectedIndex = -1; // Clear selection first
                        ResultsClubCombo.SelectedItem = null; // Ensure completely cleared
                        ResultsClubCombo.ItemsSource = _clubs.Select(c => c.ShortName).ToList();
                    }
                    if (ResultsVenueCombo is not null)
                    {
                        ResultsVenueCombo.SelectedIndex = -1; // Clear selection first
                        ResultsVenueCombo.SelectedItem = null; // Ensure completely cleared
                        ResultsVenueCombo.ItemsSource = _clubs.Select(c => c.LongName).ToList();
                        _lastKnownVenueSelection = null; // Reset tracking
                    }
                }
                finally
                {
                    // Re-attach event handlers
                    if (ResultsClubCombo is not null)
                    {
                        ResultsClubCombo.SelectionChanged += OnResultsClubChanged;
                        ResultsClubCombo.DropDownClosed += OnResultsClubDropDownClosed;
                    }
                    if (ResultsVenueCombo is not null)
                    {
                        ResultsVenueCombo.SelectionChanged += OnResultsVenueChanged;
                        ResultsVenueCombo.DropDownClosed += OnResultsVenueDropDownClosed;
                    }
                }

                // hide entry panel until Proceed
                if (ResultsEntryPanel is not null) ResultsEntryPanel.Visibility = Visibility.Collapsed;

                UpdateProceedButtonState();
            }
            finally
            {
                _isInitializingResults = false;
            }
        }

        // Called when the club combo selection changes in the header.
        private async void OnResultsClubChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Guard against re-entrancy during initialization
            if (_isInitializingResults || _isUpdatingClub) return;

            try
            {
                _isUpdatingClub = true;

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
                await LoadPlayersForResultsAsync(selected);
                UpdateStatus($"Loaded {_vm?.Players.Count ?? 0} players for '{selected}'.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Failed to load players: " + ex.Message);
                if (PlayerNameCombo is not null) PlayerNameCombo.ItemsSource = null;
                if (PartnerCombo is not null) PartnerCombo.ItemsSource = null;
            }
            finally
            {
                _isUpdatingClub = false;
            }
        }

        // New handler for club DropDownClosed - ensures selection is processed
        private void OnResultsClubDropDownClosed(object? sender, object e)
        {
            if (_isInitializingResults) return;
            UpdateProceedButtonState();
        }

        // Called when the date picker value changes.
        private void OnResultsDateChanged(object? sender, DatePickerValueChangedEventArgs e)
        {
            // Guard against re-entrancy during initialization
            if (_isInitializingResults) return;

            UpdateProceedButtonState();
        }

        // Called when venue selection changes.
        private void OnResultsVenueChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Guard against re-entrancy during initialization or updates
            if (_isInitializingResults || _isUpdatingVenue) return;

            try
            {
                _isUpdatingVenue = true;

                // Update tracking
                if (sender is ComboBox cb)
                {
                    _lastKnownVenueSelection = cb.SelectedItem;
                }

                UpdateProceedButtonState();
            }
            finally
            {
                _isUpdatingVenue = false;
            }
        }

        // New handler for venue DropDownClosed - ensures selection is processed even for index 0
        // NOTE: Does NOT check or set _isUpdatingVenue to avoid any deadlock scenarios
        private void OnResultsVenueDropDownClosed(object? sender, object e)
        {
            // ONLY skip if initializing - do NOT check _isUpdatingVenue
            if (_isInitializingResults) return;

            // Process the selection without any locking
            if (sender is ComboBox cb && cb.SelectedItem != null)
            {
                // Only update if selection actually changed
                if (_lastKnownVenueSelection != cb.SelectedItem)
                {
                    _lastKnownVenueSelection = cb.SelectedItem;
                    UpdateStatus($"Venue selected: '{cb.SelectedItem}'");
                }

                UpdateProceedButtonState();
            }
        }

        // Helper: enable Proceed and Import PDF only when date, club and venue are set.
        private void UpdateProceedButtonState()
        {
            try
            {
                var dateOk = ResultsDatePicker is not null && ResultsDatePicker.Date != DateTimeOffset.MinValue;
                var clubOk = ResultsClubCombo is not null && ResultsClubCombo.SelectedItem != null;
                var venueOk = ResultsVenueCombo is not null && ResultsVenueCombo.SelectedItem != null;

                var enabled = dateOk && clubOk && venueOk;

                if (ProceedResultsButton is not null) ProceedResultsButton.IsEnabled = enabled;
                if (ImportPdfButton is not null) ImportPdfButton.IsEnabled = enabled;
            }
            catch
            {
                if (ProceedResultsButton is not null) ProceedResultsButton.IsEnabled = false;
                if (ImportPdfButton is not null) ImportPdfButton.IsEnabled = false;
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

                // Reset venue tracking
                _lastKnownVenueSelection = null;

                // Disable proceed until header is filled next time
                if (ProceedResultsButton is not null) ProceedResultsButton.IsEnabled = false;
                if (ImportPdfButton is not null) ImportPdfButton.IsEnabled = false;

                UpdateStatus("Returned to main menu.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Error closing results UI: " + ex.Message);
            }
        }

    }

}