using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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

        // Helper: prefer ApplicationData when available, otherwise use AppStorage and ensure folder exists.
        private static string GetDataFolder()
        {
            try
            {
                var appData = Windows.Storage.ApplicationData.Current;
                var path = appData?.LocalFolder?.Path;
                if (!string.IsNullOrWhiteSpace(path)) return path;
            }
            catch { /* Activation may fail in unpackaged scenarios */ }

            // NOTE: AppStorage is not defined in this file. Assuming it's in GolfApp1.Data namespace or a helper class.
            // If AppStorage is an error, you must define it or replace it with a system-specific fallback path.
            var fallback = AppStorage.GetDataFolder();
            return fallback;
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

        // --- Menu Item Handlers (Required by XAML) ---

        // ERROR FIXED: Removed duplicate declaration. OnFileNewClicked kept.
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

        // --- Navigation and Validation Handlers ---

        private void OnPrevClicked(object sender, RoutedEventArgs e) { if (_index > 0) { _index--; ShowCurrent(); } }
        private void OnNextClicked(object sender, RoutedEventArgs e) { var total = _clubs.Count + 1; if (_index < total - 1) { _index++; ShowCurrent(); } }

        private void OnNameFieldsTextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateNameFields();
        }

        private void ValidateNameFields()
        {
            var shortName = ShortNameTextBox.Text?.Trim() ?? string.Empty;
            var longName = LongNameTextBox.Text?.Trim() ?? string.Empty;
            SaveButton.IsEnabled = shortName.Length == 4 && longName.Length >= 1 && longName.Length <= 20;
        }

        // --- CRUD Operations ---

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

        private async void OnAddPlayerClicked(object sender, RoutedEventArgs e)
        {
            // Use current club short name
            var clubShort = ShortNameTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(clubShort))
            {
                UpdateStatus("Set club short name before adding players.");
                return;
            }

            // Build dialog fields
            var codeBox = new TextBox { PlaceholderText = "6-digit code", MaxLength = 6 };
            var nameBox = new TextBox { PlaceholderText = "Player name (<=20)", MaxLength = 20 };
            var indexBox = new TextBox { PlaceholderText = "Index (e.g. 12.3)", MaxLength = 5 };
            var noteBox = new TextBox { PlaceholderText = "Note (<=20)", MaxLength = 20 };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Club short name:" });
            panel.Children.Add(new TextBlock { Text = clubShort, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = "6-digit unique code:" });
            panel.Children.Add(codeBox);
            panel.Children.Add(new TextBlock { Text = "Player name (<=20, unique):" });
            panel.Children.Add(nameBox);
            panel.Children.Add(new TextBlock { Text = "Index (xx.x):" });
            panel.Children.Add(indexBox);
            panel.Children.Add(new TextBlock { Text = "Note (<=20):" });
            panel.Children.Add(noteBox);

            var dialog = new ContentDialog
            {
                Title = "Add Player",
                Content = panel,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) { UpdateStatus("Add Player canceled."); return; }

            // Validate
            var code = codeBox.Text?.Trim() ?? string.Empty;
            var name = nameBox.Text?.Trim() ?? string.Empty;
            var idx = indexBox.Text?.Trim() ?? string.Empty;
            var note = noteBox.Text?.Trim() ?? string.Empty;

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

            var player = new Player
            {
                Id = Guid.NewGuid().ToString(),
                ClubShortName = clubShort,
                Code = code,
                Name = name,
                IndexValue = idx,
                Note = note
            };

            if (_db is null) { UpdateStatus("Database not initialized."); return; }

            var (success, error) = await _db.InsertPlayerAsync(player);
            if (!success)
            {
                UpdateStatus("Add player failed: " + error);
                return;
            }

            UpdateStatus($"Added player '{name}' to club {clubShort}.");
        }
    }
}