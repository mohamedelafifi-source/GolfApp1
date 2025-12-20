//MainWindow.Reports.cs
//============================
using GolfApp1.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        private async void OnReportByClubClicked(object sender, RoutedEventArgs e)
        {
            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Report Error", "Database not initialized.");
                return;
            }

            try
            {
                UpdateStatus("Loading venues for report...");

                // Step 1: Get venues that have results
                var venuesWithResults = await _db.GetVenuesWithResultsAsync();
                if (venuesWithResults == null || venuesWithResults.Count == 0)
                {
                    await ShowErrorAsync("Report - By Club", "No venues with results found in the database.");
                    return;
                }

                // Step 2: Show venue selection dialog
                var selectedVenue = await ShowVenueSelectionForReportAsync(venuesWithResults);
                if (selectedVenue == null)
                {
                    UpdateStatus("Report cancelled.");
                    return;
                }

                // Step 3: Get the date for this venue
                var gameDate = await _db.GetDateForVenueAsync(selectedVenue);
                if (gameDate == null)
                {
                    await ShowErrorAsync("Report - By Club", $"No date found for venue '{selectedVenue}'.");
                    return;
                }

                // Step 4: Show date confirmation
                var confirmed = await ShowDateConfirmationForReportAsync(selectedVenue, gameDate.Value);
                if (!confirmed)
                {
                    UpdateStatus("Report cancelled.");
                    return;
                }

                // Step 5: Get clubs that participated at this venue/date
                var clubShortNames = await _db.GetClubsForVenueDateAsync(selectedVenue, gameDate.Value);
                if (clubShortNames == null || clubShortNames.Count == 0)
                {
                    await ShowErrorAsync("Report - By Club", $"No clubs found for {selectedVenue} on {gameDate.Value:yyyy-MM-dd}.");
                    return;
                }

                // Step 6: Load all clubs to get LongNames
                var allClubs = await _db.GetAllClubsAsync();
                var clubLookup = allClubs.ToDictionary(c => c.ShortName, c => c);

                // Build display list with LongNames
                var clubDisplayList = new List<(string ShortName, string DisplayName)>();
                foreach (var shortName in clubShortNames)
                {
                    var longName = clubLookup.ContainsKey(shortName) ? clubLookup[shortName].LongName : shortName;
                    clubDisplayList.Add((shortName, $"{longName} ({shortName})"));
                }

                // Step 7: Show club selection
                var selectedClubShortName = await ShowClubSelectionForReportAsync(clubDisplayList);
                if (selectedClubShortName == null)
                {
                    UpdateStatus("Report cancelled.");
                    return;
                }

                // Step 8: Get club details
                var selectedClub = clubLookup.ContainsKey(selectedClubShortName) ? clubLookup[selectedClubShortName] : null;
                if (selectedClub == null)
                {
                    await ShowErrorAsync("Report - By Club", $"Club '{selectedClubShortName}' not found.");
                    return;
                }

                UpdateStatus($"Generating report for club: {selectedClub.LongName}...");

                // Get results filtered by venue, date, and club
                var results = await _db.GetResultsByVenueDateClubAsync(selectedVenue, gameDate.Value, selectedClub.ShortName);

                if (results == null || results.Count == 0)
                {
                    UpdateStatus($"No results found for {selectedClub.LongName} at {selectedVenue} on {gameDate.Value:yyyy-MM-dd}.");
                    await ShowErrorAsync("Report - By Club", $"No results found for club '{selectedClub.LongName}' at {selectedVenue} on {gameDate.Value:yyyy-MM-dd}.");
                    return;
                }

                // Sort results: by Position (ascending), then by Result (descending), then by Handicap (ascending)
                var sortedResults = results
                    .OrderBy(r => r.Position)
                    .ThenByDescending(r => r.Result)
                    .ThenBy(r => r.Hcp)
                    .ToList();

                // Generate CSV content
                var csvContent = GenerateClubReportCsv(selectedClub, sortedResults);

                // Show file save picker
                var savedFile = await SaveCsvFileAsync($"{selectedClub.ShortName}_{selectedVenue.Replace(" ", "_")}_{gameDate.Value:yyyyMMdd}_Report", csvContent);
                if (savedFile != null)
                {
                    UpdateStatus($"Report saved: {savedFile.Name}");
                    await ShowErrorAsync("Report Saved", $"Club report saved successfully to:\n{savedFile.Path}");
                }
                else
                {
                    UpdateStatus("Report save cancelled.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Report generation failed: {ex.Message}");
                await ShowErrorAsync("Report Error", $"Failed to generate report:\n{ex.Message}");
            }
        }

        private async void OnReportByPlayerClicked(object sender, RoutedEventArgs e)
        {
            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Report Error", "Database not initialized.");
                return;
            }

            try
            {
                UpdateStatus("Generating report for all players...");

                // Get all clubs
                var clubs = await _db.GetAllClubsAsync();
                if (clubs == null || clubs.Count == 0)
                {
                    UpdateStatus("No clubs found in database.");
                    await ShowErrorAsync("Report - By Player", "No clubs found in the database.");
                    return;
                }

                // Gather all results from all clubs (all venues, all dates)
                var allResults = new List<ResultRecord>();
                foreach (var club in clubs)
                {
                    var clubResults = await _db.GetResultsAsync(club.ShortName);
                    if (clubResults != null)
                    {
                        allResults.AddRange(clubResults);
                    }
                }

                if (allResults.Count == 0)
                {
                    UpdateStatus("No results found in database.");
                    await ShowErrorAsync("Report - By Player", "No results found in the database.");
                    return;
                }

                // FILTER: Remove invalid entries (bad dates, missing venues) and deduplicate EXACT duplicates
                var validResults = allResults
                    .Where(r => !string.IsNullOrWhiteSpace(r.Venue))  // Must have venue
                    .Where(r => r.Date.Year >= 2020)                  // Must have valid date (not DateTime.MinValue = 1601)
                    .Where(r => !string.IsNullOrWhiteSpace(r.PlayerName))  // Must have player name
                    .GroupBy(r => new { r.PlayerId, r.Date, r.Venue, r.Club })  // Group by unique combination
                    .Select(g => g.First())  // Take first from each duplicate group
                    .ToList();

                if (validResults.Count == 0)
                {
                    UpdateStatus("No valid results found in database (after filtering bad data).");
                    await ShowErrorAsync("Report - By Player", "No valid results found in the database.");
                    return;
                }

                // Report how many invalid entries were filtered out
                var filteredCount = allResults.Count - validResults.Count;

                // OPTION 1: Show ONLY BEST result per player (one entry per player)
                // Uncomment this block if you want only ONE result per player:
                /*
                var bestResultsPerPlayer = validResults
                    .GroupBy(r => new { r.PlayerId, r.PlayerName, r.Club })
                    .Select(g => g.OrderByDescending(r => r.Result).First())  // Take best result
                    .OrderByDescending(r => r.Result)
                    .ThenBy(r => r.PlayerName ?? string.Empty)
                    .ToList();

                var sortedResults = bestResultsPerPlayer;
                */

                // OPTION 2: Show ALL results but sorted by best score first (current behavior)
                // This is the default - shows multiple entries per player if they played multiple games
                var sortedResults = validResults
                    .OrderByDescending(r => r.Result)  // Best scores first (highest points)
                    .ThenBy(r => r.PlayerName ?? string.Empty)
                    .ThenBy(r => r.Venue ?? string.Empty)
                    .ThenBy(r => r.Date)
                    .ThenBy(r => r.Hcp)
                    .ToList();

                if (filteredCount > 0)
                {
                    UpdateStatus($"Filtered out {filteredCount} invalid/duplicate entries from report.");
                }

                // Generate CSV content
                var csvContent = GenerateAllPlayersReportCsv(sortedResults);

                // Show file save picker
                var savedFile = await SaveCsvFileAsync("All_Players_Report", csvContent);
                if (savedFile != null)
                {
                    UpdateStatus($"Report saved: {savedFile.Name}");
                    await ShowErrorAsync("Report Saved", $"All players report saved successfully to:\n{savedFile.Path}\n\nTotal results: {sortedResults.Count}\nFiltered out {filteredCount} invalid/duplicate entries.");
                }
                else
                {
                    UpdateStatus("Report save cancelled.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Report generation failed: {ex.Message}");
                await ShowErrorAsync("Report Error", $"Failed to generate report:\n{ex.Message}");
            }
        }


        private async Task<string?> ShowVenueSelectionForReportAsync(List<string> venues)
        {
            var comboBox = new ComboBox
            {
                Width = 400,
                PlaceholderText = "Select a venue",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = venues.OrderBy(v => v).ToList()
            };

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = "Select the venue for the report:",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(comboBox);

            var dialog = new ContentDialog
            {
                Title = "Report - Select Venue",
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

        private async Task<bool> ShowDateConfirmationForReportAsync(string venue, DateTime date)
        {
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = $"Generate report for:",
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

            var dialog = new ContentDialog
            {
                Title = "Confirm Report Date",
                Content = content,
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot == null) return false;

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private async Task<string?> ShowClubSelectionForReportAsync(List<(string ShortName, string DisplayName)> clubs)
        {
            var comboBox = new ComboBox
            {
                Width = 400,
                PlaceholderText = "Select a club",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

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
                Text = "Select the club for the report:",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(comboBox);

            var dialog = new ContentDialog
            {
                Title = "Report - Select Club",
                Content = content,
                PrimaryButtonText = "Generate Report",
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

            if (result == ContentDialogResult.Primary && comboBox.SelectedItem is ComboBoxItem item)
            {
                return item.Tag as string;
            }
            return null;
        }

        private string GenerateClubReportCsv(Club club, List<ResultRecord> results)
        {
            var csv = new StringBuilder();

            // Header with club information
            csv.AppendLine($"Club Report: {club.LongName} ({club.ShortName})");
            csv.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            csv.AppendLine($"Total Results: {results.Count}");
            csv.AppendLine();

            // CSV column headers
            csv.AppendLine("Venue,Date,Player Name,Partner,Handicap,Result,Position");

            // Data rows
            foreach (var result in results)
            {
                var venue = CsvEscape(result.Venue ?? string.Empty);
                var date = result.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var playerName = CsvEscape(result.PlayerName ?? string.Empty);
                var partner = CsvEscape(result.Partner ?? string.Empty);
                var handicap = result.Hcp.ToString();
                var score = result.Result.ToString();
                var position = result.Position.ToString();

                csv.AppendLine($"{venue},{date},{playerName},{partner},{handicap},{score},{position}");
            }

            return csv.ToString();
        }

        private string GenerateAllPlayersReportCsv(List<ResultRecord> results)
        {
            var csv = new StringBuilder();

            // Header with report information
            csv.AppendLine("All Players Report - Sorted by Best Score");
            csv.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            csv.AppendLine($"Total Results: {results.Count}");
            csv.AppendLine();

            // CSV column headers
            csv.AppendLine("Player Name,Club,Venue,Date,Partner,Handicap,Result,Position");

            // Data rows
            foreach (var result in results)
            {
                var playerName = CsvEscape(result.PlayerName ?? string.Empty);
                var club = CsvEscape(result.Club ?? string.Empty);
                var venue = CsvEscape(result.Venue ?? string.Empty);
                var date = result.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var partner = CsvEscape(result.Partner ?? string.Empty);
                var handicap = result.Hcp.ToString();
                var score = result.Result.ToString();
                var position = result.Position.ToString();

                csv.AppendLine($"{playerName},{club},{venue},{date},{partner},{handicap},{score},{position}");
            }

            return csv.ToString();
        }

        private string GenerateAveragesReportCsv(List<dynamic> playerStats)
        {
            var csv = new StringBuilder();

            // Header with report information
            csv.AppendLine("Player Averages Report - Sorted by Average Points");
            csv.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            csv.AppendLine($"Total Players: {playerStats.Count}");
            csv.AppendLine();

            // CSV column headers
            csv.AppendLine("Player Name,Club,Total Points,Games Played,Average Points");

            // Data rows
            foreach (var stat in playerStats)
            {
                var playerName = CsvEscape(stat.PlayerName ?? string.Empty);
                var club = CsvEscape(stat.ClubShort ?? string.Empty);
                var totalPoints = stat.TotalPoints.ToString();
                var gamesPlayed = stat.GamesPlayed.ToString();
                var averagePoints = stat.AveragePoints.ToString("F2", CultureInfo.InvariantCulture);

                csv.AppendLine($"{playerName},{club},{totalPoints},{gamesPlayed},{averagePoints}");
            }

            return csv.ToString();
        }

        //=====
        private async void OnReportByAveragesClicked(object sender, RoutedEventArgs e)
        {
            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Report Error", "Database not initialized.");
                return;
            }

            try
            {
                UpdateStatus("Generating averages report...");

                // Get all clubs
                var clubs = await _db.GetAllClubsAsync();
                if (clubs == null || clubs.Count == 0)
                {
                    UpdateStatus("No clubs found in database.");
                    await ShowErrorAsync("Report - By Averages", "No clubs found in the database.");
                    return;
                }

                // Gather all results from all clubs (all venues, all dates)
                var allResults = new List<ResultRecord>();
                foreach (var club in clubs)
                {
                    var clubResults = await _db.GetResultsAsync(club.ShortName);
                    if (clubResults != null)
                    {
                        allResults.AddRange(clubResults);
                    }
                }

                if (allResults.Count == 0)
                {
                    UpdateStatus("No results found in database.");
                    await ShowErrorAsync("Report - By Averages", "No results found in the database.");
                    return;
                }

                // FILTER: Remove invalid entries (bad dates, missing venues) and deduplicate
                var validResults = allResults
                    .Where(r => !string.IsNullOrWhiteSpace(r.Venue))
                    .Where(r => r.Date.Year >= 2020)
                    .Where(r => !string.IsNullOrWhiteSpace(r.PlayerName))
                    .GroupBy(r => new { r.PlayerId, r.Date, r.Venue, r.Club })
                    .Select(g => g.First())
                    .ToList();

                if (validResults.Count == 0)
                {
                    UpdateStatus("No valid results found in database.");
                    await ShowErrorAsync("Report - By Averages", "No valid results found in the database.");
                    return;
                }

                // Group by player and calculate statistics
                var playerStats = validResults
                    .GroupBy(r => new { r.PlayerId, r.PlayerName, r.Club })
                    .Select(g => new
                    {
                        PlayerName = g.Key.PlayerName ?? "Unknown",
                        ClubShort = g.Key.Club ?? "Unknown",
                        TotalPoints = g.Sum(r => r.Result),
                        GamesPlayed = g.Count(),
                        AveragePoints = g.Average(r => r.Result)
                    })
                    .OrderByDescending(p => p.AveragePoints)
                    .ThenByDescending(p => p.TotalPoints)
                    .ThenBy(p => p.PlayerName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (playerStats.Count == 0)
                {
                    UpdateStatus("No player statistics available.");
                    await ShowErrorAsync("Report - By Averages", "No player statistics could be calculated.");
                    return;
                }

                // Generate CSV content
                var csv = new StringBuilder();
                csv.AppendLine("Player Averages Report - Sorted by Average Points");
                csv.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                csv.AppendLine($"Total Players: {playerStats.Count}");
                csv.AppendLine();
                csv.AppendLine("Player Name,Club,Total Points,Games Played,Average Points");

                foreach (var stat in playerStats)
                {
                    var playerName = CsvEscape(stat.PlayerName ?? string.Empty);
                    var club = CsvEscape(stat.ClubShort ?? string.Empty);
                    var totalPoints = stat.TotalPoints.ToString();
                    var gamesPlayed = stat.GamesPlayed.ToString();
                    var averagePoints = stat.AveragePoints.ToString("F2", CultureInfo.InvariantCulture);

                    csv.AppendLine($"{playerName},{club},{totalPoints},{gamesPlayed},{averagePoints}");
                }

                var csvContent = csv.ToString();

                // Show file save picker
                var savedFile = await SaveCsvFileAsync("Player_Averages_Report", csvContent);
                if (savedFile != null)
                {
                    UpdateStatus($"Report saved: {savedFile.Name}");
                    await ShowErrorAsync("Report Saved", $"Player averages report saved successfully to:\n{savedFile.Path}\n\nTotal players: {playerStats.Count}");
                }
                else
                {
                    UpdateStatus("Report save cancelled.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Report generation failed: {ex.Message}");
                await ShowErrorAsync("Report Error", $"Failed to generate averages report:\n{ex.Message}");
            }
        }
        private async Task<StorageFile?> SaveCsvFileAsync(string suggestedFileName, string content)
        {
            try
            {
                var savePicker = new FileSavePicker();
                InitializeWithWindow.Initialize(savePicker, WindowNative.GetWindowHandle(this));
                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("CSV File", new List<string> { ".csv" });
                savePicker.SuggestedFileName = $"{suggestedFileName}_{DateTime.Now:yyyyMMdd_HHmmss}";

                var file = await savePicker.PickSaveFileAsync().AsTask();
                if (file != null)
                {
                    await FileIO.WriteTextAsync(file, content);
                    return file;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Save failed: {ex.Message}");
                await ShowErrorAsync("Save Error", $"Failed to save file:\n{ex.Message}");
            }

            return null;
        }

        // Note: CsvEscape method is already defined in MainWindow.PdfImport.cs
        // No need to duplicate it here since this is a partial class
    }
}