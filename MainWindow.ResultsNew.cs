// MainWindow.ResultsNew.cs
//===========================

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GolfApp1.Models;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        // Handler for "New Results" menu item
        private async void OnNewResultsClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus(""); // Clear status

            if (_db is null || _vm is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("New Results", "Database not initialized.");
                return;
            }

            try
            {
                UpdateStatus("New Results: Select venue...");

                // Step 1: Select Venue (all clubs as venues)
                var clubs = await _db.GetAllClubsAsync();
                if (clubs == null || clubs.Count == 0)
                {
                    await ShowErrorAsync("New Results", "No clubs/venues found in database.");
                    return;
                }

                var selectedVenue = await ShowVenueSelectionDialogAsync(clubs, "Select Venue for New Results");
                if (selectedVenue == null)
                {
                    UpdateStatus("New Results cancelled.");
                    return;
                }

                // Step 2: Select Date
                var selectedDate = await ShowDatePickerDialogAsync("Select Game Date");
                if (selectedDate == null)
                {
                    UpdateStatus("New Results cancelled.");
                    return;
                }

                // Step 3: Choose Action (Add/Edit or Import PDF)
                var action = await ShowActionDialogAsync();

                if (action == "AddEdit")
                {
                    // Step 4: Select Club
                    var selectedClub = await ShowClubSelectionDialogAsync(clubs, "Select Club");
                    if (selectedClub == null)
                    {
                        UpdateStatus("New Results cancelled.");
                        return;
                    }

                    // Open data entry UI with selected parameters
                    await OpenDataEntryUIAsync(selectedVenue, selectedDate.Value, selectedClub, isEditMode: false);
                }
                else if (action == "ImportPDF")
                {
                    // Open PDF import with venue and date pre-set
                    await OpenPdfImportWithContextAsync(selectedVenue, selectedDate.Value);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"New Results error: {ex.Message}");
                await ShowErrorAsync("New Results Error", ex.Message);
            }
        }

        private async Task<string?> ShowVenueSelectionDialogAsync(System.Collections.Generic.List<Club> clubs, string title)
        {
            var comboBox = new ComboBox
            {
                Width = 400,
                PlaceholderText = "Select a venue",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = clubs.Select(c => c.LongName).OrderBy(n => n).ToList()
            };

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = "Select the venue (club) where the game was played:",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(comboBox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            dialog.IsPrimaryButtonEnabled = false;
            comboBox.SelectionChanged += (s, args) =>
            {
                dialog.IsPrimaryButtonEnabled = comboBox.SelectedIndex >= 0;
            };

            if (this.Content?.XamlRoot == null) return null;

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? comboBox.SelectedItem?.ToString() : null;
        }

        private async Task<DateTime?> ShowDatePickerDialogAsync(string title)
        {
            var datePicker = new CalendarDatePicker
            {
                Date = DateTimeOffset.Now,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = "Select the date when the game was played:",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(datePicker);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot == null) return null;

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? datePicker.Date?.DateTime : null;
        }

        private async Task<string?> ShowActionDialogAsync()
        {
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = "Choose how to enter results:",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            var dialog = new ContentDialog
            {
                Title = "Enter Results Method",
                Content = panel,
                PrimaryButtonText = "Add/Edit",
                SecondaryButtonText = "Import PDF",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot == null) return null;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary) return "AddEdit";
            if (result == ContentDialogResult.Secondary) return "ImportPDF";
            return null;
        }

        private async Task<string?> ShowClubSelectionDialogAsync(System.Collections.Generic.List<Club> clubs, string title)
        {
            var comboBox = new ComboBox
            {
                Width = 400,
                PlaceholderText = "Select a club",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            foreach (var club in clubs.OrderBy(c => c.LongName))
            {
                comboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{club.LongName} ({club.ShortName})",
                    Tag = club.ShortName
                });
            }

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = "Select the club to enter results for:",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(comboBox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = "Open Editor",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            dialog.IsPrimaryButtonEnabled = false;
            comboBox.SelectionChanged += (s, args) =>
            {
                dialog.IsPrimaryButtonEnabled = comboBox.SelectedIndex >= 0;
            };

            if (this.Content?.XamlRoot == null) return null;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                return selectedItem.Tag as string;
            }
            return null;
        }

        private async Task OpenDataEntryUIAsync(string venue, DateTime date, string clubShortName, bool isEditMode)
        {
            try
            {
                // Store context
                _currentResultsVenue = venue;
                _currentResultsDate = date;
                _currentResultsClub = clubShortName;
                _isEditingExistingResults = isEditMode;

                // Load players for the selected club FIRST
                await LoadPlayersForResultsAsync(clubShortName);

                // Load existing results into buffer if editing
                if (isEditMode && _db is not null)
                {
                    var existing = await _db.GetResultsByVenueDateClubAsync(venue, date, clubShortName);
                    _resultBuffer.Clear();

                    if (existing != null && existing.Count > 0)
                    {
                        foreach (var r in existing)
                        {
                            _resultBuffer.Add(r);
                        }
                        _resultIndex = 0;
                        UpdateStatus($"Loaded {existing.Count} existing result(s) for {clubShortName} at {venue}.");
                    }
                    else
                    {
                        // No existing results - start with blank entry
                        _resultBuffer.Add(new ResultRecord
                        {
                            Date = date,
                            Club = clubShortName,
                            Venue = venue,
                            PlayerName = string.Empty,
                            Partner = string.Empty,
                            Hcp = 0,
                            Result = 0,
                            Position = 0
                        });
                        _resultIndex = 0;
                        UpdateStatus("No existing results found - ready to enter new result.");
                    }
                }
                else
                {
                    // New results mode - start with blank entry
                    _resultBuffer.Clear();
                    _resultBuffer.Add(new ResultRecord
                    {
                        Date = date,
                        Club = clubShortName,
                        Venue = venue,
                        PlayerName = string.Empty,
                        Partner = string.Empty,
                        Hcp = 0,
                        Result = 0,
                        Position = 0
                    });
                    _resultIndex = 0;
                    UpdateStatus("Ready to enter new results.");
                }

                // Populate the fields with the current buffer entry
                PopulateResultFields();

                // Update the READONLY display TextBlocks
                if (ResultsDateDisplay is not null)
                    ResultsDateDisplay.Text = date.ToString("dddd, MMMM d, yyyy");
                if (ResultsVenueDisplay is not null)
                    ResultsVenueDisplay.Text = venue;
                if (ResultsClubDisplay is not null)
                {
                    // Try to find the club's long name
                    var club = _clubs.FirstOrDefault(c => c.ShortName == clubShortName);
                    ResultsClubDisplay.Text = club != null ? $"{club.LongName} ({clubShortName})" : clubShortName;
                }

                // Hide legacy controls and show readonly header
                if (ResultsHeaderLegacy is not null) ResultsHeaderLegacy.Visibility = Visibility.Collapsed;
                if (ResultsHeaderButtons is not null) ResultsHeaderButtons.Visibility = Visibility.Collapsed;
                if (ResultsHeaderPanel is not null) ResultsHeaderPanel.Visibility = Visibility.Visible;

                // Show the data entry UI
                if (EditorArea is not null) EditorArea.Visibility = Visibility.Collapsed;
                if (ResultsArea is not null) ResultsArea.Visibility = Visibility.Visible;
                if (ResultsEntryPanel is not null) ResultsEntryPanel.Visibility = Visibility.Visible;

                // Update status
                var mode = isEditMode ? "Editing" : "Entering";
                UpdateStatus($"{mode} results for {venue} on {date:yyyy-MM-dd} - Club: {clubShortName}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error opening data entry: {ex.Message}");
                await ShowErrorAsync("Data Entry Error", ex.Message);
            }
        }

        private async Task OpenPdfImportWithContextAsync(string venue, DateTime date)
        {
            // Store context
            _currentResultsVenue = venue;
            _currentResultsDate = date;

            // Set context before opening PDF import
            if (ResultsDatePicker is not null) ResultsDatePicker.Date = new DateTimeOffset(date);
            if (ResultsVenueCombo is not null)
            {
                ResultsVenueCombo.SelectedItem = venue;
            }

            UpdateStatus($"Import PDF for {venue} on {date:yyyy-MM-dd}");

            // Call existing PDF import
            OnImportPdfClicked(this, new RoutedEventArgs());
        }
    }
}