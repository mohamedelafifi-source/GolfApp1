//MainWindow.CreateTeam.cs
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
        // Helper class to hold player selection state
        private class SelectablePlayer
        {
            public Player Player { get; set; } = null!;
            public bool IsSelected { get; set; }
            public string Division { get; set; } = string.Empty;
            public double Handicap { get; set; }
        }

        private async void OnCreateTeamClicked(object sender, RoutedEventArgs e)
        {
            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Create Team", "Database not initialized.");
                return;
            }

            try
            {
                // Step 1: Show team info dialog (Club + Venue + Date)
                await ShowTeamInfoDialogAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Team selection failed: {ex.Message}");
                await ShowErrorAsync("Error", $"An error occurred:\n{ex.Message}");
            }
        }

        // ============================================================================
        // STEP 1: TEAM INFO DIALOG (Club + Venue + Date)
        // ============================================================================

        private async Task ShowTeamInfoDialogAsync()
        {
            // Get all clubs
            var clubs = await _db!.GetAllClubsAsync();
            if (clubs == null || clubs.Count == 0)
            {
                await ShowErrorAsync("Create Team", "No clubs found in the database.");
                return;
            }

            // Create UI elements
            var clubCombo = new ComboBox
            {
                Width = 300,
                PlaceholderText = "Select club...",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            foreach (var club in clubs)
            {
                clubCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"{club.ShortName} - {club.LongName}",
                    Tag = club.ShortName
                });
            }

            // Venue combo (use existing clubs as venue options)
            var venueCombo = new ComboBox
            {
                Width = 300,
                PlaceholderText = "Select venue...",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            foreach (var club in clubs)
            {
                venueCombo.Items.Add(new ComboBoxItem
                {
                    Content = club.LongName,
                    Tag = club.LongName
                });
            }

            // Date picker (any date allowed)
            var datePicker = new DatePicker
            {
                Width = 300,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Date = DateTime.Today
            };

            // Status message
            var statusText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            };

            // Build content
            var content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Select Club:",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    clubCombo,
                    new TextBlock
                    {
                        Text = "Select Venue:",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    venueCombo,
                    new TextBlock
                    {
                        Text = "Select Date:",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    datePicker,
                    statusText
                }
            };

            // Create dialog
            var dialog = new ContentDialog
            {
                Title = "Create Team - Team Information",
                Content = content,
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                IsPrimaryButtonEnabled = false,
                XamlRoot = this.Content?.XamlRoot
            };

            // Track selections
            string? selectedClub = null;
            string? selectedVenue = null;
            DateTime? selectedDate = null;

            void UpdateDialogState()
            {
                selectedClub = (clubCombo.SelectedItem as ComboBoxItem)?.Tag as string;
                selectedVenue = (venueCombo.SelectedItem as ComboBoxItem)?.Tag as string;
                selectedDate = datePicker.Date.DateTime;

                dialog.IsPrimaryButtonEnabled = !string.IsNullOrEmpty(selectedClub) &&
                                                 !string.IsNullOrEmpty(selectedVenue) &&
                                                 selectedDate.HasValue;

                if (dialog.IsPrimaryButtonEnabled)
                {
                    statusText.Text = "";
                }
            }

            clubCombo.SelectionChanged += (s, args) => UpdateDialogState();
            venueCombo.SelectionChanged += (s, args) => UpdateDialogState();
            datePicker.DateChanged += (s, args) => UpdateDialogState();

            if (this.Content?.XamlRoot == null)
            {
                UpdateStatus("UI not ready.");
                return;
            }

            var result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
            {
                UpdateStatus("Team creation cancelled.");
                return;
            }

            // Check if draft exists
            var (draftExists, draftId, lastModified) = await _db.CheckDraftExistsAsync(
                selectedClub!, selectedVenue!, selectedDate!.Value);

            if (draftExists)
            {
                // Show load draft option
                var loadDraft = await ShowLoadDraftDialogAsync(selectedClub!, selectedVenue!, selectedDate!.Value, lastModified!.Value);
                if (loadDraft)
                {
                    // Load existing draft
                    await ShowTeamSelectionDialogAsync(selectedClub!, selectedVenue!, selectedDate!.Value, draftId);
                }
                else
                {
                    // Start new (will overwrite draft)
                    await ShowTeamSelectionDialogAsync(selectedClub!, selectedVenue!, selectedDate!.Value, null);
                }
            }
            else
            {
                // Start new draft
                await ShowTeamSelectionDialogAsync(selectedClub!, selectedVenue!, selectedDate!.Value, null);
            }
        }

        private async Task<bool> ShowLoadDraftDialogAsync(string club, string venue, DateTime date, DateTime lastModified)
        {
            var timeAgo = DateTime.Now - lastModified;
            var timeAgoText = timeAgo.TotalHours < 1
                ? $"{(int)timeAgo.TotalMinutes} minutes ago"
                : timeAgo.TotalDays < 1
                    ? $"{(int)timeAgo.TotalHours} hours ago"
                    : $"{(int)timeAgo.TotalDays} days ago";

            var content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "A draft team selection exists for:",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = $"Club: {club}\nVenue: {venue}\nDate: {date:dddd, MMMM d, yyyy}\n\nLast modified: {timeAgoText}",
                        Margin = new Thickness(20, 0, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "Would you like to load the draft or start a new selection?",
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 0)
                    }
                }
            };

            var dialog = new ContentDialog
            {
                Title = "Draft Exists",
                Content = content,
                PrimaryButtonText = "Load Draft",
                SecondaryButtonText = "Start New",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot == null) return false;

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        // ============================================================================
        // STEP 2: PLAYER SELECTION DIALOG
        // ============================================================================

        private async Task ShowTeamSelectionDialogAsync(string clubShortName, string venue, DateTime gameDate, string? existingDraftId)
        {
            // Load players for selected club
            var players = await _db!.GetPlayersByClubAsync(clubShortName);
            if (players == null || players.Count == 0)
            {
                await ShowErrorAsync("Create Team", $"No players found for club '{clubShortName}'.");
                return;
            }

            // Create divisions display areas
            var divAPanel = new StackPanel { Spacing = 4 };
            var divBPanel = new StackPanel { Spacing = 4 };
            var divCPanel = new StackPanel { Spacing = 4 };

            var divAScroll = new ScrollViewer
            {
                Content = divAPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsTabStop = true,
                TabIndex = 0
            };

            var divBScroll = new ScrollViewer
            {
                Content = divBPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsTabStop = true,
                TabIndex = 1
            };

            var divCScroll = new ScrollViewer
            {
                Content = divCPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsTabStop = true,
                TabIndex = 2
            };

            var selectionStatus = new TextBlock
            {
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 0),
                TextAlignment = TextAlignment.Center
            };

            var validationStatus = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                TextAlignment = TextAlignment.Center,
                FontSize = 12
            };

            List<SelectablePlayer> allSelectablePlayers = new();
            string? currentDraftId = existingDraftId;

            // Parse and categorize players by division
            foreach (var player in players)
            {
                if (string.IsNullOrWhiteSpace(player.IndexValue))
                    continue;

                if (!double.TryParse(player.IndexValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double handicap))
                    continue;

                if (handicap < 0 || handicap > 40)
                    continue;

                string division;
                if (handicap <= 12.4)
                    division = "A";
                else if (handicap <= 18.4)
                    division = "B";
                else if (handicap <= 24.4)
                    division = "C";
                else
                    continue;

                allSelectablePlayers.Add(new SelectablePlayer
                {
                    Player = player,
                    IsSelected = false,
                    Division = division,
                    Handicap = handicap
                });
            }

            // Sort players by name within each division
            allSelectablePlayers = allSelectablePlayers
                .OrderBy(p => p.Division)
                .ThenBy(p => p.Player.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Load existing draft selections if available
            if (!string.IsNullOrEmpty(existingDraftId))
            {
                var draftPlayers = await _db.LoadDraftPlayersAsync(existingDraftId);
                if (draftPlayers != null)
                {
                    var draftPlayerIds = new HashSet<string>(draftPlayers.Select(dp => dp.PlayerId));
                    foreach (var sp in allSelectablePlayers)
                    {
                        if (draftPlayerIds.Contains(sp.Player.Id))
                        {
                            sp.IsSelected = true;
                        }
                    }
                }
            }

            // Update display
            void UpdatePlayerDisplay()
            {
                var selectedCount = allSelectablePlayers.Count(p => p.IsSelected);
                var divACount = allSelectablePlayers.Count(p => p.IsSelected && p.Division == "A");
                var divBCount = allSelectablePlayers.Count(p => p.IsSelected && p.Division == "B");
                var divCCount = allSelectablePlayers.Count(p => p.IsSelected && p.Division == "C");

                selectionStatus.Text = $"Selected: Division A ({divACount}/3) | Division B ({divBCount}/3) | Division C ({divCCount}/2) | Total: {selectedCount}";

                // Validation for finalize (≤ max players)
                bool valid = divACount <= 3 && divBCount <= 3 && divCCount <= 2;
                if (!valid)
                {
                    var issues = new List<string>();
                    if (divACount > 3) issues.Add($"Division A has {divACount} (max 3)");
                    if (divBCount > 3) issues.Add($"Division B has {divBCount} (max 3)");
                    if (divCCount > 2) issues.Add($"Division C has {divCCount} (max 2)");
                    validationStatus.Text = $"⚠️ {string.Join(", ", issues)}";
                    validationStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                }
                else if (selectedCount > 0)
                {
                    validationStatus.Text = "✓ Ready to save or finalize";
                    validationStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);
                }
                else
                {
                    validationStatus.Text = "";
                }
            }

            // Create checkboxes for each player
            foreach (var sp in allSelectablePlayers)
            {
                var checkbox = new CheckBox
                {
                    Content = $"{sp.Player.Name} - HCP: {sp.Handicap:F1} - Games: {sp.Player.GamesPlayed}",
                    IsChecked = sp.IsSelected,
                    Tag = sp,
                    Margin = new Thickness(0, 2, 0, 2)
                };

                checkbox.Checked += (s, e) =>
                {
                    sp.IsSelected = true;
                    UpdatePlayerDisplay();
                };

                checkbox.Unchecked += (s, e) =>
                {
                    sp.IsSelected = false;
                    UpdatePlayerDisplay();
                };

                if (sp.Division == "A") divAPanel.Children.Add(checkbox);
                else if (sp.Division == "B") divBPanel.Children.Add(checkbox);
                else if (sp.Division == "C") divCPanel.Children.Add(checkbox);
            }

            // Build horizontal division layout
            var divisionsGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                Height = 400,
                Margin = new Thickness(0, 12, 0, 0)
            };

            // Division A
            var divABorder = new Border
            {
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 4, 0),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Division A (HCP 0.0 - 12.4)",
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(8, 8, 8, 0)
                        },
                        new TextBlock
                        {
                            Text = "Max 3 players",
                            FontSize = 12,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(8, 0, 8, 4)
                        }
                    }
                }
            };
            divAScroll.Height = 320;
            divAScroll.Margin = new Thickness(8, 0, 8, 8);
            ((StackPanel)divABorder.Child).Children.Add(divAScroll);
            Grid.SetColumn(divABorder, 0);
            divisionsGrid.Children.Add(divABorder);

            // Division B
            var divBBorder = new Border
            {
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(4, 0, 4, 0),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Division B (HCP 12.5 - 18.4)",
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(8, 8, 8, 0)
                        },
                        new TextBlock
                        {
                            Text = "Max 3 players",
                            FontSize = 12,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(8, 0, 8, 4)
                        }
                    }
                }
            };
            divBScroll.Height = 320;
            divBScroll.Margin = new Thickness(8, 0, 8, 8);
            ((StackPanel)divBBorder.Child).Children.Add(divBScroll);
            Grid.SetColumn(divBBorder, 1);
            divisionsGrid.Children.Add(divBBorder);

            // Division C
            var divCBorder = new Border
            {
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(4, 0, 0, 0),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Division C (HCP 18.5 - 24.4)",
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(8, 8, 8, 0)
                        },
                        new TextBlock
                        {
                            Text = "Max 2 players",
                            FontSize = 12,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(8, 0, 8, 4)
                        }
                    }
                }
            };
            divCScroll.Height = 320;
            divCScroll.Margin = new Thickness(8, 0, 8, 8);
            ((StackPanel)divCBorder.Child).Children.Add(divCScroll);
            Grid.SetColumn(divCBorder, 2);
            divisionsGrid.Children.Add(divCBorder);

            // Build main content
            var content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Team for: {clubShortName} @ {venue} ({gameDate:d})",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "Click on a division to scroll its players independently",
                        FontSize = 11,
                        FontStyle = Windows.UI.Text.FontStyle.Italic,
                        TextAlignment = TextAlignment.Center,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
                    },
                    divisionsGrid,
                    selectionStatus,
                    validationStatus
                }
            };

            // Show dialog with three buttons
            var dialog = new ContentDialog
            {
                Title = "Create Team - Select Players",
                Content = content,
                PrimaryButtonText = "Finalize Team",
                SecondaryButtonText = "Save Draft",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            UpdatePlayerDisplay();

            if (this.Content?.XamlRoot == null)
            {
                UpdateStatus("UI not ready.");
                return;
            }

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                // Finalize Team - validate and export CSV
                await FinalizeTeamAsync(clubShortName, venue, gameDate, allSelectablePlayers, currentDraftId);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                // Save Draft
                await SaveDraftAsync(clubShortName, venue, gameDate, allSelectablePlayers, currentDraftId);
            }
            else
            {
                UpdateStatus("Team selection cancelled.");
            }
        }

        // ============================================================================
        // SAVE DRAFT
        // ============================================================================

        private async Task SaveDraftAsync(string clubShortName, string venue, DateTime gameDate,
            List<SelectablePlayer> allPlayers, string? existingDraftId)
        {
            var selectedPlayers = allPlayers.Where(p => p.IsSelected).ToList();

            if (selectedPlayers.Count == 0)
            {
                await ShowErrorAsync("Save Draft", "No players selected. Draft not saved.");
                return;
            }

            try
            {
                var playersToSave = selectedPlayers.Select(sp =>
                    (sp.Player.Id, sp.Division, sp.Handicap)).ToList();

                var (success, draftId, error) = await _db!.SaveDraftAsync(
                    existingDraftId, clubShortName, venue, gameDate, playersToSave);

                if (success)
                {
                    UpdateStatus($"Draft saved: {selectedPlayers.Count} players");
                    await ShowErrorAsync("Draft Saved", $"✅ Team draft saved successfully!\n\nPlayers: {selectedPlayers.Count}\n\nYou can return later to edit and finalize the team.");
                }
                else
                {
                    await ShowErrorAsync("Save Failed", $"Failed to save draft:\n{error}");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Save Error", $"Failed to save draft:\n{ex.Message}");
            }
        }

        // ============================================================================
        // FINALIZE TEAM
        // ============================================================================

        private async Task FinalizeTeamAsync(string clubShortName, string venue, DateTime gameDate,
            List<SelectablePlayer> allPlayers, string? existingDraftId)
        {
            var selectedPlayers = allPlayers.Where(p => p.IsSelected).ToList();

            if (selectedPlayers.Count == 0)
            {
                await ShowErrorAsync("Finalize Team", "No players selected.");
                return;
            }

            // Validate counts (≤ max)
            var divACount = selectedPlayers.Count(p => p.Division == "A");
            var divBCount = selectedPlayers.Count(p => p.Division == "B");
            var divCCount = selectedPlayers.Count(p => p.Division == "C");

            if (divACount > 3 || divBCount > 3 || divCCount > 2)
            {
                var issues = new List<string>();
                if (divACount > 3) issues.Add($"Division A: {divACount} players (max 3)");
                if (divBCount > 3) issues.Add($"Division B: {divBCount} players (max 3)");
                if (divCCount > 2) issues.Add($"Division C: {divCCount} players (max 2)");

                await ShowErrorAsync("Validation Failed",
                    $"Cannot finalize team:\n\n{string.Join("\n", issues)}\n\nPlease adjust your selection.");
                return;
            }

            // Export to CSV
            await ExportTeamToCsvAsync(clubShortName, venue, gameDate, selectedPlayers, existingDraftId);
        }

        private async Task ExportTeamToCsvAsync(string clubShortName, string venue, DateTime gameDate,
            List<SelectablePlayer> selectedPlayers, string? existingDraftId)
        {
            try
            {
                // Sort by division, then by name
                var sortedPlayers = selectedPlayers
                    .OrderBy(p => p.Division)
                    .ThenBy(p => p.Player.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Build CSV content
                var csv = new StringBuilder();
                csv.AppendLine("Team Selection - Final Team");
                csv.AppendLine($"Club: {clubShortName}");
                csv.AppendLine($"Venue: {venue}");
                csv.AppendLine($"Date: {gameDate:yyyy-MM-dd (dddd, MMMM d, yyyy)}");
                csv.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                csv.AppendLine($"Total Players: {sortedPlayers.Count}");
                csv.AppendLine();
                csv.AppendLine("Division,Code,Name,Handicap,Games Played");

                foreach (var sp in sortedPlayers)
                {
                    var division = CsvEscape(sp.Division);
                    var code = CsvEscape(sp.Player.Code);
                    var name = CsvEscape(sp.Player.Name);
                    var handicap = sp.Handicap.ToString("F1", CultureInfo.InvariantCulture);
                    var gamesPlayed = sp.Player.GamesPlayed.ToString();

                    csv.AppendLine($"{division},{code},{name},{handicap},{gamesPlayed}");
                }

                var csvContent = csv.ToString();

                // Suggest filename
                var suggestedName = $"Team_{clubShortName}_{venue.Replace(" ", "_")}_{gameDate:yyyyMMdd}";

                // Show file save picker
                var savedFile = await SaveCsvFileAsync(suggestedName, csvContent);
                if (savedFile != null)
                {
                    // Delete draft after successful export
                    if (!string.IsNullOrEmpty(existingDraftId))
                    {
                        await _db!.DeleteDraftAsync(existingDraftId);
                    }

                    UpdateStatus($"Team finalized and exported: {savedFile.Name}");
                    await ShowErrorAsync("Team Finalized",
                        $"✅ Team finalized and exported successfully!\n\n{savedFile.Path}\n\nPlayers: {sortedPlayers.Count}\n\nDraft has been deleted.");
                }
                else
                {
                    UpdateStatus("Export cancelled.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Export Error", $"Failed to export team:\n{ex.Message}");
            }
        }

        // NOTE: CsvEscape method already exists in MainWindow.PdfImport.cs
        // NOTE: SaveCsvFileAsync method already exists in MainWindow.Reports.cs
        // Both are accessible since this is a partial class

        /* 
         * ============================================================================
         * POPULATE GAMES PLAYED - KEPT FOR FUTURE USE (COMMENTED OUT)
         * ============================================================================
         * 
        private async void OnCreateTeamClicked_PopulateGamesPlayed(object sender, RoutedEventArgs e)
        {
            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Create Team", "Database not initialized.");
                return;
            }

            try
            {
                // Show confirmation dialog
                var confirmDialog = new ContentDialog
                {
                    Title = "Create Team - Populate Games Played",
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "This will populate the 'Games Played' field for all players by counting their results.",
                                TextWrapping = TextWrapping.Wrap,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                            },
                            new TextBlock
                            {
                                Text = "• Counts unique games (venue + date combination)\n• Only counts valid results (after 2020)\n• Updates all players in the database\n• Safe to run multiple times",
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = "This operation is safe and can be run anytime to update the counts.",
                                TextWrapping = TextWrapping.Wrap,
                                FontStyle = Windows.UI.Text.FontStyle.Italic
                            }
                        }
                    },
                    PrimaryButtonText = "Populate Games Played",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content?.XamlRoot
                };

                if (this.Content?.XamlRoot == null)
                {
                    UpdateStatus("UI not ready.");
                    return;
                }

                var result = await confirmDialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    UpdateStatus("Operation cancelled.");
                    return;
                }

                UpdateStatus("Populating Games Played counts...");

                // Execute the migration and population
                var (success, error, updatedCount) = await _db.PopulateGamesPlayedAsync();

                if (!success)
                {
                    UpdateStatus($"Failed to populate Games Played: {error}");
                    await ShowErrorAsync("Operation Failed", $"Failed to populate Games Played:\n{error}");
                    return;
                }

                UpdateStatus($"Games Played updated for {updatedCount} players.");
                await ShowErrorAsync("Success", $"✅ Games Played populated successfully!\n\nUpdated: {updatedCount} players\n\nThe 'Games Played' field now shows the number of games each player has participated in.");

                // Reload current player if in player editor
                if (PlayerEditorPanel != null && PlayerEditorPanel.Visibility == Visibility.Visible)
                {
                    await ReloadCurrentPlayerAsync();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Operation failed: {ex.Message}");
                await ShowErrorAsync("Error", $"An error occurred:\n{ex.Message}");
            }
        }

        private async Task ReloadCurrentPlayerAsync()
        {
            if (_players.Count == 0 || _playerIndex < 0 || _playerIndex >= _players.Count)
                return;

            var currentPlayer = _players[_playerIndex];
            if (string.IsNullOrEmpty(currentPlayer.ClubShortName))
                return;

            // Reload players from database
            var reloadedPlayers = await _db!.GetPlayersByClubAsync(currentPlayer.ClubShortName);
            if (reloadedPlayers != null && reloadedPlayers.Count > 0)
            {
                _players.Clear();
                _players.AddRange(reloadedPlayers);

                // Try to find the same player by ID
                var newIndex = _players.FindIndex(p => p.Id == currentPlayer.Id);
                if (newIndex >= 0)
                {
                    _playerIndex = newIndex;
                }
                else
                {
                    _playerIndex = Math.Min(_playerIndex, _players.Count - 1);
                }

                ShowPlayer();
            }
        }
        */
    }
}