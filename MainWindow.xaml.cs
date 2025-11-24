using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Windows.Storage;
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
            catch { /* Activation may fail in unpackaged scenarios */ }

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
                var msg = ex.Message.Contains("ShortName") ? "Short name must be unique." : ex.Message.Contains("LongName") ? "Long name must be unique." : "Constraint violation: " + ex.Message;
                UpdateStatus($"Save failed: {msg}");
                var errDlg = new ContentDialog { Title = "Save Error", Content = msg, CloseButtonText = "OK", XamlRoot = this.Content?.XamlRoot };
                if (this.Content?.XamlRoot != null) await errDlg.ShowAsync();
            }
            catch (Exception ex) { UpdateStatus($"Save failed: {ex.Message}"); }
        }

        private void OnEditorExitClicked(object sender, RoutedEventArgs e)
        {
            EditorArea.Visibility = Visibility.Collapsed;
            UpdateStatus("Editor closed.");
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

        private async Task EnterPlayerModeAsync(string clubShort)
        {
            _players.Clear();
            if (_db is null) return;
            var list = await _db.GetPlayersByClubAsync(clubShort);
            _players.AddRange(list);

            _playerIndex = 0;
            _inPlayerMode = true;

            // Toggle UI: hide club editor panels, show player editor panel (must exist in XAML)
            ClubEditorPanel.Visibility = Visibility.Collapsed;
            ClubButtonsPanel.Visibility = Visibility.Collapsed;
            PlayerEditorPanel.Visibility = Visibility.Visible;

            ShowPlayer();
            UpdatePlayerNavigationButtons();
            UpdateStatus($"Player editor for club {clubShort}. {_players.Count} existing players.");
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
                PlayerCodeTextBox.IsEnabled = false;
            }
            else
            {
                PlayerCodeTextBox.Text = string.Empty;
                PlayerNameTextBox.Text = string.Empty;
                PlayerIndexTextBox.Text = string.Empty;
                PlayerNoteTextBox.Text = string.Empty;
                UpdatePlayerButton.Content = "Add";
                PlayerCodeTextBox.IsEnabled = true;
            }

            UpdatePlayerNavigationButtons();
            var total = _players.Count + 1;
            UpdateStatus($"Player {_playerIndex + 1}/{total}");
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
                return;
            }
            if (string.IsNullOrEmpty(name) || name.Length > 20)
            {
                UpdateStatus("Player name must be 1..20 chars.");
                return;
            }

            try
            {
                if (_playerIndex < _players.Count)
                {
                    var existing = _players[_playerIndex];
                    existing.Name = name;
                    existing.IndexValue = idx;
                    existing.Note = note;

                    var err = await _db.UpsertPlayerAsync(existing);
                    if (err != null) { UpdateStatus("Update failed: " + err); return; }
                    UpdateStatus($"Player '{name}' updated.");
                }
                else
                {
                    if (_players.Exists(p => p.Code == code))
                    {
                        UpdateStatus($"Add failed: Player code '{code}' already exists in this club.");
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
                    if (err != null) { UpdateStatus("Add failed: " + err); return; }

                    _players.Add(player);
                    _playerIndex = _players.Count - 1;
                    UpdateStatus($"Player '{name}' added.");
                }

                ShowPlayer();
            }
            catch (Exception ex)
            {
                UpdateStatus("Save player failed: " + ex.Message);
            }
        }

        private void OnExitPlayerEditorClicked(object sender, RoutedEventArgs e) => ExitPlayerMode();

        private async void ExitPlayerMode()
        {
            _inPlayerMode = false;
            PlayerEditorPanel.Visibility = Visibility.Collapsed;
            ClubEditorPanel.Visibility = Visibility.Visible;
            ClubButtonsPanel.Visibility = Visibility.Visible;

            if (_db != null) { await LoadClubsAsync(); ShowCurrent(); }
            UpdateStatus("Returned from player editor.");
        }
    }
}