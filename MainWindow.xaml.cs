//MainWindow.xaml.cs
using GolfApp1.Data;
using GolfApp1.Models;
using GolfApp1.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace GolfApp1
{
    // NOTE: class name must match the x:Class in MainWindow.xaml
    public sealed partial class MainWindow : Window
    {
        private Database? _db;
        private MainViewModel? _vm;

        private readonly List<Club> _clubs = new();
        private int _index = 0;

        // Player editor state (kept for UI navigation; populated from VM)
        private readonly List<Player> _players = new();
        private int _playerIndex = 0;
        private bool _inPlayerMode = false;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "GolfApp1";

            // Do NOT show editor on startup — show menus instead.
            EditorArea.Visibility = Visibility.Collapsed;

            _ = InitializeAsync();
        }

        // ---------------- File / Bulk import entry ----------------

        private async void OnLoadFileClicked(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Clear();
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".csv");

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                UpdateStatus("File open cancelled.");
                return;
            }

            UpdateStatus($"Selected file: {file.Name}");

            try
            {
                var text = await Windows.Storage.FileIO.ReadTextAsync(file);

                // Show confirmation (optional) — shared ShowErrorAsync is implemented in Helpers partial.
                await ShowErrorAsync("File Selected", $"File '{file.Name}' selected ({text.Length} bytes).");

                // Determine club short name to import into.
                var clubShort = ShortNameTextBox.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(clubShort))
                {
                    var inputBox = new TextBox { PlaceholderText = "Enter 4-char club short name" };
                    var dlg = new ContentDialog
                    {
                        Title = "Select Club",
                        Content = new StackPanel
                        {
                            Children =
                            {
                                new TextBlock { Text = "No club selected in the editor. Enter the club short name to import into:", TextWrapping = TextWrapping.Wrap },
                                inputBox
                            },
                            Spacing = 8
                        },
                        PrimaryButtonText = "OK",
                        CloseButtonText = "Cancel",
                        XamlRoot = this.Content?.XamlRoot
                    };

                    var result = ContentDialogResult.None;
                    if (this.Content?.XamlRoot != null) result = await dlg.ShowAsync();
                    if (result != ContentDialogResult.Primary)
                    {
                        UpdateStatus("Import cancelled (no club selected).");
                        return;
                    }

                    clubShort = inputBox.Text?.Trim() ?? string.Empty;
                    if (clubShort.Length != 4)
                    {
                        UpdateStatus("Club short name must be exactly 4 characters.");
                        await ShowErrorAsync("Invalid Club", "Club short name must be exactly 4 characters (e.g. ABCD).");
                        return;
                    }
                }

                // Hand off to the shared parser/import flow (Helpers partial provides ParseAndBulkAddAsync).
                await ParseAndBulkAddAsync(text, clubShort);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("File Open Failed", ex.Message);
            }
        }

        // ---------------- Initialization ----------------

        private void OnAddPlayerSaveClicked(object sender, RoutedEventArgs e)
        {
            // Reuse existing Add/Update logic implemented in OnUpdatePlayerClicked
            OnUpdatePlayerClicked(sender, e);
        }
        private async Task InitializeAsync()
        {
            try
            {
                var dataFolder = GetDataFolder();
                var dbPath = Path.Combine(dataFolder, "golfapp.db");
                _db = new Database(dbPath);
                await _db.InitializeAsync();

                _vm = new MainViewModel(_db);

                var root = this.Content as FrameworkElement;
                if (root is not null)
                {
                    root.DataContext = _vm;
                }

                await _vm.LoadClubsAsync();
                RefreshLocalClubsFromVm();

                _index = 0;
                if (EditorArea.Visibility == Visibility.Visible)
                {
                    ShowCurrent();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Initialization error: " + ex.Message);
                await ShowErrorAsync("Initialization error", ex.Message);
            }
        }

        private void RefreshLocalClubsFromVm()
        {
            _clubs.Clear();
            if (_vm is null) return;
            foreach (var c in _vm.Clubs) _clubs.Add(c);
        }

        private void UpdateStatus(string message)
        {
            StatusLabel.Text = message;
        }

        private static string GetDataFolder()
        {
            try
            {
                var appData = ApplicationData.Current;
                var path = appData?.LocalFolder?.Path;
                if (!string.IsNullOrWhiteSpace(path)) return path;
            }
            catch { /* may fail in unpackaged scenarios */ }

            return AppStorage.GetDataFolder();
        }

        // ---------------- Club UI helpers ----------------

        private void ShowCurrent()
        {
            var total = _clubs.Count + 1;
            var shown = Math.Min(Math.Max(_index + 1, 1), total);
            ShownLabel.Text = $"{shown}/{total}";

            if (_index < _clubs.Count)
            {
                var c = _clubs[_index];
                ShortNameTextBox.Text = c.ShortName;
                LongNameTextBox.Text = c.LongName;
                SaveButton.Content = "Save";
                UpdateStatus($"Editing club {_index + 1} of {total}.");
            }
            else
            {
                ShortNameTextBox.Text = string.Empty;
                LongNameTextBox.Text = string.Empty;
                SaveButton.Content = "Create";
                UpdateStatus("Creating new club.");
            }

            PrevButton.IsEnabled = _index > 0;
            NextButton.IsEnabled = _index < total - 1;

            ValidateNameFields();
        }

        private void OnFileNewClicked(object sender, RoutedEventArgs e)
        {
            _index = _clubs.Count;
            EditorArea.Visibility = Visibility.Visible;
            ShowCurrent();
        }

        private void OnFileOpenClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Action: Create New Players - will be implemented next.");
        }

        private void OnFileExitClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Action: Exiting Application...");
            this.Close();
        }

        private void OnEditSettingsClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Action: Edit -> Settings was clicked.");
        }

        private void OnPrevClicked(object sender, RoutedEventArgs e) { if (_index > 0) { _index--; ShowCurrent(); } }
        private void OnNextClicked(object sender, RoutedEventArgs e) { var total = _clubs.Count + 1; if (_index < total - 1) { _index++; ShowCurrent(); } }

        private void OnNameFieldsTextChanged(object sender, TextChangedEventArgs e) => ValidateNameFields();

        private void ValidateNameFields()
        {
            var shortName = ShortNameTextBox.Text?.Trim() ?? string.Empty;
            var longName = LongNameTextBox.Text?.Trim() ?? string.Empty;
            SaveButton.IsEnabled = shortName.Length == 4 && longName.Length >= 1 && longName.Length <= 20;
        }

        private async void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            if (_vm is null) { UpdateStatus("Database not initialized."); return; }

            var shortName = ShortNameTextBox.Text?.Trim() ?? string.Empty;
            var longName = LongNameTextBox.Text?.Trim() ?? string.Empty;

            if (shortName.Length != 4) { UpdateStatus("Short name must be exactly 4 characters."); return; }
            if (longName.Length == 0 || longName.Length > 20) { UpdateStatus("Long name must be 1..20 characters."); return; }

            try
            {
                if (_index < _clubs.Count)
                {
                    var existing = _clubs[_index];
                    existing.ShortName = shortName;
                    existing.LongName = longName;
                    await _vm.UpsertClubAsync(existing);
                    UpdateStatus($"Saved club '{shortName}' (updated).");
                }
                else
                {
                    var club = new Club { Id = Guid.NewGuid().ToString(), ShortName = shortName, LongName = longName, NumberOfPlayers = 0 };
                    await _vm.UpsertClubAsync(club);
                    _clubs.Add(club);
                    _index = _clubs.Count - 1;
                    UpdateStatus($"Created club '{shortName}'.");
                }

                await _vm.LoadClubsAsync();
                RefreshLocalClubsFromVm();
                ShowCurrent();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                var msg = ex.Message.Contains("ShortName") ? "Short name must be unique." :
                          ex.Message.Contains("LongName") ? "Long name must be unique." :
                          "Constraint violation: " + ex.Message;
                UpdateStatus($"Save failed: {msg}");
                await ShowErrorAsync("Save Error", msg);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Save failed: {ex.Message}");
                await ShowErrorAsync("Save Error", ex.Message);
            }
        }

        // Delete Club handler (with confirmation)
        private async void OnDeleteClubClicked(object sender, RoutedEventArgs e)
        {
            if (_db is null || _vm is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Delete Club", "Database not initialized.");
                return;
            }

            var shortName = ShortNameTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(shortName))
            {
                UpdateStatus("No club selected to delete.");
                await ShowErrorAsync("Delete Club", "No club selected to delete.");
                return;
            }

            var confirm = new ContentDialog
            {
                Title = $"Delete club '{shortName}'?",
                Content = $"This will permanently delete the club '{shortName}'. This action cannot be undone. If the club still has players you must remove them first.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            var res = ContentDialogResult.None;
            if (this.Content?.XamlRoot != null) res = await confirm.ShowAsync();
            if (res != ContentDialogResult.Primary)
            {
                UpdateStatus("Delete cancelled.");
                return;
            }

            try
            {
                var err = await _db.DeleteClubAsync(shortName);
                if (err != null)
                {
                    UpdateStatus("Delete failed: " + err);
                    await ShowErrorAsync("Delete Club Failed", err);
                    return;
                }

                await _vm.LoadClubsAsync();
                RefreshLocalClubsFromVm();
                _index = Math.Min(_index, Math.Max(0, _clubs.Count - 1));
                ShowCurrent();
                UpdateStatus($"Club '{shortName}' deleted.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Delete club failed: " + ex.Message);
                await ShowErrorAsync("Delete club failed", ex.Message);
            }
        }

        // ---------------- Player editing ----------------

        private async void OnAddPlayerClicked(object sender, RoutedEventArgs e)
        {
            if (_db is null) { UpdateStatus("Database not initialized."); return; }
            if (_index >= _clubs.Count) { UpdateStatus("Please save the club before adding players."); return; }

            var clubShort = ShortNameTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(clubShort)) { UpdateStatus("Club short name missing."); return; }

            await EnterPlayerModeAsync(clubShort);
        }

        private void ShowPlayer()
        {
            if (!_inPlayerMode) return;

            if (_playerIndex < _players.Count)
            {
                var p = _players[_playerIndex];
                PlayerCodeTextBox.Text = p.Code;
                PlayerNameTextBox.Text = p.Name;
                PlayerIndexTextBox.Text = p.IndexValue;
                PlayerNoteTextBox.Text = p.Note;
                UpdatePlayerButton.Content = "Update";
            }
            else
            {
                PlayerCodeTextBox.Text = string.Empty;
                PlayerNameTextBox.Text = string.Empty;
                PlayerIndexTextBox.Text = string.Empty;
                PlayerNoteTextBox.Text = string.Empty;
                UpdatePlayerButton.Content = "Add";
            }

            UpdatePlayerNavigationButtons();
        }

        private void UpdatePlayerNavigationButtons()
        {
            PrevPlayerButton.IsEnabled = _playerIndex > 0;
            NextPlayerButton.IsEnabled = _playerIndex < _players.Count;
        }

        private void OnPrevPlayerClicked(object sender, RoutedEventArgs e)
        {
            if (!_inPlayerMode) return;
            if (_playerIndex > 0) { _playerIndex--; ShowPlayer(); }
        }

        private void OnNextPlayerClicked(object sender, RoutedEventArgs e)
        {
            if (!_inPlayerMode) return;
            var maxIndex = _players.Count;
            if (_playerIndex < maxIndex) { _playerIndex++; ShowPlayer(); }
        }

        private async Task EnterPlayerModeAsync(string clubShort)
        {
            if (_vm is null || _db is null) return;

            _players.Clear();
            await _vm.LoadPlayersAsync(clubShort);
            foreach (var p in _vm.Players) _players.Add(p);

            _playerIndex = 0;
            _inPlayerMode = true;

            ShortNameTextBox.IsEnabled = false;
            LongNameTextBox.IsEnabled = false;

            PrevButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            AddPlayerButton.IsEnabled = false;

            PlayerEditorPanel.Visibility = Visibility.Visible;

            ShowPlayer();
            UpdatePlayerNavigationButtons();
            UpdateStatus($"Player editor for club {clubShort}. {_players.Count} existing players.");
        }

        private async void ExitPlayerMode()
        {
            _inPlayerMode = false;
            PlayerEditorPanel.Visibility = Visibility.Collapsed;

            ShortNameTextBox.IsEnabled = true;
            LongNameTextBox.IsEnabled = true;

            PrevButton.IsEnabled = _index > 0;
            NextButton.IsEnabled = _index < _clubs.Count - 1;
            AddPlayerButton.IsEnabled = true;

            ValidateNameFields();

            if (_vm != null) { await _vm.LoadClubsAsync(); RefreshLocalClubsFromVm(); ShowCurrent(); }
            UpdateStatus("Returned from player editor.");
        }

        // Called by 'Update' / 'Add' UI button — validates and inserts/updates player
        private async void OnUpdatePlayerClicked(object sender, RoutedEventArgs e)
        {
            if (!_inPlayerMode || _vm is null) return;

            var clubShort = ShortNameTextBox.Text?.Trim() ?? string.Empty;
            var code = PlayerCodeTextBox.Text?.Trim() ?? string.Empty;
            var name = PlayerNameTextBox.Text?.Trim() ?? string.Empty;
            var idx = PlayerIndexTextBox.Text?.Trim() ?? string.Empty;
            var note = PlayerNoteTextBox.Text?.Trim() ?? string.Empty;

            if (code.Length != 6 || !int.TryParse(code, out _))
            {
                UpdateStatus("Code must be 6 digits.");
                await ShowErrorAsync("Invalid Code", "Code must be exactly 6 digits. Example: 012345");
                return;
            }
            if (string.IsNullOrEmpty(name) || name.Length > 20)
            {
                UpdateStatus("Player name must be 1..20 chars.");
                await ShowErrorAsync("Invalid Name", "Player name must be 1..20 characters.");
                return;
            }

            try
            {
                if (_playerIndex < _players.Count)
                {
                    var existing = _players[_playerIndex];
                    existing.Code = code;
                    existing.Name = name;
                    existing.IndexValue = idx;
                    existing.Note = note;

                    var err = await _vm.UpsertPlayerAsync(existing);
                    if (err != null)
                    {
                        var friendly = MapDbErrorToUserMessage(err, existing.Code);
                        UpdateStatus("Update failed: " + friendly);
                        await ShowErrorAsync("Update Failed", friendly);
                        return;
                    }
                    UpdateStatus($"Player '{name}' updated.");
                }
                else
                {
                    if (_players.Exists(p => p.Code == code))
                    {
                        var msg = $"The player code '{code}' already exists in this club. It must be unique.";
                        UpdateStatus("Add failed: " + msg);
                        await ShowErrorAsync("Add Failed", msg);
                        return;
                    }

                    var player = new Player
                    {
                        Id = Guid.NewGuid().ToString(),
                        ClubShortName = clubShort,
                        Code = code,
                        Name = name,
                        IndexValue = idx,
                        Note = note
                    };

                    var err = await _vm.UpsertPlayerAsync(player);
                    if (err != null)
                    {
                        var friendly = MapDbErrorToUserMessage(err, code);
                        UpdateStatus("Add failed: " + friendly);
                        await ShowErrorAsync("Add Failed", friendly);
                        return;
                    }

                    _players.Add(player);
                    _playerIndex = _players.Count - 1;
                    UpdateStatus($"Player '{name}' added.");
                }

                ShowPlayer();
            }
            catch (Exception ex)
            {
                UpdateStatus("Save player failed: " + ex.Message);
                await ShowErrorAsync("Save player failed", ex.Message);
            }
        }

        // Editor exit handlers wired in XAML
        private void OnExitPlayerEditorClicked(object sender, RoutedEventArgs e)
        {
            ExitPlayerMode();
        }

        private void OnEditorExitClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_inPlayerMode)
                {
                    ExitPlayerMode();
                }
            }
            catch { /* ignore */ }

            EditorArea.Visibility = Visibility.Collapsed;
            UpdateStatus("Editor closed.");
        }
       

        private async void OnDeletePlayerClicked(object sender, RoutedEventArgs e)
        {
            if (!_inPlayerMode || _vm is null || _db is null)
            {
                UpdateStatus("Player editor not active or database not initialized.");
                await ShowErrorAsync("Delete Player", "Player editor not active or database not initialized.");
                return;
            }

            if (_playerIndex < 0 || _playerIndex >= _players.Count)
            {
                UpdateStatus("No player selected to delete.");
                await ShowErrorAsync("Delete Player", "No player selected to delete.");
                return;
            }

            var player = _players[_playerIndex];

            // Check for dependent results and require explicit confirmation if any exist.
            int resultsCount = 0;
            try
            {
                resultsCount = await _db.GetResultsCountByPlayerIdAsync(player.Id);
            }
            catch
            {
                // non-fatal: treat as unknown / proceed with conservative confirmation
                resultsCount = -1;
            }

            string content;
            if (resultsCount > 0)
            {
                content = $"Player '{player.Name}' (code: {player.Code}) has {resultsCount} result(s) recorded. Deleting the player will also remove those result(s). Are you sure you want to delete the player and all their results?";
            }
            else if (resultsCount == 0)
            {
                content = $"This will permanently delete player '{player.Name}' (code: {player.Code}). This action cannot be undone and will update the club's player count.";
            }
            else
            {
                // resultsCount == -1 (error reading), ask conservative confirmation
                content = $"This will permanently delete player '{player.Name}' (code: {player.Code}). There was an error checking for related results; deletion may fail if dependent results exist. Continue?";
            }

            var confirm = new ContentDialog
            {
                Title = resultsCount > 0 ? $"Delete player and {(resultsCount > 0 ? resultsCount.ToString() : "their")} result(s)?" : $"Delete player '{player.Name}'?",
                Content = content,
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            var res = ContentDialogResult.None;
            if (this.Content?.XamlRoot != null) res = await confirm.ShowAsync();
            if (res != ContentDialogResult.Primary)
            {
                UpdateStatus("Delete cancelled.");
                return;
            }

            try
            {
                var err = await _db.DeletePlayerAsync(player.Id);
                if (err != null)
                {
                    UpdateStatus("Delete failed: " + err);
                    await ShowErrorAsync("Delete Player Failed", err);
                    return;
                }

                _players.RemoveAt(_playerIndex);
                if (_playerIndex >= _players.Count) _playerIndex = Math.Max(0, _players.Count - 1);
                ShowPlayer();

                await _vm.LoadClubsAsync();
                RefreshLocalClubsFromVm();

                var clubShort = ShortNameTextBox.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(clubShort))
                {
                    await _vm.LoadPlayersAsync(clubShort);
                    _players.Clear();
                    foreach (var p in _vm.Players) _players.Add(p);
                    _playerIndex = Math.Min(_playerIndex, Math.Max(0, _players.Count - 1));
                    ShowPlayer();
                }

                UpdateStatus($"Player '{player.Name}' deleted.");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                // Constraint violation (likely FK: player has dependent results) — ask to cascade delete
                UpdateStatus("Delete failed due to constraint (player may have results).");

                var dlg = new ContentDialog
                {
                    Title = "Delete player failed",
                    Content = "This player has dependent results in the database. You can either remove those results first or delete the player and all their results now. Delete all results and the player?",
                    PrimaryButtonText = "Delete results + player",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content?.XamlRoot
                };

                var confirmCascade = ContentDialogResult.None;
                if (this.Content?.XamlRoot != null) confirmCascade = await dlg.ShowAsync();
                if (confirmCascade != ContentDialogResult.Primary)
                {
                    await ShowErrorAsync("Delete Player", "Player not deleted. Remove related results first to allow deletion.");
                    return;
                }

                try
                {
                    var delErr = await _db.DeleteResultsByPlayerIdAsync(player.Id);
                    if (!string.IsNullOrEmpty(delErr))
                    {
                        UpdateStatus("Failed to delete related results: " + delErr);
                        await ShowErrorAsync("Delete Results Failed", delErr);
                        return;
                    }

                    // retry deleting the player
                    var err2 = await _db.DeletePlayerAsync(player.Id);
                    if (err2 != null)
                    {
                        UpdateStatus("Delete failed after removing results: " + err2);
                        await ShowErrorAsync("Delete Player Failed", err2);
                        return;
                    }

                    // success — update UI
                    _players.RemoveAt(_playerIndex);
                    if (_playerIndex >= _players.Count) _playerIndex = Math.Max(0, _players.Count - 1);
                    ShowPlayer();

                    await _vm.LoadClubsAsync();
                    RefreshLocalClubsFromVm();

                    var clubShort2 = ShortNameTextBox.Text?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(clubShort2))
                    {
                        await _vm.LoadPlayersAsync(clubShort2);
                        _players.Clear();
                        foreach (var p in _vm.Players) _players.Add(p);
                        _playerIndex = Math.Min(_playerIndex, Math.Max(0, _players.Count - 1));
                        ShowPlayer();
                    }

                    UpdateStatus($"Player '{player.Name}' and related results deleted.");
                }
                catch (Exception ex2)
                {
                    UpdateStatus("Cascade delete failed: " + ex2.Message);
                    await ShowErrorAsync("Delete Player", "Cascade delete failed: " + ex2.Message);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Delete player failed: " + ex.Message);
                await ShowErrorAsync("Delete player failed", ex.Message);
            }
        }

        /*
        //Start of replace
        // Delete Player handler (with confirmation) - enhanced to handle FK constraint by offering cascade delete
        private async void OnDeletePlayerClicked(object sender, RoutedEventArgs e)
        {
            if (!_inPlayerMode || _vm is null || _db is null)
            {
                UpdateStatus("Player editor not active or database not initialized.");
                await ShowErrorAsync("Delete Player", "Player editor not active or database not initialized.");
                return;
            }

            if (_playerIndex < 0 || _playerIndex >= _players.Count)
            {
                UpdateStatus("No player selected to delete.");
                await ShowErrorAsync("Delete Player", "No player selected to delete.");
                return;
            }

            var player = _players[_playerIndex];
            var confirm = new ContentDialog
            {
                Title = $"Delete player '{player.Name}'?",
                Content = $"This will permanently delete player '{player.Name}' (code: {player.Code}). This action cannot be undone and will update the club's player count.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            var res = ContentDialogResult.None;
            if (this.Content?.XamlRoot != null) res = await confirm.ShowAsync();
            if (res != ContentDialogResult.Primary)
            {
                UpdateStatus("Delete cancelled.");
                return;
            }

            try
            {
                var err = await _db.DeletePlayerAsync(player.Id);
                if (err != null)
                {
                    UpdateStatus("Delete failed: " + err);
                    await ShowErrorAsync("Delete Player Failed", err);
                    return;
                }

                _players.RemoveAt(_playerIndex);
                if (_playerIndex >= _players.Count) _playerIndex = Math.Max(0, _players.Count - 1);
                ShowPlayer();

                await _vm.LoadClubsAsync();
                RefreshLocalClubsFromVm();

                var clubShort = ShortNameTextBox.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(clubShort))
                {
                    await _vm.LoadPlayersAsync(clubShort);
                    _players.Clear();
                    foreach (var p in _vm.Players) _players.Add(p);
                    _playerIndex = Math.Min(_playerIndex, Math.Max(0, _players.Count - 1));
                    ShowPlayer();
                }

                UpdateStatus($"Player '{player.Name}' deleted.");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                // Constraint violation (likely FK: player has dependent results)
                UpdateStatus("Delete failed due to constraint (player may have results).");

                var dlg = new ContentDialog
                {
                    Title = "Delete player failed",
                    Content = "This player has dependent results in the database. You can either remove those results first or delete the player and all their results now. Delete all results and the player?",
                    PrimaryButtonText = "Delete results + player",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content?.XamlRoot
                };

                var confirmCascade = ContentDialogResult.None;
                if (this.Content?.XamlRoot != null) confirmCascade = await dlg.ShowAsync();
                if (confirmCascade != ContentDialogResult.Primary)
                {
                    await ShowErrorAsync("Delete Player", "Player not deleted. Remove related results first to allow deletion.");
                    return;
                }

                try
                {
                    // Try to find a DB helper to delete related results (matches common naming)
                    MethodInfo? delResultsMethod = null;
                    var dbType = _db.GetType();
                    var candidateMethods = dbType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var m in candidateMethods)
                    {
                        if (m.Name.IndexOf("DeleteResult", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            m.GetParameters().Length == 1 &&
                            m.GetParameters()[0].ParameterType == typeof(string))
                        {
                            delResultsMethod = m;
                            break;
                        }
                    }

                    if (delResultsMethod != null)
                    {
                        var taskObj = delResultsMethod.Invoke(_db, new object[] { player.Id });
                        if (taskObj is Task task)
                        {
                            await task.ConfigureAwait(false);

                            // If Task<TResult> returned a string error, extract it
                            string? delErr = null;
                            var ttype = task.GetType();
                            if (ttype.IsGenericType)
                            {
                                var resultProp = ttype.GetProperty("Result");
                                var resultVal = resultProp?.GetValue(task);
                                delErr = resultVal as string;
                            }

                            if (!string.IsNullOrEmpty(delErr))
                            {
                                UpdateStatus("Failed to delete related results: " + delErr);
                                await ShowErrorAsync("Delete Results Failed", delErr);
                                return;
                            }
                        }
                        else
                        {
                            var info = "Database helper found but did not return a Task. Remove results manually or update the Database helper.";
                            UpdateStatus(info);
                            await ShowErrorAsync("Delete Player", info);
                            return;
                        }
                    }
                    else
                    {
                        var info = "Database layer does not expose a DeleteResults... helper that takes a player Id. Remove results manually or add such a helper to Database.";
                        UpdateStatus(info);
                        await ShowErrorAsync("Delete Player", info);
                        return;
                    }

                    // retry deleting the player
                    var err2 = await _db.DeletePlayerAsync(player.Id);
                    if (err2 != null)
                    {
                        UpdateStatus("Delete failed after removing results: " + err2);
                        await ShowErrorAsync("Delete Player Failed", err2);
                        return;
                    }

                    // success — update UI as above
                    _players.RemoveAt(_playerIndex);
                    if (_playerIndex >= _players.Count) _playerIndex = Math.Max(0, _players.Count - 1);
                    ShowPlayer();

                    await _vm.LoadClubsAsync();
                    RefreshLocalClubsFromVm();

                    var clubShort2 = ShortNameTextBox.Text?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(clubShort2))
                    {
                        await _vm.LoadPlayersAsync(clubShort2);
                        _players.Clear();
                        foreach (var p in _vm.Players) _players.Add(p);
                        _playerIndex = Math.Min(_playerIndex, Math.Max(0, _players.Count - 1));
                        ShowPlayer();
                    }

                    UpdateStatus($"Player '{player.Name}' and related results deleted.");
                }
                catch (Exception ex2)
                {
                    UpdateStatus("Cascade delete failed: " + ex2.Message);
                    await ShowErrorAsync("Delete Player", "Cascade delete failed: " + ex2.Message);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Delete player failed: " + ex.Message);
                await ShowErrorAsync("Delete player failed", ex.Message);

                
            }
        }
        // End of replace
        */
        private static string MapDbErrorToUserMessage(string dbError, string code)
        {
            if (string.IsNullOrEmpty(dbError)) return "A database error occurred.";
            var lower = dbError.ToLowerInvariant();
            if (lower.Contains("unique") || lower.Contains("constraint") || lower.Contains("code"))
            {
                return $"This player code '{code}' already exists. It must be unique.";
            }
            if (lower.Contains("sqlite") && lower.Contains("19"))
            {
                return "A uniqueness constraint failed in the database. The value must be unique.";
            }
            return dbError;
        }
    }
}