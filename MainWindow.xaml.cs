using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
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

        private void OnClubHandlingClicked(object sender, RoutedEventArgs e)
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
    }
}
