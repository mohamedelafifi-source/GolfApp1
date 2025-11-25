using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Microsoft.Data.Sqlite;
using GolfApp1.Data;
using GolfApp1.Models;

namespace GolfApp1
{
    public sealed partial class MainWindow : Window
    {
        private Database? _db;
        private string? _dbPath;
        private readonly List<Club> _clubs = new();
        private int _index = 0;

        // Player editor state
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


     
        private void OnEditorExitClicked(object sender, RoutedEventArgs e)
        {
            EditorArea.Visibility = Visibility.Collapsed;
            UpdateStatus("Editor closed.");
        }

   
        /// </summary>
        /// <param name="message"></param>

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

        private async Task InitializeAsync()
        {
            try
            {
                var dataFolder = GetDataFolder();
                _dbPath = Path.Combine(dataFolder, "golfapp.db");
                _db = new Database(_dbPath);
                await _db.InitializeAsync();
                await LoadClubsAsync();

                _index = 0;
                ShowCurrent();
            }
            catch (Exception ex)
            {
                UpdateStatus("Initialization error: " + ex.Message);
                await ShowErrorAsync("Initialization error", ex.Message);
            }
        }

        private async Task LoadClubsAsync()
        {
            _clubs.Clear();
            if (_db is null) return;
            var list = await _db.GetAllClubsAsync();
            _clubs.AddRange(list);
        }

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
            if (_db is null) { UpdateStatus("Database not initialized."); return; }

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
                    await _db.UpsertClubAsync(existing);
                    UpdateStatus($"Saved club '{shortName}' (updated).");
                }
                else
                {
                    var club = new Club { Id = Guid.NewGuid().ToString(), ShortName = shortName, LongName = longName, NumberOfPlayers = 0 };
                    await _db.UpsertClubAsync(club);
                    _clubs.Add(club);
                    _index = _clubs.Count - 1;
                    UpdateStatus($"Created club '{shortName}'.");
                }

                await LoadClubsAsync();
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

        private async void OnUpdatePlayerClicked(object sender, RoutedEventArgs e)
        {
            if (!_inPlayerMode || _db is null) return;

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

                    var err = await _db.UpsertPlayerAsync(existing);
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

                    var err = await _db.UpsertPlayerAsync(player);
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

        private void OnExitPlayerEditorClicked(object sender, RoutedEventArgs e) => ExitPlayerMode();

       
       
        private async Task EnterPlayerModeAsync(string clubShort)
        {
            _players.Clear();
            if (_db is null) return;
            var list = await _db.GetPlayersByClubAsync(clubShort);
            _players.AddRange(list);

            _playerIndex = 0;
            _inPlayerMode = true;

            // Keep the club editor visible but make it read-only so the user still sees the current club.
            ShortNameTextBox.IsEnabled = false;
            LongNameTextBox.IsEnabled = false;

            // Disable club navigation and actions while in player mode to avoid conflicting edits.
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

            // Restore club navigation/buttons to their normal enabled state
            PrevButton.IsEnabled = _index > 0;
            NextButton.IsEnabled = _index < _clubs.Count - 1 ? true : _index < _clubs.Count; // safe restore
            AddPlayerButton.IsEnabled = true;

            // Re-evaluate save button enabled state
            ValidateNameFields();

            // reload clubs to refresh NumberOfPlayers etc.
            if (_db != null) { await LoadClubsAsync(); ShowCurrent(); }
            UpdateStatus("Returned from player editor.");
        }

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