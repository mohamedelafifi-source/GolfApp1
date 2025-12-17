
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
                    // no results yet . start with a single blank entry
                    _resultBuffer.Add(CreateEmptyResultFromHeader());
                    _resultIndex = 0;
                    PopulateResultFields();
                    UpdateStatus("No existing results found ready to enter new result");
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
            // Count filled entries (non-empty PlayerName)
            var filledCount = _resultBuffer.Count(r => !string.IsNullOrWhiteSpace(r.PlayerName));

            // If we already have 8 filled entries, cannot add more
            if (filledCount >= 8)
            {
                UpdateStatus("Cannot add more entries: Maximum of 8 entries reached.");
                return;
            }

            // Move to next record if exists
            if (_resultIndex < _resultBuffer.Count - 1)
            {
                _resultIndex++;
                PopulateResultFields();
            }
            else
            {
                // At the end - create new empty entry (only if less than 8 filled)
                _resultBuffer.Add(CreateEmptyResultFromHeader());
                _resultIndex = _resultBuffer.Count - 1;
                PopulateResultFields();
                UpdateStatus("Created new empty entry.");
            }
        }

        // Update (save) current entry into DB
        private async void OnUpdateResultClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Starting save operation...");

            if (!TryBuildCurrentEntry(out var rec, out var error))
            {
                UpdateStatus($"Validation failed: {error}");
                return;
            }

            UpdateStatus($"Building record for player: {rec.PlayerName}, Venue: {rec.Venue}, Date: {rec.Date:yyyy-MM-dd}, Club: {rec.Club}");

            // preserve Id if we're editing an existing buffer entry
            if (_resultIndex >= 0 && _resultIndex < _resultBuffer.Count)
            {
                rec.Id = _resultBuffer[_resultIndex].Id ?? string.Empty;
            }

            if (string.IsNullOrEmpty(rec.Id))
            {
                rec.Id = Guid.NewGuid().ToString();
            }

            UpdateStatus($"Record ID: {rec.Id}");

            // set PlayerId / PartnerId if available from VM
            if (_vm is not null)
            {
                var p = _vm.Players.FirstOrDefault(x => string.Equals(x.Name, rec.PlayerName, StringComparison.Ordinal));
                if (p is not null)
                {
                    rec.PlayerId = p.Id;
                    UpdateStatus($"Found player ID: {rec.PlayerId}");
                }
                else
                {
                    UpdateStatus($"Warning: Could not find player ID for '{rec.PlayerName}'");
                }

                // Only look up partner if partner name is provided
                if (!string.IsNullOrWhiteSpace(rec.Partner))
                {
                    var q = _vm.Players.FirstOrDefault(x => string.Equals(x.Name, rec.Partner, StringComparison.Ordinal));
                    if (q is not null)
                    {
                        rec.PartnerId = q.Id;
                        UpdateStatus($"Found partner ID: {rec.PartnerId}");
                    }
                    else
                    {
                        UpdateStatus($"Warning: Could not find partner ID for '{rec.Partner}'");
                    }
                }
            }

            if (_db is null)
            {
                UpdateStatus("CRITICAL: Database not initialized.");
                await ShowErrorAsync("Database Error", "Database is not initialized. Cannot save results.");
                return;
            }

            try
            {
                UpdateStatus("Calling database UpsertResultAsync...");
                var err = await _db.UpsertResultAsync(rec);

                if (err != null)
                {
                    UpdateStatus($"Database save failed: {err}");
                    await ShowErrorAsync("Save Failed", $"Database error: {err}");
                    return;
                }

                UpdateStatus("Database save successful!");

                // update buffer
                if (_resultIndex >= 0 && _resultIndex < _resultBuffer.Count)
                {
                    _resultBuffer[_resultIndex] = rec;
                    UpdateStatus("Updated existing buffer entry.");
                }
                else
                {
                    _resultBuffer.Add(rec);
                    _resultIndex = _resultBuffer.Count - 1;
                    UpdateStatus("Added new buffer entry.");
                }

                UpdateStatus($"✓ Saved result for '{rec.PlayerName}' to database.");

                // After saving, navigate to next empty slot or create new one (respecting 8 entry limit)
                var nextEmptyIndex = _resultBuffer.FindIndex(_resultIndex + 1, r => string.IsNullOrWhiteSpace(r.PlayerName));

                if (nextEmptyIndex >= 0)
                {
                    // Found an existing empty slot
                    _resultIndex = nextEmptyIndex;
                    PopulateResultFields();
                    UpdateStatus($"✓ Saved. Moved to next empty slot (index {_resultIndex}).");
                }
                else
                {
                    // No empty slot found check if we can add more (limit of 8 filled entries)
                    var filledCount = _resultBuffer.Count(r => !string.IsNullOrWhiteSpace(r.PlayerName));

                    if (filledCount < 8)
                    {
                        // Create new one at the end
                        _resultBuffer.Add(CreateEmptyResultFromHeader());
                        _resultIndex = _resultBuffer.Count - 1;
                        PopulateResultFields();
                        UpdateStatus($"✓ Saved. Created new empty entry (index {_resultIndex}).");
                    }
                    else
                    {
                        // Maximum reached stay on current entry
                        UpdateStatus($"✓ Saved. Maximum of 8 entries reached.");
                        PopulateResultFields();
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"EXCEPTION during save: {ex.Message}");
                await ShowErrorAsync("Save Exception", $"An error occurred while saving:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}");
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

                // Clear selection first, then set value (prevents showing previous value on empty entries)
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

            // Update all button states
            UpdateNavigationButtonStates();
        }

        // FIX: Separate method to update navigation button states
        private void UpdateNavigationButtonStates()
        {
            // Count filled entries for button state
            var filledCount = _resultBuffer.Count(r => !string.IsNullOrWhiteSpace(r.PlayerName));

            // SIMPLIFIED LOGIC FOR PREV/NEXT BUTTONS:
            // Previous: Disabled only at first player (index 0)
            PrevResultButton.IsEnabled = _resultIndex > 0;

            // FIX #2: Next button depends on whether required fields are filled
            // Check if all required fields are filled (Partner is optional)
            bool currentFieldsValid =
                PlayerNameCombo.SelectedItem != null &&
                !string.IsNullOrWhiteSpace(HcpTextBox.Text) &&
                !string.IsNullOrWhiteSpace(ResultTextBox.Text) &&
                !string.IsNullOrWhiteSpace(PositionTextBox.Text);

            // Next: Disabled if we have 8 filled players OR if current entry has empty required fields
            NextResultButton.IsEnabled = (filledCount < 8) && currentFieldsValid;

            // Enable Update button based on required fields and changes
            UpdateResultButtonState();

            // Always enable Delete button (even for empty entries - will remove from buffer)
            DeleteResultButton.IsEnabled = _resultBuffer.Count > 0 && _resultIndex >= 0;
        }

        // Check if current data differs from original
        private void UpdateResultButtonState()
        {
            // Check if all required fields are filled (Partner is optional)
            bool allRequiredFieldsFilled =
                PlayerNameCombo.SelectedItem != null &&
                !string.IsNullOrWhiteSpace(HcpTextBox.Text) &&
                !string.IsNullOrWhiteSpace(ResultTextBox.Text) &&
                !string.IsNullOrWhiteSpace(PositionTextBox.Text);

            // If on a blank/new entry, enable only if all required fields are filled
            if (_originalResultRecord == null || string.IsNullOrWhiteSpace(_originalResultRecord.PlayerName))
            {
                UpdateResultButton.IsEnabled = allRequiredFieldsFilled;
                return;
            }

            // For existing entries, check if data has changed AND all required fields are filled
            bool hasChanged =
                PlayerNameCombo.SelectedItem?.ToString() != _originalResultRecord.PlayerName ||
                PartnerCombo.SelectedItem?.ToString() != _originalResultRecord.Partner ||
                HcpTextBox.Text?.Trim() != _originalResultRecord.Hcp.ToString() ||
                ResultTextBox.Text?.Trim() != _originalResultRecord.Result.ToString() ||
                PositionTextBox.Text?.Trim() != _originalResultRecord.Position.ToString();

            UpdateResultButton.IsEnabled = hasChanged && allRequiredFieldsFilled;
        }

        // FIX: Hook into field change events to update ALL button states, not just Update
        private void OnResultFieldChanged(object sender, object e)
        {
            UpdateNavigationButtonStates();
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

            // Validate all required fields (Partner is optional)
            if (PlayerNameCombo.SelectedItem is null)
            {
                errorMessage = "Select player name.";
                return false;
            }
            rec.PlayerName = PlayerNameCombo.SelectedItem.ToString() ?? string.Empty;

            // Partner is OPTIONAL - only validate if selected
            rec.Partner = PartnerCombo.SelectedItem?.ToString() ?? string.Empty;

            // Only check for same player/partner if partner is actually selected
            if (!string.IsNullOrWhiteSpace(rec.Partner) && rec.Partner == rec.PlayerName)
            {
                errorMessage = "Partner must be different from player.";
                return false;
            }

            // Check HCP is not empty
            if (string.IsNullOrWhiteSpace(HcpTextBox.Text?.Trim()))
            {
                errorMessage = "HCP is required.";
                return false;
            }

            if (!int.TryParse(HcpTextBox.Text?.Trim(), out var hcp) || hcp < 0 || hcp > 40)
            {
                errorMessage = "HCP must be an integer 0..40.";
                return false;
            }
            rec.Hcp = hcp;

            // Check Result is not empty
            if (string.IsNullOrWhiteSpace(ResultTextBox.Text?.Trim()))
            {
                errorMessage = "Result is required.";
                return false;
            }

            if (!int.TryParse(ResultTextBox.Text?.Trim(), out var res) || res < 0 || res > 50)
            {
                errorMessage = "Result must be an integer 0..50.";
                return false;
            }
            rec.Result = res;

            // Check Position is not empty
            if (string.IsNullOrWhiteSpace(PositionTextBox.Text?.Trim()))
            {
                errorMessage = "Position is required.";
                return false;
            }

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