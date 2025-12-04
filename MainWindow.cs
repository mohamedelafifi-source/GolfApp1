//MainWindow.EnterResults.cs
//=============================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GolfApp1.Models;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        private readonly List<ResultRecord> _resultBuffer = new();
        private int _resultIndex = -1;
        
        private void OnProceedResultsClicked(object sender, RoutedEventArgs e)
        {
            // Validate header
            if (ResultsClubCombo.SelectedItem is null)
            {
                UpdateStatus("Select a club before proceeding.");
                return;
            }

            if (ResultsVenueCombo.SelectedItem is null)
            {
                UpdateStatus("Select a venue before proceeding.");
                return;
            }

            // Prepare player combos
            var clubShort = ResultsClubCombo.SelectedItem.ToString() ?? string.Empty;
            _ = LoadPlayersForResultsAsync(clubShort).ContinueWith(_ => { }, TaskScheduler.FromCurrentSynchronizationContext());

            // initialize buffer
            _resultBuffer.Clear();
            _resultIndex = -1;

            ResultsEntryPanel.Visibility = Visibility.Visible;
            UpdateStatus("Enter player results. Use Next/Prev to navigate, Update to save entry.");
        }

        private async Task LoadPlayersForResultsAsync(string clubShort)
        {
            if (_vm is null) return;
            await _vm.LoadPlayersAsync(clubShort);
            var playerNames = _vm.Players.Select(p => p.Name).ToList();
            PlayerNameCombo.ItemsSource = playerNames;
            PartnerCombo.ItemsSource = playerNames;
        }

        // Navigation / CRUD for results buffer
        private void OnPrevResultClicked(object sender, RoutedEventArgs e)
        {
            if (_resultBuffer.Count == 0) return;
            if (_resultIndex > 0) _resultIndex--;
            PopulateResultFields();
        }

        private void OnNextResultClicked(object sender, RoutedEventArgs e)
        {
            if (_resultBuffer.Count == 0)
            {
                // start a new entry
                _resultIndex = _resultBuffer.Count;
                _resultBuffer.Add(CreateEmptyResultFromHeader());
            }
            else if (_resultIndex < _resultBuffer.Count - 1)
            {
                _resultIndex++;
            }
            else
            {
                // append new blank entry
                _resultIndex = _resultBuffer.Count;
                _resultBuffer.Add(CreateEmptyResultFromHeader());
            }

            PopulateResultFields();
        }

        private void OnUpdateResultClicked(object sender, RoutedEventArgs e)
        {
            if (!TryBuildCurrentEntry(out var rec, out var error))
            {
                UpdateStatus(error);
                return;
            }

            if (_resultIndex >= 0 && _resultIndex < _resultBuffer.Count)
            {
                _resultBuffer[_resultIndex] = rec;
            }
            else
            {
                _resultBuffer.Add(rec);
                _resultIndex = _resultBuffer.Count - 1;
            }

            // persist append to CSV
            try
            {
                SaveResultRecordToCsv(rec);
                UpdateStatus($"Saved result for '{rec.PlayerName}'.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Save failed: " + ex.Message);
            }
        }

        private void OnDeleteResultClicked(object sender, RoutedEventArgs e)
        {
            if (_resultIndex >= 0 && _resultIndex < _resultBuffer.Count)
            {
                _resultBuffer.RemoveAt(_resultIndex);
                if (_resultBuffer.Count == 0) _resultIndex = -1;
                else if (_resultIndex >= _resultBuffer.Count) _resultIndex = _resultBuffer.Count - 1;

                PopulateResultFields();
                UpdateStatus("Deleted entry from buffer (CSV not modified).");
            }
        }

        private void PopulateResultFields()
        {
            if (_resultIndex >= 0 && _resultIndex < _resultBuffer.Count)
            {
                var r = _resultBuffer[_resultIndex];
                PlayerNameCombo.SelectedItem = r.PlayerName;
                PartnerCombo.SelectedItem = r.Partner;
                HcpTextBox.Text = r.Hcp.ToString();
                ResultTextBox.Text = r.Result.ToString();
                PositionTextBox.Text = r.Position.ToString();
            }
            else
            {
                PlayerNameCombo.SelectedIndex = -1;
                PartnerCombo.SelectedIndex = -1;
                HcpTextBox.Text = string.Empty;
                ResultTextBox.Text = string.Empty;
                PositionTextBox.Text = string.Empty;
            }

            PrevResultButton.IsEnabled = _resultBuffer.Count > 0 && _resultIndex > 0;
            NextResultButton.IsEnabled = true;
            UpdateResultButton.IsEnabled = true;
            DeleteResultButton.IsEnabled = _resultBuffer.Count > 0 && _resultIndex >= 0;
        }

        private ResultRecord CreateEmptyResultFromHeader()
        {
            return new ResultRecord
            {
                Date = ResultsDatePicker.Date.Date,
                Club = ResultsClubCombo.SelectedItem?.ToString() ?? string.Empty,
                Venue = ResultsVenueCombo.SelectedItem?.ToString() ?? string.Empty,
                PlayerName = string.Empty,
                Partner = string.Empty,
                Hcp = 0,
                Result = 0,
                Position = 0
            };
        }

        private bool TryBuildCurrentEntry(out ResultRecord rec, out string errorMessage)
        {
            rec = CreateEmptyResultFromHeader();
            errorMessage = string.Empty;

            if (PlayerNameCombo.SelectedItem is null)
            {
                errorMessage = "Select player name.";
                return false;
            }
            rec.PlayerName = PlayerNameCombo.SelectedItem.ToString() ?? string.Empty;

            if (PartnerCombo.SelectedItem is null)
            {
                errorMessage = "Select partner name.";
                return false;
            }
            rec.Partner = PartnerCombo.SelectedItem.ToString() ?? string.Empty;

            if (rec.Partner == rec.PlayerName)
            {
                errorMessage = "Partner must be different from player.";
                return false;
            }

            if (!int.TryParse(HcpTextBox.Text?.Trim(), out var hcp) || hcp < 0 || hcp > 40)
            {
                errorMessage = "HCP must be an integer 0..40.";
                return false;
            }
            rec.Hcp = hcp;

            if (!int.TryParse(ResultTextBox.Text?.Trim(), out var res) || res < 0 || res > 50)
            {
                errorMessage = "Result must be an integer 0..50.";
                return false;
            }
            rec.Result = res;

            if (!int.TryParse(PositionTextBox.Text?.Trim(), out var pos) || pos < 0 || pos > 8)
            {
                errorMessage = "Position must be an integer 0..8.";
                return false;
            }
            rec.Position = pos;

            return true;
        }

        private void SaveResultRecordToCsv(ResultRecord r)
        {
            var folder = GetDataFolder();
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "results.csv");
            var header = "Date,Club,Venue,Player,Partner,Hcp,Result,Position";
            var line = $"{r.Date:yyyy-MM-dd},{EscapeCsv(r.Club)},{EscapeCsv(r.Venue)},{EscapeCsv(r.PlayerName)},{EscapeCsv(r.Partner)},{r.Hcp},{r.Result},{r.Position}";
            var writeHeader = !File.Exists(path);
            using var sw = new StreamWriter(path, append: true);
            if (writeHeader) sw.WriteLine(header);
            sw.WriteLine(line);
        }

        private static string EscapeCsv(string s)
        {
            if (s is null) return string.Empty;
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }
    }
}