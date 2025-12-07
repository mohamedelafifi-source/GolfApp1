

//MainWindow.Helpers.cs
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using GolfApp1.Models;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        // Shared modal error helper (used across partial files)
        private async Task ShowErrorAsync(string title, string message)
        {
            UpdateStatus(message);
            var dlg = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot != null)
            {
                await dlg.ShowAsync();
            }
        }

        // Reusable parser used by File/Open and Bulk Add flows.
        // Parses semicolon-separated player lines and either auto-adds or loads for review.
        private async Task ParseAndBulkAddAsync(string text, string clubShort)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                UpdateStatus("File is empty.");
                await ShowErrorAsync("Empty file", "The selected file contains no text.");
                return;
            }

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var parsed = new List<Player>(lines.Length);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var tokens = line.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var code = string.Empty;
                var name = string.Empty;
                var idx = string.Empty;
                var note = string.Empty;

                foreach (var t in tokens)
                {
                    var parts = t.Split(new[] { ':' }, 2);
                    if (parts.Length < 2) continue;
                    var label = parts[0].Trim().TrimEnd(':').ToLowerInvariant();
                    var value = parts[1].Trim();

                    if (label.Contains("code")) code = value;
                    else if (label.Contains("name")) name = value;
                    else if (label.Contains("index")) idx = value;
                    else if (label.Contains("note")) note = value;
                }

                code = Regex.Replace(code, @"\s+", string.Empty);

                var player = new Player
                {
                    Id = Guid.NewGuid().ToString(),
                    ClubShortName = clubShort,
                    Code = code,
                    Name = name,
                    IndexValue = idx,
                    Note = note
                };

                parsed.Add(player);
            }

            if (parsed.Count == 0)
            {
                UpdateStatus("No valid records found in file.");
                await ShowErrorAsync("No records", "No valid player records were found in the selected file.");
                return;
            }

            var dlg = new ContentDialog
            {
                Title = "Import Options",
                Content = $"Parsed {parsed.Count} records. Do you want to add them to the database now, or load them for review?",
                PrimaryButtonText = "Auto Add",
                SecondaryButtonText = "Review",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            var result = ContentDialogResult.None;
            if (this.Content?.XamlRoot != null) result = await dlg.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                await BulkAddPlayersAsync(parsed);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                LoadPlayersForReview(parsed, clubShort);
            }
            else
            {
                UpdateStatus("Import cancelled.");
            }
        }

        // Bulk add implementation (keeps behaviour consistent with other partials).
        private async Task BulkAddPlayersAsync(List<Player> players)
        {
            if (_vm is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Error", "Database not initialized.");
                return;
            }

            var added = 0;
            var failed = 0;
            var errors = new List<string>();

            foreach (var p in players)
            {
                // minimal validation
                if (string.IsNullOrEmpty(p.Code) || p.Code.Length != 6 || !int.TryParse(p.Code, out _))
                {
                    failed++;
                    errors.Add($"Code '{p.Code}' invalid (expected 6 digits).");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(p.Name))
                {
                    failed++;
                    errors.Add($"Code '{p.Code}': missing name.");
                    continue;
                }

                try
                {
                    var err = await _vm.UpsertPlayerAsync(p);
                    if (err != null)
                    {
                        failed++;
                        errors.Add($"Code '{p.Code}': {MapDbErrorToUserMessage(err, p.Code)}");
                    }
                    else
                    {
                        added++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Code '{p.Code}': unexpected error: {ex.Message}");
                }
            }

            var summary = $"Bulk import finished. Added: {added}. Failed: {failed}.";
            UpdateStatus(summary);

            var details = errors.Count == 0 ? string.Empty : string.Join("\n", errors.Count > 50 ? errors.GetRange(0, 50) : errors);
            if (errors.Count > 50) details += $"\n... ({errors.Count - 50} more lines)";

            await ShowErrorAsync(failed == 0 ? "Import Complete" : "Import Completed with errors",
                                 summary + (string.IsNullOrEmpty(details) ? string.Empty : $"\n\n{details}"));

            // If player editor was open for this club, reload it
            if (_inPlayerMode)
            {
                var clubShort = ShortNameTextBox.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(clubShort)) await EnterPlayerModeAsync(clubShort);
            }
        }

        // Load parsed players for review in the player editor
        private void LoadPlayersForReview(List<Player> players, string clubShort)
        {
            _players.Clear();
            foreach (var p in players) _players.Add(p);

            _playerIndex = 0;
            _inPlayerMode = true;

            // Keep the club editor visible but read-only
            ShortNameTextBox.IsEnabled = false;
            LongNameTextBox.IsEnabled = false;

            // Disable club navigation and actions while reviewing
            PrevButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            AddPlayerButton.IsEnabled = false;

            // Show player editor panel and load first record into fields for review
            PlayerEditorPanel.Visibility = Visibility.Visible;
            ShowPlayer();
            UpdatePlayerNavigationButtons();

            UpdateStatus($"Loaded {players.Count} records for review. Use Next/Prev and press Add/Update to save.");
        }
    }
}