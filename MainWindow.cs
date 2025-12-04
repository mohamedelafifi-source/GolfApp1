
//MainWindow.cs
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

        // Proceed: load any existing results for date/club/venue and show entry panel.
        private async void OnProceedResultsClicked(object sender, RoutedEventArgs e)
        {
            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                return;
            }

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

            // Prepare player combos (load players)
            var clubShort = ResultsClubCombo.SelectedItem.ToString() ?? string.Empty;
            await LoadPlayersForResultsAsync(clubShort).ConfigureAwait(true);

            // load existing results for the same date & club
            var date = ResultsDatePicker.Date.Date;
            var venue = ResultsVenueCombo.SelectedItem?.ToString() ?? string.Empty;

            try
            {
                var existing = await _db.GetResultsAsync(clubShort, date, date).ConfigureAwait(true);
                // filter by venue (case-insensitive)
                var matches = existing.Where(r => string.Equals(r.Venue ?? string.Empty, venue ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                                      .ToList();

                _resultBuffer.Clear();
                if (matches.Count > 0)
                {
                    // use DB results
                    foreach (var r in matches) _resultBuffer.Add(r);
                    _resultIndex = 0;
                    PopulateResultFields();
                    UpdateStatus($"Loaded {matches.Count} result(s) for {clubShort} @ {venue} on {date:yyyy-MM-dd}.");
                }
                else
                {
                    // no results yet — start with a single blank entry
                    _resultBuffer.Add(CreateEmptyResultFromHeader());
                    _resultIndex = 0;
                    PopulateResultFields();
                    UpdateStatus("No existing results found — ready to enter new result.");
                }

                ResultsEntryPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                UpdateStatus("Failed to load existing results: " + ex.Message);
                // still show entry panel with a blank record
                _resultBuffer.Clear();
                _resultBuffer.Add(CreateEmptyResultFromHeader());
                _resultIndex = 0;
                ResultsEntryPanel.Visibility = Visibility.Visible;
            }
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
                UpdateStatus("No entries available. Create a new entry with Update.");
                return;
            }

            var start = Math.Max(_resultIndex + 1, 0);

            // 1) prefer next empty record
            var nextEmpty = _resultBuffer.FindIndex(start, r => string.IsNullOrWhiteSpace(r.PlayerName));
            if (nextEmpty >= 0)
            {
                _resultIndex = nextEmpty;
                PopulateResultFields();
                return;
            }

            // 2) otherwise move to next existing record if available
            if (_resultIndex < _resultBuffer.Count - 1)
            {
                _resultIndex++;
                PopulateResultFields();
            }
            else
            {
                UpdateStatus("No next record.");
            }
        }

        // Update (save) current entry into DB
        private async void OnUpdateResultClicked(object sender, RoutedEventArgs e)
        {
            if (!TryBuildCurrentEntry(out var rec, out var error))
            {
                UpdateStatus(error);
                return;
            }

            // preserve Id if we're editing an existing buffer entry
            if (_resultIndex >= 0 && _resultIndex < _resultBuffer.Count)
            {
                rec.Id = _resultBuffer[_resultIndex].Id ?? string.Empty;
            }
            else
            {
                rec.Id = Guid.NewGuid().ToString();
            }

            // set PlayerId / PartnerId if available from VM
            if (_vm is not null)
            {
                var p = _vm.Players.FirstOrDefault(x => string.Equals(x.Name, rec.PlayerName, StringComparison.Ordinal));
                if (p is not null) rec.PlayerId = p.Id;
                var q = _vm.Players.FirstOrDefault(x => string.Equals(x.Name, rec.Partner, StringComparison.Ordinal));
                if (q is not null) rec.PartnerId = q.Id;
            }

            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                return;
            }

            try
            {
                var err = await _db.UpsertResultAsync(rec).ConfigureAwait(true);
                if (err != null)
                {
                    UpdateStatus("Save failed: " + err);
                    return;
                }

                // update buffer
                if (_resultIndex >= 0 && _resultIndex < _resultBuffer.Count)
                {
                    _resultBuffer[_resultIndex] = rec;
                }
                else
                {
                    _resultBuffer.Add(rec);
                    _resultIndex = _resultBuffer.Count - 1;
                }

                PopulateResultFields();
                UpdateStatus($"Saved result for '{rec.PlayerName}'.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Save failed: " + ex.Message);
            }
        }

        // Delete current entry (buffer + DB if persisted)
        private async void OnDeleteResultClicked(object sender, RoutedEventArgs e)
        {
            if (_resultIndex < 0 || _resultIndex >= _resultBuffer.Count) return;

            var rec = _resultBuffer[_resultIndex];
            try
            {
                if (!string.IsNullOrEmpty(rec.Id) && _db is not null)
                {
                    var err = await _db.DeleteResultAsync(rec.Id).ConfigureAwait(true);
                    if (err != null)
                    {
                        UpdateStatus("Delete failed: " + err);
                        return;
                    }
                }

                _resultBuffer.RemoveAt(_resultIndex);
                if (_resultBuffer.Count == 0) _resultIndex = -1;
                else if (_resultIndex >= _resultBuffer.Count) _resultIndex = _resultBuffer.Count - 1;

                PopulateResultFields();
                UpdateStatus("Deleted entry.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Delete failed: " + ex.Message);
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

            // Enable Next if there is either a next empty or a next existing record
            var start = Math.Max(_resultIndex + 1, 0);
            var hasNextEmpty = _resultBuffer.FindIndex(start, r => string.IsNullOrWhiteSpace(r.PlayerName)) >= 0;
            var hasNextRecord = _resultIndex < _resultBuffer.Count - 1;
            NextResultButton.IsEnabled = hasNextEmpty || hasNextRecord;

            UpdateResultButton.IsEnabled = _resultBuffer.Count >= 0; // allow saving even for blank
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
    }
}