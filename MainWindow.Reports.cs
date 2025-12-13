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
                UpdateStatus("Loading clubs for report...");

                // Get all clubs
                var clubs = await _db.GetAllClubsAsync();
                if (clubs == null || clubs.Count == 0)
                {
                    UpdateStatus("No clubs found in database.");
                    await ShowErrorAsync("Report - By Club", "No clubs found in the database.");
                    return;
                }

                // Show club selection dialog
                var selectedClub = await ShowClubSelectionDialogAsync(clubs);
                if (selectedClub == null)
                {
                    UpdateStatus("Report cancelled.");
                    return;
                }

                UpdateStatus($"Generating report for club: {selectedClub.LongName}...");

                // Get all results for this club
                var results = await _db.GetResultsAsync(selectedClub.ShortName);
                if (results == null || results.Count == 0)
                {
                    UpdateStatus($"No results found for {selectedClub.LongName}.");
                    await ShowErrorAsync("Report - By Club", $"No results found for club '{selectedClub.LongName}'.");
                    return;
                }

                // Sort results: by Venue, then Date, then Result (descending - best score first), then Handicap (ascending - lower handicap first)
                var sortedResults = results
                    .OrderBy(r => r.Venue ?? string.Empty)
                    .ThenBy(r => r.Date)
                    .ThenByDescending(r => r.Result)
                    .ThenBy(r => r.Hcp)
                    .ToList();

                // Generate CSV content
                var csvContent = GenerateClubReportCsv(selectedClub, sortedResults);

                // Show file save picker
                var savedFile = await SaveCsvFileAsync($"{selectedClub.ShortName}_Report", csvContent);
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

                // Gather all results from all clubs
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

                // Sort results: by Result (descending - best score first), then Handicap (ascending), then Player Name
                var sortedResults = allResults
                    .OrderByDescending(r => r.Result)
                    .ThenBy(r => r.Hcp)
                    .ThenBy(r => r.PlayerName ?? string.Empty)
                    .ToList();

                // Generate CSV content
                var csvContent = GenerateAllPlayersReportCsv(sortedResults);

                // Show file save picker
                var savedFile = await SaveCsvFileAsync("All_Players_Report", csvContent);
                if (savedFile != null)
                {
                    UpdateStatus($"Report saved: {savedFile.Name}");
                    await ShowErrorAsync("Report Saved", $"All players report saved successfully to:\n{savedFile.Path}");
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

        private async Task<Club?> ShowClubSelectionDialogAsync(List<Club> clubs)
        {
            // Create a dialog with a ComboBox to select club
            var comboBox = new ComboBox
            {
                Width = 400,
                PlaceholderText = "Select a club",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Add clubs to ComboBox (display LongName, but store Club object)
            foreach (var club in clubs.OrderBy(c => c.LongName))
            {
                comboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{club.LongName} ({club.ShortName})",
                    Tag = club
                });
            }

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = "Select a club to generate the report:",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(comboBox);

            var dialog = new ContentDialog
            {
                Title = "Report - By Club",
                Content = content,
                PrimaryButtonText = "Generate",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            // Disable primary button until a club is selected
            dialog.IsPrimaryButtonEnabled = false;
            comboBox.SelectionChanged += (s, args) =>
            {
                dialog.IsPrimaryButtonEnabled = comboBox.SelectedIndex >= 0;
            };

            if (this.Content?.XamlRoot == null)
            {
                return null;
            }

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                return selectedItem.Tag as Club;
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