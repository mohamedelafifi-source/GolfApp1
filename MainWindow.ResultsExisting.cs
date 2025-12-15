// MainWindow.ResultsExisting.cs
//================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        // Handler for "Existing Results" menu item
        private async void OnExistingResultsClicked(object sender, RoutedEventArgs e)
        {
            if (_db is null || _vm is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Existing Results", "Database not initialized.");
                return;
            }

            try
            {
                UpdateStatus("Existing Results: Loading venues...");

                // Step 1: Get venues that have results
                var venuesWithResults = await _db.GetVenuesWithResultsAsync();
                if (venuesWithResults == null || venuesWithResults.Count == 0)
                {
                    await ShowErrorAsync("Existing Results", "No existing results found in the database.");
                    return;
                }

                // Step 2: Show venue selection dialog
                var selectedVenue = await ShowExistingVenueSelectionDialogAsync(venuesWithResults);
                if (selectedVenue == null)
                {
                    UpdateStatus("Existing Results cancelled.");
                    return;
                }

                // Step 3: Get the single date for this venue
                var gameDate = await _db.GetDateForVenueAsync(selectedVenue);
                if (gameDate == null)
                {
                    await ShowErrorAsync("Existing Results", $"No date found for venue '{selectedVenue}'.");
                    return;
                }

                // Step 4: Show date confirmation
                var confirmed = await ShowDateConfirmationDialogAsync(selectedVenue, gameDate.Value);
                if (!confirmed)
                {
                    UpdateStatus("Existing Results cancelled.");
                    return;
                }

                // Step 5: Get clubs (ShortNames) that participated
                var clubShortNames = await _db.GetClubsForVenueDateAsync(selectedVenue, gameDate.Value);
                if (clubShortNames == null || clubShortNames.Count == 0)
                {
                    await ShowErrorAsync("Existing Results", $"No clubs found for {selectedVenue} on {gameDate.Value:yyyy-MM-dd}.");
                    return;
                }

                // Step 6: Load all clubs to get LongNames
                var allClubs = await _db.GetAllClubsAsync();
                var clubLookup = allClubs.ToDictionary(c => c.ShortName, c => c.LongName);

                // Build display list with LongNames
                var clubDisplayList = new List<(string ShortName, string DisplayName)>();
                foreach (var shortName in clubShortNames)
                {
                    var longName = clubLookup.ContainsKey(shortName) ? clubLookup[shortName] : shortName;
                    clubDisplayList.Add((shortName, $"{longName} ({shortName})"));
                }

                // Step 7: Show club selection with LongNames
                var selectedClubShortName = await ShowExistingClubSelectionDialogAsync(clubDisplayList);
                if (selectedClubShortName == null)
                {
                    UpdateStatus("Existing Results cancelled.");
                    return;
                }

                // Step 8: Open data entry UI in edit mode
                await OpenDataEntryUIAsync(selectedVenue, gameDate.Value, selectedClubShortName, isEditMode: true);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Existing Results error: {ex.Message}");
                await ShowErrorAsync("Existing Results Error", ex.Message);
            }
        }

        private async Task<string?> ShowExistingVenueSelectionDialogAsync(System.Collections.Generic.List<string> venues)
        {
            var comboBox = new ComboBox
            {
                Width = 400,
                PlaceholderText = "Select a venue",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = venues
            };

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = "Select a venue to view/edit existing results:",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(comboBox);

            var dialog = new ContentDialog
            {
                Title = "Existing Results - Select Venue",
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

        private async Task<bool> ShowDateConfirmationDialogAsync(string venue, DateTime date)
        {
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = $"Game played at:",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            content.Children.Add(new TextBlock
            {
                Text = $"Venue: {venue}",
                Margin = new Thickness(20, 0, 0, 0)
            });
            content.Children.Add(new TextBlock
            {
                Text = $"Date: {date:dddd, MMMM d, yyyy}",
                Margin = new Thickness(20, 0, 0, 0)
            });
            content.Children.Add(new TextBlock
            {
                Text = "\nProceed to select a club?",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });

            var dialog = new ContentDialog
            {
                Title = "Confirm Game Date",
                Content = content,
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot == null) return false;

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private async Task<string?> ShowExistingClubSelectionDialogAsync(List<(string ShortName, string DisplayName)> clubs)
        {
            var comboBox = new ComboBox
            {
                Width = 400,
                PlaceholderText = "Select a club",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Populate with display names but track short names
            foreach (var club in clubs.OrderBy(c => c.DisplayName))
            {
                comboBox.Items.Add(new ComboBoxItem
                {
                    Content = club.DisplayName,
                    Tag = club.ShortName
                });
            }

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = "Select a club to view/edit their results:",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(comboBox);

            var dialog = new ContentDialog
            {
                Title = "Existing Results - Select Club",
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

            // Return the ShortName (Tag) not the DisplayName
            if (result == ContentDialogResult.Primary && comboBox.SelectedItem is ComboBoxItem item)
            {
                return item.Tag as string;
            }
            return null;
        }
    }
}