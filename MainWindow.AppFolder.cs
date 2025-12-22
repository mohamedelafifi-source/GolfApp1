// MainWindow.AppFolder.cs
//=============================
// App Folder Management - Selection, confirmation, and state management

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        private bool _appFolderConfirmed = false;
        private string? _confirmedAppFolder = null;

        /// <summary>
        /// Show dialog to select/confirm app data folder
        /// </summary>
        private async void OnSetAppFolderClicked(object sender, RoutedEventArgs e)
        {
            await ShowSetAppFolderDialogAsync(forceSelection: true);
        }

        /// <summary>
        /// Show the app folder selection/confirmation dialog
        /// </summary>
        private async Task<bool> ShowSetAppFolderDialogAsync(bool forceSelection = false)
        {
            UpdateStatus("");

            try
            {
                // Get current configured path
                var currentPath = AppSettings.GetAppDataFolderPath();
                var currentDbLocation = AppSettings.GetCurrentDatabaseLocation();

                // Build dialog content
                var contentPanel = new StackPanel { Spacing = 12, Margin = new Thickness(0) };

                // Header
                var headerText = new TextBlock
                {
                    Text = forceSelection ? "Set Application Data Folder" : "Confirm Application Data Folder",
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                };
                contentPanel.Children.Add(headerText);

                // Explanation
                var explanationText = new TextBlock
                {
                    Text = "All application data (database, exports, backups) will be stored in this folder.\n\nYou can create a new folder or select an existing one.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
                };
                contentPanel.Children.Add(explanationText);

                // Current path display
                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    var currentPathPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
                    currentPathPanel.Children.Add(new TextBlock 
                    { 
                        Text = "Current Folder:", 
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold 
                    });
                    currentPathPanel.Children.Add(new TextBlock 
                    { 
                        Text = currentPath, 
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 12
                    });
                    contentPanel.Children.Add(currentPathPanel);
                }

                // Database location info
                if (!string.IsNullOrWhiteSpace(currentDbLocation))
                {
                    var dbLocationPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
                    dbLocationPanel.Children.Add(new TextBlock 
                    { 
                        Text = "Current Database:", 
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold 
                    });
                    dbLocationPanel.Children.Add(new TextBlock 
                    { 
                        Text = currentDbLocation, 
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 12,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange)
                    });
                    contentPanel.Children.Add(dbLocationPanel);
                }

                // Create dialog
                var dialog = new ContentDialog
                {
                    Title = forceSelection ? "?? Set App Folder" : "?? Confirm App Folder",
                    Content = new ScrollViewer 
                    { 
                        Content = contentPanel, 
                        MaxHeight = 500 
                    },
                    PrimaryButtonText = forceSelection ? "Select Folder..." : "Confirm",
                    SecondaryButtonText = forceSelection && !string.IsNullOrWhiteSpace(currentPath) ? "Keep Current" : null,
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.Content?.XamlRoot
                };

                if (this.Content?.XamlRoot == null)
                {
                    UpdateStatus("UI not ready.");
                    return false;
                }

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // Select new folder
                    var newFolder = await PickFolderAsync();
                    if (newFolder == null)
                    {
                        UpdateStatus("Folder selection cancelled.");
                        return false;
                    }

                    // Save the new path
                    if (!AppSettings.SetAppDataFolderPath(newFolder))
                    {
                        await ShowErrorAsync("Error", "Failed to save folder path setting.");
                        return false;
                    }

                    // Offer to migrate existing database
                    if (!string.IsNullOrWhiteSpace(currentDbLocation) && currentDbLocation != Path.Combine(newFolder, "golfapp.db"))
                    {
                        await OfferDatabaseMigrationAsync(currentDbLocation, newFolder);
                    }

                    _confirmedAppFolder = newFolder;
                    _appFolderConfirmed = true;
                    EnableMenuItems(true);

                    UpdateStatus($"App folder set to: {newFolder}");
                    await ShowInfoAsync("Folder Set", $"Application data folder configured:\n\n{newFolder}\n\nAll data will be stored here.");

                    // Reinitialize database with new path
                    await ReinitializeDatabaseWithFolderAsync(newFolder);

                    return true;
                }
                else if (result == ContentDialogResult.Secondary && !string.IsNullOrWhiteSpace(currentPath))
                {
                    // Keep current
                    _confirmedAppFolder = currentPath;
                    _appFolderConfirmed = true;
                    EnableMenuItems(true);
                    UpdateStatus($"Using current app folder: {currentPath}");
                    return true;
                }
                else
                {
                    // Cancelled
                    if (!forceSelection && !string.IsNullOrWhiteSpace(currentPath))
                    {
                        _confirmedAppFolder = currentPath;
                        _appFolderConfirmed = true;
                        EnableMenuItems(true);
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error setting app folder: {ex.Message}");
                await ShowErrorAsync("Error", $"Failed to set app folder:\n{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Pick a folder using the folder picker
        /// </summary>
        private async Task<string?> PickFolderAsync()
        {
            try
            {
                var picker = new FolderPicker();
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add("*");

                var folder = await picker.PickSingleFolderAsync();
                return folder?.Path;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Folder picker error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Offer to migrate existing database to new folder
        /// </summary>
        private async Task OfferDatabaseMigrationAsync(string currentDbPath, string newFolder)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Migrate Existing Database?",
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "An existing database was found. Would you like to copy it to the new folder?",
                                TextWrapping = TextWrapping.Wrap,
                                FontWeight = Microsoft.UI.Text.FontWeights.Bold
                            },
                            new TextBlock
                            {
                                Text = $"From: {currentDbPath}\n\nTo: {Path.Combine(newFolder, "golfapp.db")}",
                                TextWrapping = TextWrapping.Wrap,
                                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                                FontSize = 11
                            },
                            new TextBlock
                            {
                                Text = "?? The original database will remain in its current location.",
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange)
                            }
                        }
                    },
                    PrimaryButtonText = "Copy Database",
                    CloseButtonText = "Skip",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.Content?.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var newDbPath = Path.Combine(newFolder, "golfapp.db");

                    // Ensure folder exists
                    if (!Directory.Exists(newFolder))
                    {
                        Directory.CreateDirectory(newFolder);
                    }

                    // Copy database
                    File.Copy(currentDbPath, newDbPath, overwrite: true);

                    UpdateStatus($"Database copied to new location.");
                    await ShowInfoAsync("Migration Complete", $"Database successfully copied to:\n\n{newDbPath}");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Migration error: {ex.Message}");
                await ShowErrorAsync("Migration Error", $"Failed to copy database:\n{ex.Message}\n\nYou can manually copy the database file later.");
            }
        }

        /// <summary>
        /// Offer automatic migration on startup if database is missing or empty
        /// </summary>
        private async Task OfferAutomaticMigrationAsync(string configuredFolder)
        {
            try
            {
                var oldDbLocation = AppSettings.GetCurrentDatabaseLocation();
                if (string.IsNullOrWhiteSpace(oldDbLocation) || !File.Exists(oldDbLocation))
                {
                    return;
                }

                var newDbPath = Path.Combine(configuredFolder, "golfapp.db");
                
                // Get file sizes
                var oldFileInfo = new FileInfo(oldDbLocation);
                var oldSizeKB = oldFileInfo.Length / 1024;

                string existingInfo = "";
                if (File.Exists(newDbPath))
                {
                    var newFileInfo = new FileInfo(newDbPath);
                    var newSizeKB = newFileInfo.Length / 1024;
                    existingInfo = $"\n\nCurrent database at destination: {newSizeKB:N0} KB";
                }

                var dialog = new ContentDialog
                {
                    Title = "?? Database Found - Auto Migration",
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "A database with your data was found in a different location!",
                                TextWrapping = TextWrapping.Wrap,
                                FontWeight = Microsoft.UI.Text.FontWeights.Bold
                            },
                            new TextBlock
                            {
                                Text = $"Found database: {oldSizeKB:N0} KB\nLocation: {oldDbLocation}{existingInfo}",
                                TextWrapping = TextWrapping.Wrap,
                                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                                FontSize = 11
                            },
                            new TextBlock
                            {
                                Text = "Would you like to copy this database to your configured app folder?",
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(0, 8, 0, 0)
                            },
                            new TextBlock
                            {
                                Text = "? Recommended: This will preserve all your clubs, players, and results.",
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green),
                                FontSize = 11
                            }
                        }
                    },
                    PrimaryButtonText = "Yes, Copy Database",
                    SecondaryButtonText = "No, Start Fresh",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.Content?.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    // Copy database
                    if (!Directory.Exists(configuredFolder))
                    {
                        Directory.CreateDirectory(configuredFolder);
                    }

                    File.Copy(oldDbLocation, newDbPath, overwrite: true);

                    UpdateStatus($"? Database copied successfully ({oldSizeKB:N0} KB)");
                    await ShowInfoAsync("Migration Complete", 
                        $"Your database has been successfully copied!\n\n" +
                        $"Size: {oldSizeKB:N0} KB\n" +
                        $"Location: {newDbPath}\n\n" +
                        $"All your clubs, players, and results are now available.");
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    UpdateStatus("Starting with fresh database.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Auto-migration error: {ex.Message}");
                await ShowErrorAsync("Migration Error", 
                    $"Failed to copy database automatically:\n{ex.Message}\n\n" +
                    $"You can use 'Database ? Set App Folder' to try again or manually copy the file.");
            }
        }

        /// <summary>
        /// Reinitialize database with the specified folder
        /// </summary>
        private async Task ReinitializeDatabaseWithFolderAsync(string folder)
        {
            try
            {
                // Ensure folder exists
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var dbPath = Path.Combine(folder, "golfapp.db");

                // Close current database
                _db?.Dispose();
                _db = null;

                // Create new database instance
                _db = new Data.Database(dbPath);
                await _db.InitializeAsync();

                // Reinitialize view model
                if (_vm != null)
                {
                    _vm = new ViewModels.MainViewModel(_db);
                    var root = this.Content as FrameworkElement;
                    if (root != null)
                    {
                        root.DataContext = _vm;
                    }

                    // Reload data
                    await _vm.LoadClubsAsync();
                    RefreshLocalClubsFromVm();
                }

                UpdateStatus($"Database initialized at: {dbPath}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Database initialization error: {ex.Message}");
                await ShowErrorAsync("Database Error", $"Failed to initialize database:\n{ex.Message}");
            }
        }

        /// <summary>
        /// Enable or disable menu items based on app folder confirmation status
        /// </summary>
        private void EnableMenuItems(bool enabled)
        {
            // Database menu items (except Set App Folder)
            if (ClearResultsMenuItem != null) ClearResultsMenuItem.IsEnabled = enabled;
            if (BackupDatabaseMenuItem != null) BackupDatabaseMenuItem.IsEnabled = enabled;
            if (RestoreDatabaseMenuItem != null) RestoreDatabaseMenuItem.IsEnabled = enabled;
            if (CleanDatabaseMenuItem != null) CleanDatabaseMenuItem.IsEnabled = enabled;

            // Data menu button and items
            if (DataButton != null) DataButton.IsEnabled = enabled;
            if (ClubDataMenuItem != null) ClubDataMenuItem.IsEnabled = enabled;
            if (NewResultsMenuItem != null) NewResultsMenuItem.IsEnabled = enabled;
            if (ExistingResultsMenuItem != null) ExistingResultsMenuItem.IsEnabled = enabled;

            // Teams menu button and items
            if (TeamsButton != null) TeamsButton.IsEnabled = enabled;
            if (CreateTeamMenuItem != null) CreateTeamMenuItem.IsEnabled = enabled;
            if (CreateGameMenuItem != null) CreateGameMenuItem.IsEnabled = enabled;

            // Reports menu button and items
            if (ReportsButton != null) ReportsButton.IsEnabled = enabled;
            if (ReportByClubMenuItem != null) ReportByClubMenuItem.IsEnabled = enabled;
            if (ReportByPlayerMenuItem != null) ReportByPlayerMenuItem.IsEnabled = enabled;
            if (ReportByAveragesMenuItem != null) ReportByAveragesMenuItem.IsEnabled = enabled;
        }

        /// <summary>
        /// Open the database folder in Windows Explorer
        /// </summary>
        private void OnOpenDbFolderClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataFolder = GetDataFolder();

                if (Directory.Exists(dataFolder))
                {
                    // Open folder in Windows Explorer
                    System.Diagnostics.Process.Start("explorer.exe", dataFolder);
                    UpdateStatus($"Opened folder: {dataFolder}");
                }
                else
                {
                    UpdateStatus("Database folder does not exist.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error opening folder: {ex.Message}");
            }
        }

        /// <summary>
        /// Check app folder status on startup
        /// </summary>
        private async Task CheckAppFolderOnStartupAsync()
        {
            var configuredPath = AppSettings.GetAppDataFolderPath();

            if (string.IsNullOrWhiteSpace(configuredPath) || !Directory.Exists(configuredPath))
            {
                // No folder configured or folder doesn't exist - disable menus and show selection dialog
                EnableMenuItems(false);
                UpdateStatus("?? Please set the application data folder to continue.");

                // Show selection dialog
                await ShowSetAppFolderDialogAsync(forceSelection: true);
            }
            else
            {
                // Folder configured - check if database exists and is valid
                var dbPath = Path.Combine(configuredPath, "golfapp.db");
                bool needsMigration = false;

                if (!File.Exists(dbPath))
                {
                    // Check if there's an old database to migrate
                    var oldDbLocation = AppSettings.GetCurrentDatabaseLocation();
                    if (!string.IsNullOrWhiteSpace(oldDbLocation) && File.Exists(oldDbLocation))
                    {
                        needsMigration = true;
                    }
                }
                else
                {
                    // Check if existing DB is too small (likely empty)
                    var fileInfo = new FileInfo(dbPath);
                    if (fileInfo.Length < 20 * 1024) // Less than 20KB
                    {
                        var oldDbLocation = AppSettings.GetCurrentDatabaseLocation();
                        if (!string.IsNullOrWhiteSpace(oldDbLocation) && 
                            File.Exists(oldDbLocation) && 
                            oldDbLocation != dbPath)
                        {
                            var oldFileInfo = new FileInfo(oldDbLocation);
                            if (oldFileInfo.Length > fileInfo.Length)
                            {
                                needsMigration = true;
                            }
                        }
                    }
                }

                if (needsMigration)
                {
                    // Offer to migrate automatically
                    await OfferAutomaticMigrationAsync(configuredPath);
                }

                // Confirm folder
                _confirmedAppFolder = configuredPath;
                _appFolderConfirmed = true;
                EnableMenuItems(true);
                UpdateStatus($"App folder: {configuredPath}");
            }
        }

        /// <summary>
        /// Show info dialog
        /// </summary>
        private async Task ShowInfoAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "OK",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot != null)
            {
                await dialog.ShowAsync();
            }
        }
    }
}
