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
                // Show team selection dialog
                await ShowTeamSelectionDialogAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Team selection failed: {ex.Message}");
                await ShowErrorAsync("Error", $"An error occurred:\n{ex.Message}");
            }
        }

        private async Task ShowTeamSelectionDialogAsync()
        {
            // Get all clubs
            var clubs = await _db!.GetAllClubsAsync();
            if (clubs == null || clubs.Count == 0)
            {
                await ShowErrorAsync("Create Team", "No clubs found in the database.");
                return;
            }

            // Create club selection combo
            var clubCombo = new ComboBox
            {
                Width = 300,
                PlaceholderText = "Select a club...",
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

            // Create divisions display areas
            var divAPanel = new StackPanel { Spacing = 4 };
            var divBPanel = new StackPanel { Spacing = 4 };
            var divCPanel = new StackPanel { Spacing = 4 };

            // Each division gets its own ScrollViewer with proper sizing and independent scrolling
            var divAScroll = new ScrollViewer
            {
                Content = divAPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsTabStop = true, // Allow focus for independent scrolling
                TabIndex = 0
            };

            var divBScroll = new ScrollViewer
            {
                Content = divBPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsTabStop = true, // Allow focus for independent scrolling
                TabIndex = 1
            };

            var divCScroll = new ScrollViewer
            {
                Content = divCPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsTabStop = true, // Allow focus for independent scrolling
                TabIndex = 2
            };

            var selectionStatus = new TextBlock
            {
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 0),
                TextAlignment = TextAlignment.Center
            };

            List<SelectablePlayer> allSelectablePlayers = new();
            ContentDialog? currentDialog = null;

            // Club selection changed handler
            void UpdatePlayerDisplay()
            {
                var selectedCount = allSelectablePlayers.Count(p => p.IsSelected);
                var divACount = allSelectablePlayers.Count(p => p.IsSelected && p.Division == "A");
                var divBCount = allSelectablePlayers.Count(p => p.IsSelected && p.Division == "B");
                var divCCount = allSelectablePlayers.Count(p => p.IsSelected && p.Division == "C");

                selectionStatus.Text = $"Selected: Division A ({divACount}/3) | Division B ({divBCount}/3) | Division C ({divCCount}/2) | Total: {selectedCount}";

                // Enable export button if at least one player is selected
                if (currentDialog != null)
                {
                    currentDialog.IsPrimaryButtonEnabled = selectedCount > 0;
                }
            }

            clubCombo.SelectionChanged += async (s, args) =>
            {
                if (clubCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string clubShort)
                    return;

                try
                {
                    // Load players for selected club
                    var players = await _db!.GetPlayersByClubAsync(clubShort);
                    if (players == null || players.Count == 0)
                    {
                        divAPanel.Children.Clear();
                        divBPanel.Children.Clear();
                        divCPanel.Children.Clear();
                        selectionStatus.Text = "No players found for this club.";
                        if (currentDialog != null) currentDialog.IsPrimaryButtonEnabled = false;
                        return;
                    }

                    // Clear previous data
                    allSelectablePlayers.Clear();
                    divAPanel.Children.Clear();
                    divBPanel.Children.Clear();
                    divCPanel.Children.Clear();

                    // Parse and categorize players by division
                    foreach (var player in players)
                    {
                        // Skip players without handicap
                        if (string.IsNullOrWhiteSpace(player.IndexValue))
                            continue;

                        if (!double.TryParse(player.IndexValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double handicap))
                            continue;

                        if (handicap < 0 || handicap > 40)
                            continue;

                        // Determine division
                        string division;

                        if (handicap <= 12.4)
                        {
                            division = "A";
                        }
                        else if (handicap <= 18.4)
                        {
                            division = "B";
                        }
                        else if (handicap <= 24.4)
                        {
                            division = "C";
                        }
                        else
                        {
                            continue; // Skip players with handicap > 24.4
                        }

                        var selectablePlayer = new SelectablePlayer
                        {
                            Player = player,
                            IsSelected = false,
                            Division = division,
                            Handicap = handicap
                        };

                        allSelectablePlayers.Add(selectablePlayer);
                    }

                    // Sort players by name within each division
                    allSelectablePlayers = allSelectablePlayers
                        .OrderBy(p => p.Division)
                        .ThenBy(p => p.Player.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    // Create checkboxes for each player - NO CODE in display
                    foreach (var sp in allSelectablePlayers)
                    {
                        var checkbox = new CheckBox
                        {
                            Content = $"{sp.Player.Name} - HCP: {sp.Handicap:F1} - Games: {sp.Player.GamesPlayed}",
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

                        // Add to appropriate division panel
                        if (sp.Division == "A") divAPanel.Children.Add(checkbox);
                        else if (sp.Division == "B") divBPanel.Children.Add(checkbox);
                        else if (sp.Division == "C") divCPanel.Children.Add(checkbox);
                    }

                    UpdatePlayerDisplay();
                }
                catch (Exception ex)
                {
                    selectionStatus.Text = $"Error loading players: {ex.Message}";
                }
            };

            // Build horizontal division layout using Grid
            var divisionsGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                Height = 400, // Fixed height for all divisions
                Margin = new Thickness(0, 12, 0, 0)
            };

            // Division A column with border for better focus indication
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
                            Text = "Need 3 players",
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

            // Division B column with border for better focus indication
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
                            Text = "Need 3 players",
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

            // Division C column with border for better focus indication
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
                            Text = "Need 2 players",
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
                        Text = "Select Club:",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    clubCombo,
                    new TextBlock
                    {
                        Text = "Click on a division to scroll its players independently",
                        FontSize = 11,
                        FontStyle = Windows.UI.Text.FontStyle.Italic,
                        TextAlignment = TextAlignment.Center,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
                    },
                    divisionsGrid,
                    selectionStatus
                }
            };

            // Show dialog with Export as Primary button
            var dialog = new ContentDialog
            {
                Title = "Create Team - Select Players",
                Content = content,
                PrimaryButtonText = "Export CSV",
                CloseButtonText = "Close",
                IsPrimaryButtonEnabled = false,
                XamlRoot = this.Content?.XamlRoot
            };

            currentDialog = dialog;

            if (this.Content?.XamlRoot != null)
            {
                var result = await dialog.ShowAsync();

                // If user clicked Export CSV
                if (result == ContentDialogResult.Primary)
                {
                    await ExportTeamToCsvAsync(allSelectablePlayers.Where(p => p.IsSelected).ToList());
                }
            }
        }

        private async Task ExportTeamToCsvAsync(List<SelectablePlayer> selectedPlayers)
        {
            if (selectedPlayers.Count == 0)
            {
                await ShowErrorAsync("Export Team", "No players selected.");
                return;
            }

            try
            {
                // Sort by division, then by name
                var sortedPlayers = selectedPlayers
                    .OrderBy(p => p.Division)
                    .ThenBy(p => p.Player.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Build CSV content - CODE is included here
                var csv = new StringBuilder();
                csv.AppendLine("Team Selection Export");
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

                // Show file save picker
                var savedFile = await SaveCsvFileAsync($"Team_Selection_{DateTime.Now:yyyyMMdd_HHmmss}", csvContent);
                if (savedFile != null)
                {
                    UpdateStatus($"Team exported: {savedFile.Name}");
                    await ShowErrorAsync("Export Complete", $"✅ Team selection exported successfully!\n\n{savedFile.Path}\n\nPlayers: {sortedPlayers.Count}");
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