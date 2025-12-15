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
        // Re-entrancy guard
        private bool _isInitializingResults = false;

        // SAFEGUARD: A timer to delay UI updates until animations finish
        private DispatcherTimer _venueDebounceTimer;

        // Results menu: show import dialog
        private async void OnResultsClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Results menu opened.");

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = "Choose an action for results:",
                TextWrapping = TextWrapping.Wrap
            });

            var root = this.Content?.XamlRoot;
            if (root == null)
            {
                UpdateStatus("Results action: UI unavailable.");
                return;
            }

            var dlg = new ContentDialog
            {
                Title = "Import Results",
                Content = panel,
                PrimaryButtonText = "Import PDF",
                CloseButtonText = "Cancel",
                XamlRoot = root
            };

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
            if (_isInitializingResults) return;

            try
            {
                _isInitializingResults = true;
                UpdateStatus("Enter Results selected.");

                if (EditorArea is not null) EditorArea.Visibility = Visibility.Collapsed;
                if (ResultsArea is not null) ResultsArea.Visibility = Visibility.Visible;
                if (ResultsDatePicker is not null) ResultsDatePicker.Date = DateTimeOffset.Now.Date;

                // Initialize the safeguard timer
                if (_venueDebounceTimer == null)
                {
                    _venueDebounceTimer = new DispatcherTimer();
                    _venueDebounceTimer.Interval = TimeSpan.FromMilliseconds(100); // 100ms delay
                    _venueDebounceTimer.Tick += OnVenueDebounceTick;
                }
                _venueDebounceTimer.Stop();

                // Detach handlers
                if (ResultsClubCombo is not null) ResultsClubCombo.SelectionChanged -= OnResultsClubChanged;
                if (ResultsVenueCombo is not null) ResultsVenueCombo.SelectionChanged -= OnResultsVenueChanged;

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
                    if (ResultsClubCombo is not null) ResultsClubCombo.SelectionChanged += OnResultsClubChanged;
                    if (ResultsVenueCombo is not null) ResultsVenueCombo.SelectionChanged += OnResultsVenueChanged;
                }

                if (ResultsEntryPanel is not null) ResultsEntryPanel.Visibility = Visibility.Collapsed;

                UpdateProceedButtonState();
            }
            finally
            {
                _isInitializingResults = false;
            }
        }

        // Called when the club combo selection changes
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
            _ = LoadPlayersForResultsAsync(selected);
        }

        private void OnResultsDateChanged(object? sender, DatePickerValueChangedEventArgs e)
        {
            if (_isInitializingResults) return;
            UpdateProceedButtonState();
        }

        // ============================================================================
        // FIX: DELAYED UPDATE WITH SAFETY CHECK
        // ============================================================================
        private void OnResultsVenueChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingResults) return;

            // 1. DO NOT update buttons here. It causes the Single-Click hang.

            // 2. Start the timer. This pushes the logic to the next UI frame.
            if (_venueDebounceTimer != null)
            {
                _venueDebounceTimer.Stop();
                _venueDebounceTimer.Start();
            }

            if (sender is ComboBox cb && cb.SelectedItem != null)
            {
                // Just log, don't touch layout
                // UpdateStatus($"Venue selected: '{cb.SelectedItem}'"); 
            }
        }

        // This runs 100ms AFTER the click
        private void OnVenueDebounceTick(object? sender, object e)
        {
            if (_venueDebounceTimer != null) _venueDebounceTimer.Stop();

            // CRITICAL CHECK:
            // If the DropDown is OPEN, it means the user Double-Clicked (re-opening it).
            // We MUST NOT update the buttons if it is Open, or we crash.
            if (ResultsVenueCombo != null && ResultsVenueCombo.IsDropDownOpen)
            {
                UpdateStatus("Skipping button update (Dropdown is open).");
                return;
            }

            // If Dropdown is Closed (Single Click), it is safe to update.
            UpdateProceedButtonState();

            if (ResultsVenueCombo != null && ResultsVenueCombo.SelectedItem != null)
            {
                UpdateStatus($"Venue confirmed: '{ResultsVenueCombo.SelectedItem}'");
            }
        }

        // Helper: enable Proceed and Import PDF
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

        private Task InvokeImportHandlerAsync()
        {
            OnImportPdfClicked(this, new RoutedEventArgs());
            return Task.CompletedTask;
        }

        // Cancel/Exit from Results entry
        private void OnCancelResultsClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                // Stop timer if pending
                if (_venueDebounceTimer != null) _venueDebounceTimer.Stop();

                if (ResultsEntryPanel is not null) ResultsEntryPanel.Visibility = Visibility.Collapsed;
                if (ResultsArea is not null) ResultsArea.Visibility = Visibility.Collapsed;
                if (EditorArea is not null) EditorArea.Visibility = Visibility.Collapsed;

                if (ResultsClubCombo is not null) ResultsClubCombo.SelectedIndex = -1;

                if (ResultsVenueCombo is not null)
                {
                    ResultsVenueCombo.SelectedIndex = -1;
                    ResultsVenueCombo.IsDropDownOpen = false;
                }

                if (ResultsDatePicker is not null) ResultsDatePicker.Date = DateTimeOffset.MinValue;

                if (PlayerNameCombo is not null) PlayerNameCombo.SelectedIndex = -1;
                if (PartnerCombo is not null) PartnerCombo.SelectedIndex = -1;
                if (HcpTextBox is not null) HcpTextBox.Text = string.Empty;
                if (ResultTextBox is not null) ResultTextBox.Text = string.Empty;
                if (PositionTextBox is not null) PositionTextBox.Text = string.Empty;

                _resultBuffer.Clear();
                _resultIndex = -1;

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