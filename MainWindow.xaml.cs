
using GolfApp1.Data;
using GolfApp1.Models;
using GolfApp1.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace GolfApp1
{
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

            // show editor on startup
            EditorArea.Visibility = Visibility.Visible;

            _ = InitializeAsync();
        }
        private async void OnLoadFileClicked(object sender, RoutedEventArgs e)
        {
            // Create a FileOpenPicker and initialize it with the current window handle (WinUI3 desktop pattern)
            var picker = new Windows.Storage.Pickers.FileOpenPicker();

            // Initialize with window handle
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

                // Show confirmation (optional)
                await ShowErrorAsync("File Selected", $"File '{file.Name}' selected ({text.Length} bytes).");

                // Determine club short name to import into.
                // Prefer the current short name in the UI; otherwise ask the user to enter one.
                var clubShort = ShortNameTextBox.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(clubShort))
                {
                    //To make sure a club is selected before parsing 
                    // I can comment this out 
                    // Prompt the user to enter a club short name (4 chars expected)
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
                //This is the direct call without the check above
                // Hand off to the parser/import flow. The method will prompt the user to Auto Add or Review.
                await ParseAndBulkAddAsync(text, clubShort);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("File Open Failed", ex.Message);
            }
        }

        /*
        private async void OnLoadFileClicked(object sender, RoutedEventArgs e)
        {
            // Create a FileOpenPicker and initialize it with the current window handle (WinUI3 desktop pattern)
            var picker = new Windows.Storage.Pickers.FileOpenPicker();

            // Initialize with window handle
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

                // Show confirmation (optional)
                await ShowErrorAsync("File Selected", $"File '{file.Name}' selected ({text.Length} bytes).");

                // Determine club short name to import into.
                // Prefer the current short name in the UI; otherwise ask the user to enter one.
                var clubShort = ShortNameTextBox.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(clubShort))
                {
                    // Prompt the user to enter a club short name (4 chars expected)
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

                // Hand off to the parser/import flow. The method will prompt the user to Auto Add or Review.
                await ParseAndBulkAddAsync(text, clubShort);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("File Open Failed", ex.Message);
            }
        }
        */
        private void OnExitPlayerEditorClicked(object sender, RoutedEventArgs e)
        {
            // Reuse existing ExitPlayerMode logic
            ExitPlayerMode();
        }

        private void OnEditorExitClicked(object sender, RoutedEventArgs e)
        {
            // Close the editor UI and ensure any player editor is exited
            try
            {
                if (_inPlayerMode)
                {
                    ExitPlayerMode();
                }
            }
            catch
            {
                // ignore - best effort
            }

            EditorArea.Visibility = Visibility.Collapsed;
            UpdateStatus("Editor closed.");
        }
        private async Task InitializeAsync()
        {
            try
            {
                var dataFolder = GetDataFolder();
                var dbPath = Path.Combine(dataFolder, "golfapp.db");
                _db = new Database(dbPath);
                await _db.InitializeAsync();

                // Create and wire ViewModel
                _vm = new MainViewModel(_db);

                // Set DataContext on the Window root element (WinUI3 pattern)
                var root = this.Content as FrameworkElement;
                if (root is not null)
                {
                    root.DataContext = _vm;
                }

                // Load clubs into VM and local cache
                await _vm.LoadClubsAsync();
                RefreshLocalClubsFromVm();

                _index = 0;
                ShowCurrent();
            }
            catch (Exception ex)
            {
                UpdateStatus("Initialization error: " + ex.Message);
                await ShowErrorAsync("Initialization error", ex.Message);
            }
        }

        // copy VM clubs into local list used by existing UI code
        private void RefreshLocalClubsFromVm()
        {
            _clubs.Clear();
            if (_vm is null) return;
            foreach (var c in _vm.Clubs) _clubs.Add(c);
        }

        private void UpdateStatus(string message)
        {
            // StatusLabel defined in MainWindow.xaml
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

            // Keep the club editor visible but make it read-only so the user still sees the current club.
            ShortNameTextBox.IsEnabled = false;
            LongNameTextBox.IsEnabled = false;

            // Disable club navigation and actions while in player mode
            PrevButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            AddPlayerButton.IsEnabled = false;

            // Show player editor panel
            PlayerEditorPanel.Visibility = Visibility.Visible;

            ShowPlayer();
            UpdatePlayerNavigationButtons();
            UpdateStatus($"Player editor for club {clubShort}. {_players.Count} existing players.");
        }

        private async void ExitPlayerMode()
        {
            _inPlayerMode = false;
            PlayerEditorPanel.Visibility = Visibility.Collapsed;

            // Restore club editor interactivity
            ShortNameTextBox.IsEnabled = true;
            LongNameTextBox.IsEnabled = true;

            // Restore club navigation/buttons
            PrevButton.IsEnabled = _index > 0;
            NextButton.IsEnabled = _index < _clubs.Count - 1;
            AddPlayerButton.IsEnabled = true;

            ValidateNameFields();

            // Refresh clubs via VM
            if (_vm != null) { await _vm.LoadClubsAsync(); RefreshLocalClubsFromVm(); ShowCurrent(); }
            UpdateStatus("Returned from player editor.");
        }
        // Paste these methods into the MainWindow class (alongside existing helpers).
// Also ensure you have `using System.Text.RegularExpressions;` at the top of the file.



    private async Task ParseAndBulkAddAsync(string text, string clubShort)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            UpdateStatus("File is empty.");
            await ShowErrorAsync("Empty file", "The selected file contains no text.");
            return;
        }

        // Parse into Player records (semicolon-separated fields; labels end with colon)
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

            // Basic normalization: remove spaces from code
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

        // Ask user: Auto Add or Load For Review
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

    private void LoadPlayersForReview(List<Player> players, string clubShort)
    {
        // Clear existing review list and populate with parsed records
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

        // helper: show modal error and update status
        private async Task ShowErrorAsync(string title, string message)
        {
            UpdateStatus(message);
            var dlg = new ContentDialog { Title = title, Content = message, CloseButtonText = "OK", XamlRoot = this.Content?.XamlRoot };
            if (this.Content?.XamlRoot != null) await dlg.ShowAsync();
        }

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