//MainWindow.CreateTeam.cs
//============================
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
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

                // FIXED: Changed ShowCurrentPlayer() to ShowPlayer()
                ShowPlayer();
            }
        }
    }
}