
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

        // Track original values for change detection
        private ResultRecord? _originalResultRecord = null;

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
                    // no results yet – start with a single blank entry
                    _resultBuffer.Add(CreateEmptyResultFromHeader());
                    _resultIndex = 0;
                    PopulateResultFields();
                    UpdateStatus("No existing results found – ready to enter new result.");
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

        // REMOVED: LoadPlayersForResultsAsync - now defined in MainWindow.ResultsHandlers.cs

        // Navigation / CRUD for results buffer
        private void OnPrevResultClicked(object sender, RoutedEventArgs e)
        {
            if (_resultBuffer.Count == 0) return;
            if (_resultIndex > 0)
            {
                _resultIndex--;
                PopulateResultFields();
            }
        }

        private void OnNextResultClicked(object sender, RoutedEventArgs e)
        {
            if (_resultBuffer.Count == 0)
            {
                // Create a new empty entry if buffer is empty
                _resultBuffer.Add(CreateEmptyResultFromHeader());
                _resultIndex = 0;
                PopulateResultFields();
                UpdateStatus("Created new empty entry.");
                return;
            }

            // Move to next record (or create new one if at end)
            if (_resultIndex < _resultBuffer.Count - 1)
            {
                _resultIndex++;
                PopulateResultFields();
            }
            else
            {
                // At the end - add a new empty entry
                _resultBuffer.Add(CreateEmptyResultFromHeader());
                _resultIndex = _resultBuffer.Count - 1;
                PopulateResultFields();
                UpdateStatus("Created new empty entry.");
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

                UpdateStatus($"Saved result for '{rec.PlayerName}'.");

                // FIX #1: After saving, navigate to next empty slot or create new one
                var nextEmptyIndex = _resultBuffer.FindIndex(_resultIndex + 1, r => string.IsNullOrWhiteSpace(r.PlayerName));

                if (nextEmptyIndex >= 0)
                {
                    // Found an existing empty slot
                    _resultIndex = nextEmptyIndex;
                    PopulateResultFields();
                    UpdateStatus($"Saved result. Moved to next empty slot.");
                }
                else
                {
                    // No empty slot found - create new one at the end
                    _resultBuffer.Add(CreateEmptyResultFromHeader());
                    _resultIndex = _resultBuffer.Count - 1;
                    PopulateResultFields();
                    UpdateStatus($"Saved result. Created new empty entry.");
                }
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
                if (_resultBuffer.Count == 0)
                {
                    // Buffer is empty - add a new blank entry
                    _resultBuffer.Add(CreateEmptyResultFromHeader());
                    _resultIndex = 0;
                }
                else if (_resultIndex >= _resultBuffer.Count)
                {
                    _resultIndex = _resultBuffer.Count - 1;
                }

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

                // Store original values for change detection
                _originalResultRecord = new ResultRecord
                {
                    PlayerName = r.PlayerName,
                    Partner = r.Partner,
                    Hcp = r.Hcp,
                    Result = r.Result,
                    Position = r.Position
                };

                // FIX #1: Clear selection first, then set value (prevents showing previous value on empty entries)
                PlayerNameCombo.SelectedIndex = -1;
                PartnerCombo.SelectedIndex = -1;

                if (!string.IsNullOrWhiteSpace(r.PlayerName))
                {
                    PlayerNameCombo.SelectedItem = r.PlayerName;
                }

                if (!string.IsNullOrWhiteSpace(r.Partner))
                {
                    PartnerCombo.SelectedItem = r.Partner;
                }

                HcpTextBox.Text = r.Hcp == 0 && string.IsNullOrWhiteSpace(r.PlayerName) ? string.Empty : r.Hcp.ToString();
                ResultTextBox.Text = r.Result == 0 && string.IsNullOrWhiteSpace(r.PlayerName) ? string.Empty : r.Result.ToString();
                PositionTextBox.Text = r.Position == 0 && string.IsNullOrWhiteSpace(r.PlayerName) ? string.Empty : r.Position.ToString();
            }
            else
            {
                _originalResultRecord = null;
                PlayerNameCombo.SelectedIndex = -1;
                PartnerCombo.SelectedIndex = -1;
                HcpTextBox.Text = string.Empty;
                ResultTextBox.Text = string.Empty;
                PositionTextBox.Text = string.Empty;
            }

            // Enable Prev/Next buttons based on buffer state
            PrevResultButton.IsEnabled = _resultBuffer.Count > 0 && _resultIndex > 0;
            NextResultButton.IsEnabled = true; // Always enabled - will create new entry if needed

            // Enable Update button based on whether data has changed
            UpdateResultButtonState();

            // FIX #2: Always enable Delete button (even for empty entries - will remove from buffer)
            DeleteResultButton.IsEnabled = _resultBuffer.Count > 0 && _resultIndex >= 0;
        }

        // Check if current data differs from original
        private void UpdateResultButtonState()
        {
            // Always enable if we're on a blank/new entry
            if (_originalResultRecord == null || string.IsNullOrWhiteSpace(_originalResultRecord.PlayerName))
            {
                UpdateResultButton.IsEnabled = true;
                return;
            }

            // For existing entries, only enable if data has changed
            bool hasChanged =
                PlayerNameCombo.SelectedItem?.ToString() != _originalResultRecord.PlayerName ||
                PartnerCombo.SelectedItem?.ToString() != _originalResultRecord.Partner ||
                HcpTextBox.Text?.Trim() != _originalResultRecord.Hcp.ToString() ||
                ResultTextBox.Text?.Trim() != _originalResultRecord.Result.ToString() ||
                PositionTextBox.Text?.Trim() != _originalResultRecord.Position.ToString();

            UpdateResultButton.IsEnabled = hasChanged;
        }

        // Hook into field change events to update button state
        private void OnResultFieldChanged(object sender, object e)
        {
            UpdateResultButtonState();
        }

        private ResultRecord CreateEmptyResultFromHeader()
        {
            return new ResultRecord
            {
                Date = ResultsDatePicker?.Date.Date ?? _currentResultsDate ?? DateTime.Now.Date,
                Club = ResultsClubCombo?.SelectedItem?.ToString() ?? _currentResultsClub ?? string.Empty,
                Venue = ResultsVenueCombo?.SelectedItem?.ToString() ?? _currentResultsVenue ?? string.Empty,
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