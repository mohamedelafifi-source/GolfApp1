
//MainWindow.PdfImport.cs
//============================
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using GolfApp1.Services;
using GolfApp1.Models;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        // Set to true if you want the preview TSV to be selected in Explorer after parsing.
        // Default: false to avoid switching focus away from the app.
        private readonly bool _openExplorerAfterExport = false;

        // Called from Results menu.
        private async void OnImportPdfClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Select PDF to preview...");

            var file = await PickSingleFileAsync(new[] { ".pdf" });
            if (file is null)
            {
                UpdateStatus("No file selected.");
                return;
            }

            await PreviewPdfFileAsync(file);
        }

        // Helper: pick a single file with the given extensions (returns null if cancelled or fails).
        private async Task<StorageFile?> PickSingleFileAsync(string[] extensions)
        {
            var picker = new FileOpenPicker();
            foreach (var ext in extensions) picker.FileTypeFilter.Add(ext);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            // Attach to the WinUI window (required on desktop)
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            try
            {
                return await picker.PickSingleFileAsync().AsTask();
            }
            catch
            {
                return null;
            }
        }

        // Preview-only — show only lines that start with a number and contain a trailing ")".
        // Extract Name, Points and Handicap (handicap shown without parentheses).
        // Writes two temp files for sharing: raw parsed lines and extracted preview lines,
        // opens Explorer to the temp folder (optional), and allows importing selected rows into the database.
        private async Task PreviewPdfFileAsync(StorageFile file)
        {
            UpdateStatus($"Parsing PDF: {file.Name}");

            try
            {
                var importer = new ResultsImportService();
                var parsed = await importer.ParsePdfAsync(file.Path);

                if (parsed == null || parsed.Count == 0)
                {
                    UpdateStatus("No lines parsed from PDF.");
                    await ShowErrorAsync("Import", "No lines were parsed from the selected PDF.");
                    return;
                }

                // write raw parsed lines to temp file so you can copy/upload them
                var tempDir = Path.Combine(Path.GetTempPath(), "GolfApp1_Parsed");
                Directory.CreateDirectory(tempDir);
                var rawPath = Path.Combine(tempDir, $"parsed_raw_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllLines(rawPath, parsed.Select(p => p.RawLine ?? string.Empty));

                // Core regex: operate on truncated text up to first ')' to ignore trailing "Last Nine Holes" etc.
                var truncatedEntryRx = new Regex(
                    @"^\s*(?:\d+\s+)?(?<name>.+?)\s+(?<points>-?\d+|WD|DQ|DNS)(?:\s*pts?)?\s*\(\s*(?<hc>[+-]?\d{1,2}(?:\.\d)?)\s*\)",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                var parAny = new Regex(@"\((?<inside>[^)]*)\)", RegexOptions.Compiled);

                // Build extracted list (Name, Points, Handicap, Raw, Position)
                var extracted = parsed
                    .Select(p => new { Raw = (p.RawLine ?? string.Empty).Trim(), Parsed = p })
                    .Where(x => Regex.IsMatch(x.Raw, @"^\s*\d") && x.Raw.Contains(')'))
                    .Select(x =>
                    {
                        var idx = x.Raw.IndexOf(')');
                        var truncated = idx >= 0 ? x.Raw.Substring(0, idx + 1) : x.Raw;

                        var m = truncatedEntryRx.Match(truncated);
                        if (m.Success)
                        {
                            var name = Regex.Replace(m.Groups["name"].Value.Trim(), @"\s+", " ");
                            var points = m.Groups["points"].Value.Trim();
                            var hc = m.Groups["hc"].Value.Trim();
                            var hcDisplay = hc.StartsWith("+") ? hc.Substring(1) : hc;

                            var posMatch = Regex.Match(x.Raw, @"^\s*(\d+)");
                            var pos = posMatch.Success ? int.Parse(posMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;

                            return new { Name = name, Points = points, Handicap = hcDisplay, Raw = x.Raw, Position = pos };
                        }

                        var par = parAny.Match(truncated);
                        if (par.Success)
                        {
                            var inside = par.Groups["inside"].Value;
                            var numMatch = Regex.Match(inside, @"[+-]?\d{1,2}(?:\.\d)?");
                            var hcFound = numMatch.Success ? numMatch.Value.Trim() : "—";

                            var before = truncated.Substring(0, par.Index).Trim();
                            var tokens = before.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                            string pointsToken = string.IsNullOrWhiteSpace(x.Parsed.Result) ? "—" : x.Parsed.Result;
                            string nameToken = Regex.Replace(before, @"^\d+\s*", "").Trim();

                            if (tokens.Length >= 2)
                            {
                                var last = tokens[^1].Trim().TrimEnd('.', ',');
                                if (Regex.IsMatch(last, @"^(?:-?\d+|WD|DQ|DNS)$", RegexOptions.IgnoreCase))
                                {
                                    pointsToken = last;
                                    var nameParts = tokens.Skip(1).Take(tokens.Length - 2).ToArray();
                                    nameToken = nameParts.Length > 0 ? string.Join(' ', nameParts) : Regex.Replace(before, @"^\d+\s*", "").Trim();
                                }
                            }

                            var posMatch = Regex.Match(x.Raw, @"^\s*(\d+)");
                            var pos = posMatch.Success ? int.Parse(posMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;

                            var nameNormalized = string.IsNullOrWhiteSpace(x.Parsed.Name) ? Regex.Replace(nameToken, @"\s+", " ").Trim() : x.Parsed.Name;
                            return new { Name = Regex.Replace(nameNormalized, @"\s+", " "), Points = pointsToken, Handicap = hcFound, Raw = x.Raw, Position = pos };
                        }

                        var fallbackName = string.IsNullOrWhiteSpace(x.Parsed.Name) ? Regex.Replace(truncated, @"\s+", " ").Trim() : x.Parsed.Name;
                        var fallbackPoints = string.IsNullOrWhiteSpace(x.Parsed.Result) ? "—" : x.Parsed.Result;
                        var fallbackHc = string.IsNullOrWhiteSpace(x.Parsed.HandicapIndex) ? "—" : x.Parsed.HandicapIndex.TrimStart('+');
                        var posFallback = Regex.Match(x.Raw, @"^\s*(\d+)").Success ? int.Parse(Regex.Match(x.Raw, @"^\s*(\d+)").Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                        return new { Name = fallbackName, Points = fallbackPoints, Handicap = fallbackHc, Raw = x.Raw, Position = posFallback };
                    })
                    .ToList();

                // write extracted preview lines to temp file (tab-separated) for sharing
                var previewPath = Path.Combine(tempDir, $"parsed_preview_{DateTime.Now:yyyyMMdd_HHmmss}.tsv");
                File.WriteAllLines(previewPath, new[] { "Name\tPoints\tHandicap\tPosition\tRawLine" }
                    .Concat(extracted.Select(e => $"{e.Name}\t{e.Points}\t{e.Handicap}\t{e.Position}\t{e.Raw}")));

                // open explorer with the temp folder selected so you can copy/upload the files
                if (_openExplorerAfterExport)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("explorer", $"/select,\"{previewPath}\"") { UseShellExecute = true });
                    }
                    catch
                    {
                        // ignore explorer launch failures
                    }
                }

                // Build UI: header + rows with checkbox, Name | Points | Handicap
                var panel = new StackPanel { Spacing = 6 };
                var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                header.Children.Add(new TextBlock { Text = "Import", Width = 60, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                header.Children.Add(new TextBlock { Text = "Name", Width = 340, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                header.Children.Add(new TextBlock { Text = "Points", Width = 80, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
                header.Children.Add(new TextBlock { Text = "Handicap", Width = 100, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
                panel.Children.Add(header);

                var checkBoxes = new List<CheckBox>();
                foreach (var e in extracted.Take(500))
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                    var cb = new CheckBox { IsChecked = true, Width = 60, Tag = e }; // selected by default
                    checkBoxes.Add(cb);
                    row.Children.Add(cb);
                    row.Children.Add(new TextBlock { Text = e.Name, Width = 340, TextWrapping = TextWrapping.Wrap });
                    row.Children.Add(new TextBlock { Text = e.Points, Width = 80, TextAlignment = TextAlignment.Center });
                    row.Children.Add(new TextBlock { Text = e.Handicap, Width = 100, TextAlignment = TextAlignment.Center });
                    panel.Children.Add(row);
                }

                var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 640 };

                var previewDlg = new ContentDialog
                {
                    Title = $"PDF Parse Preview — {file.Name}",
                    Content = scroll,
                    PrimaryButtonText = "Import Selected",
                    CloseButtonText = "Close",
                    XamlRoot = this.Content?.XamlRoot
                };

                if (this.Content?.XamlRoot == null)
                {
                    UpdateStatus($"Parsed {parsed.Count} lines (preview unavailable). Files saved to: {tempDir}");
                    return;
                }

                var result = await previewDlg.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    if (_db is null)
                    {
                        UpdateStatus("Database not initialized.");
                        await ShowErrorAsync("Import failed", "Database not initialized.");
                        return;
                    }

                    // Determine import header values (use existing header controls if set)
                    var importDate = ResultsDatePicker?.Date.Date ?? DateTime.Now.Date;
                    var importClub = ResultsClubCombo?.SelectedItem?.ToString() ?? string.Empty;
                    var importVenue = ResultsVenueCombo?.SelectedItem?.ToString() ?? string.Empty;

                    int imported = 0, failed = 0;
                    var errors = new List<string>();

                    foreach (var cb in checkBoxes)
                    {
                        if (cb.IsChecked != true) continue;
                        var tagObj = cb.Tag;
                        try
                        {
                            // Read fields from anonymous-tag object safely
                            string tagName = Convert.ToString(tagObj?.GetType().GetProperty("Name")?.GetValue(tagObj)) ?? string.Empty;
                            string tagPoints = Convert.ToString(tagObj?.GetType().GetProperty("Points")?.GetValue(tagObj)) ?? string.Empty;
                            string tagHandicap = Convert.ToString(tagObj?.GetType().GetProperty("Handicap")?.GetValue(tagObj)) ?? string.Empty;
                            var posProp = tagObj?.GetType().GetProperty("Position")?.GetValue(tagObj);
                            int posVal = 0;
                            if (posProp != null) int.TryParse(posProp.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out posVal);

                            // Parse numeric values explicitly (no use of 'out var' in conditional)
                            int hParsed = 0;
                            if (!int.TryParse(tagHandicap, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out hParsed))
                            {
                                hParsed = 0;
                            }

                            int sParsed = 0;
                            if (!int.TryParse(tagPoints, NumberStyles.Integer, CultureInfo.InvariantCulture, out sParsed))
                            {
                                sParsed = 0;
                            }

                            var rec = new ResultRecord
                            {
                                Id = Guid.NewGuid().ToString(),
                                Date = importDate,
                                Club = importClub,
                                Venue = importVenue,
                                PlayerName = tagName,
                                Partner = string.Empty,
                                Hcp = hParsed,
                                Result = sParsed,
                                Position = posVal
                            };

                            // Try to resolve PlayerId from VM if possible
                            if (_vm is not null)
                            {
                                var player = _vm.Players.FirstOrDefault(p => string.Equals(p.Name, rec.PlayerName, StringComparison.Ordinal));
                                if (player is not null) rec.PlayerId = player.Id;
                            }

                            var err = await _db.UpsertResultAsync(rec);
                            if (err != null)
                            {
                                failed++;
                                errors.Add($"{rec.PlayerName}: {err}");
                            }
                            else
                            {
                                imported++;
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            errors.Add($"Import error: {ex.Message}");
                        }
                    }

                    var summary = $"Import finished. Imported: {imported}. Failed: {failed}.";
                    UpdateStatus(summary);
                    if (failed > 0)
                    {
                        var details = string.Join("\n", errors.Take(50));
                        await ShowErrorAsync("Import completed with errors", summary + "\n\n" + details);
                    }
                    else
                    {
                        await ShowErrorAsync("Import complete", summary);
                    }
                }
                else
                {
                    UpdateStatus($"Preview cancelled. Files: {rawPath}, {previewPath}");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Preview failed: " + ex.Message);
                await ShowErrorAsync("Preview failed", ex.Message);
            }
        }
    }
}