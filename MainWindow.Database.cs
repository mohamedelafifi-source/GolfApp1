//MainWindow.Database.cs
//============================
using GolfApp1.Models;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        private string? _currentDbPath;

        private async void OnClearResultsClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus(""); // Clear status

            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Clear Results", "Database not initialized.");
                return;
            }

            try
            {
                // Show warning dialog
                var warningDialog = new ContentDialog
                {
                    Title = "⚠️ Clear All Results?",
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "This will permanently delete ALL result records from the database.",
                                TextWrapping = TextWrapping.Wrap,
                                FontWeight = Microsoft.UI.Text.FontWeights.Bold
                            },
                            new TextBlock
                            {
                                Text = "• All clubs and players will remain intact\n• All result entries will be permanently deleted\n• This action CANNOT be undone",
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = "Consider creating a backup before proceeding.",
                                TextWrapping = TextWrapping.Wrap,
                                FontStyle = Windows.UI.Text.FontStyle.Italic,
                                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Crimson)
                            }
                        }
                    },
                    PrimaryButtonText = "Delete All Results",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content?.XamlRoot
                };

                if (this.Content?.XamlRoot == null)
                {
                    UpdateStatus("UI not ready.");
                    return;
                }

                var result = await warningDialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    UpdateStatus("Clear results cancelled.");
                    return;
                }

                UpdateStatus("Clearing all results...");

                // Execute SQL to delete all results
                var error = await ExecuteClearResultsAsync();
                if (error != null)
                {
                    UpdateStatus($"Clear results failed: {error}");
                    await ShowErrorAsync("Clear Results Failed", $"Failed to clear results:\n{error}");
                    return;
                }

                UpdateStatus("All results cleared successfully.");
                await ShowErrorAsync("Clear Results Complete", "All result records have been permanently deleted.\n\nClubs and players remain intact.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Clear results failed: {ex.Message}");
                await ShowErrorAsync("Clear Results Error", $"An error occurred:\n{ex.Message}");
            }
        }

        private async void OnBackupDatabaseClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus(""); // Clear status

            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Backup Database", "Database not initialized.");
                return;
            }

            try
            {
                UpdateStatus("Preparing database backup...");

                // Get current database path
                var dbPath = GetDatabasePath();
                if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                {
                    UpdateStatus("Database file not found.");
                    await ShowErrorAsync("Backup Error", "Database file not found.");
                    return;
                }

                // Show file save picker
                var savePicker = new FileSavePicker();
                InitializeWithWindow.Initialize(savePicker, WindowNative.GetWindowHandle(this));
                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("Database Backup", new System.Collections.Generic.List<string> { ".db", ".bak" });
                savePicker.SuggestedFileName = $"golfapp_backup_{DateTime.Now:yyyyMMdd_HHmmss}";

                var file = await savePicker.PickSaveFileAsync().AsTask();
                if (file == null)
                {
                    UpdateStatus("Backup cancelled.");
                    return;
                }

                UpdateStatus($"Creating backup: {file.Name}...");

                // Close database connection for safe backup
                _db?.Dispose();
                _db = null;

                // Copy database file
                await Task.Run(() =>
                {
                    File.Copy(dbPath, file.Path, overwrite: true);
                });

                // Reopen database
                await ReinitializeDatabaseAsync();

                UpdateStatus($"Backup completed: {file.Name}");
                await ShowErrorAsync("Backup Complete", $"Database backup created successfully:\n\n{file.Path}\n\nFile size: {new FileInfo(file.Path).Length / 1024} KB");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Backup failed: {ex.Message}");
                await ShowErrorAsync("Backup Error", $"Failed to create backup:\n{ex.Message}");

                // Try to reopen database even if backup failed
                try
                {
                    await ReinitializeDatabaseAsync();
                }
                catch { /* ignore */ }
            }
        }

        private async void OnRestoreDatabaseClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus(""); // Clear status

            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Restore Database", "Database not initialized.");
                return;
            }

            try
            {
                // Show warning dialog
                var warningDialog = new ContentDialog
                {
                    Title = "⚠️ Restore Database from Backup?",
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "This will REPLACE your current database with the backup file.",
                                TextWrapping = TextWrapping.Wrap,
                                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Crimson)
                            },
                            new TextBlock
                            {
                                Text = "⚠️ ALL CURRENT DATA WILL BE LOST!\n\n• All clubs, players, and results will be replaced\n• This action CANNOT be undone\n• Current data will be permanently overwritten",
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = "Recommendation: Create a backup of your current database before restoring.",
                                TextWrapping = TextWrapping.Wrap,
                                FontStyle = Windows.UI.Text.FontStyle.Italic
                            }
                        }
                    },
                    PrimaryButtonText = "Continue with Restore",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content?.XamlRoot
                };

                if (this.Content?.XamlRoot == null)
                {
                    UpdateStatus("UI not ready.");
                    return;
                }

                var warningResult = await warningDialog.ShowAsync();
                if (warningResult != ContentDialogResult.Primary)
                {
                    UpdateStatus("Restore cancelled.");
                    return;
                }

                // Show file picker
                var openPicker = new FileOpenPicker();
                InitializeWithWindow.Initialize(openPicker, WindowNative.GetWindowHandle(this));
                openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                openPicker.FileTypeFilter.Add(".db");
                openPicker.FileTypeFilter.Add(".bak");

                var backupFile = await openPicker.PickSingleFileAsync().AsTask();
                if (backupFile == null)
                {
                    UpdateStatus("Restore cancelled.");
                    return;
                }

                UpdateStatus($"Restoring from: {backupFile.Name}...");

                // Get current database path
                var dbPath = GetDatabasePath();
                if (string.IsNullOrEmpty(dbPath))
                {
                    UpdateStatus("Database path not found.");
                    await ShowErrorAsync("Restore Error", "Database path not found.");
                    return;
                }

                // Validate backup file is a SQLite database
                if (!await ValidateSqliteDatabaseAsync(backupFile.Path))
                {
                    UpdateStatus("Invalid backup file.");
                    await ShowErrorAsync("Restore Error", "The selected file is not a valid SQLite database backup.");
                    return;
                }

                // Close database connection
                _db?.Dispose();
                _db = null;

                // Copy backup file over current database
                await Task.Run(() =>
                {
                    File.Copy(backupFile.Path, dbPath, overwrite: true);
                });

                // Reinitialize database and reload all data
                await ReinitializeDatabaseAsync();

                // Reload UI data
                await ReloadAllDataAsync();

                UpdateStatus("Database restored successfully.");
                await ShowErrorAsync("Restore Complete", $"Database restored successfully from:\n\n{backupFile.Name}\n\nAll data has been reloaded.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Restore failed: {ex.Message}");
                await ShowErrorAsync("Restore Error", $"Failed to restore database:\n{ex.Message}");

                // Try to reopen database even if restore failed
                try
                {
                    await ReinitializeDatabaseAsync();
                }
                catch { /* ignore */ }
            }
        }

        private async void OnCleanDatabaseClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus(""); // Clear status

            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Clean Database", "Database not initialized.");
                return;
            }

            try
            {
                // Show warning dialog with details
                var warningDialog = new ContentDialog
                {
                    Title = "🧹 Clean Database?",
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "This will remove invalid and duplicate result entries:",
                                TextWrapping = TextWrapping.Wrap,
                                FontWeight = Microsoft.UI.Text.FontWeights.Bold
                            },
                            new TextBlock
                            {
                                Text = "✓ Entries with missing venues\n✓ Entries with invalid dates (before 2020)\n✓ Entries with missing player names\n✓ Duplicate entries (same player, date, venue, club)",
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(20, 0, 0, 0)
                            },
                            new TextBlock
                            {
                                Text = "⚠️ Valid results will NOT be affected.\n⚠️ This action CANNOT be undone.",
                                TextWrapping = TextWrapping.Wrap,
                                FontStyle = Windows.UI.Text.FontStyle.Italic,
                                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange)
                            },
                            new TextBlock
                            {
                                Text = "Recommendation: Create a backup before cleaning.",
                                TextWrapping = TextWrapping.Wrap,
                                FontSize = 11,
                                FontStyle = Windows.UI.Text.FontStyle.Italic
                            }
                        }
                    },
                    PrimaryButtonText = "Clean Database",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content?.XamlRoot
                };

                if (this.Content?.XamlRoot == null)
                {
                    UpdateStatus("UI not ready.");
                    return;
                }

                var result = await warningDialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    UpdateStatus("Database cleanup cancelled.");
                    return;
                }

                UpdateStatus("Cleaning database...");

                // Execute cleanup
                var (success, error, removedCount) = await _db.CleanDatabaseAsync();

                if (!success)
                {
                    UpdateStatus($"Database cleanup failed: {error}");
                    await ShowErrorAsync("Cleanup Failed", $"Failed to clean database:\n{error}");
                    return;
                }

                if (removedCount == 0)
                {
                    UpdateStatus("Database is already clean! No invalid entries found.");
                    await ShowErrorAsync("Database Clean", "✅ Database is already clean!\n\nNo invalid or duplicate entries were found.");
                }
                else
                {
                    UpdateStatus($"Database cleaned: {removedCount} invalid entries removed.");
                    await ShowErrorAsync("Cleanup Complete", $"✅ Database cleaned successfully!\n\n{removedCount} invalid/duplicate entries were removed.\n\nYour database is now optimized.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Database cleanup failed: {ex.Message}");
                await ShowErrorAsync("Cleanup Error", $"An error occurred during cleanup:\n{ex.Message}");
            }
        }

        private async Task<string?> ExecuteClearResultsAsync()
        {
            if (_db is null) return "Database not initialized.";

            try
            {
                var method = _db.GetType().GetMethod("ClearAllResultsAsync");
                if (method != null)
                {
                    var task = method.Invoke(_db, null) as Task<string>;
                    return await task!;
                }

                return "ClearAllResultsAsync method not found in Database class.";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private string? GetDatabasePath()
        {
            if (!string.IsNullOrEmpty(_currentDbPath))
                return _currentDbPath;

            try
            {
                var dataFolder = GetDataFolder();
                _currentDbPath = Path.Combine(dataFolder, "golfapp.db");
                return _currentDbPath;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> ValidateSqliteDatabaseAsync(string filePath)
        {
            try
            {
                await using var stream = File.OpenRead(filePath);
                var buffer = new byte[16];
                await stream.ReadAsync(buffer, 0, 16);

                // SQLite file header: "SQLite format 3\0"
                var header = System.Text.Encoding.ASCII.GetString(buffer);
                return header.StartsWith("SQLite format 3");
            }
            catch
            {
                return false;
            }
        }

        private async Task ReinitializeDatabaseAsync()
        {
            var dataFolder = GetDataFolder();
            var dbPath = Path.Combine(dataFolder, "golfapp.db");

            _db?.Dispose();
            _db = new Data.Database(dbPath);
            await _db.InitializeAsync();

            if (_vm != null)
            {
                _vm = new ViewModels.MainViewModel(_db);
                var root = this.Content as FrameworkElement;
                if (root != null)
                {
                    root.DataContext = _vm;
                }
            }
        }

        private async Task ReloadAllDataAsync()
        {
            if (_vm == null) return;

            await _vm.LoadClubsAsync();
            RefreshLocalClubsFromVm();
            _index = 0;

            if (EditorArea.Visibility == Visibility.Visible)
            {
                ShowCurrent();
            }

            UpdateStatus("Data reloaded from restored database.");
        }
    }
}