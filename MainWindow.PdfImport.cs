//MainWindow.PdfImport.cs
//============================
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using GolfApp1.Services;
using GolfApp1.Models;
using WinRT.Interop;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        // Set to true to select the preview TSV in Explorer after parsing.
        private readonly bool _openExplorerAfterExport = false;

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

        private async Task<StorageFile?> PickSingleFileAsync(string[] extensions)
        {
            var picker = new FileOpenPicker();
            foreach (var ext in extensions) picker.FileTypeFilter.Add(ext);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

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

        // Preview-only. Extracts Name, Points, Handicap and shows a selectable import UI.
        // First dialog: unsorted parsed lines for confirmation (no per-row import).
        // Buttons provided: Save (choose folder), Update (persist only known-club results), Club Preview (grouped view), Close.
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
                    await LocalShowErrorAsync("Import", "No lines were parsed from the selected PDF.");
                    return;
                }

                // write raw parsed lines to temp file so you can copy/upload them (temp files preserved)
                var tempDir = Path.Combine(Path.GetTempPath(), "GolfApp1_Parsed");
                Directory.CreateDirectory(tempDir);
                var rawPath = Path.Combine(tempDir, $"parsed_raw_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                File.WriteAllLines(rawPath, parsed.Select(p => p.RawLine ?? string.Empty));

                // Core regex: operate on truncated text up to first ')' to ignore trailing "Last Nine Holes" etc.
                var truncatedEntryRx = new Regex(
                    @"^\s*(?:\d+\s+)?(?<name>.+?)\s+(?<points>-?\d+|WD|DQ|DNS)(?:\s*pts?)?\s*\(\s*(?<hc>[+-]?\d{1,2}(?:\.\d)?)\s*\)",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                var parAny = new Regex(@"\((?<inside>[^)]*)\)", RegexOptions.Compiled);

                // NOTE: Name is nullable so this collection can be passed directly to ShowClubGroupedPreviewAsync
                var extractedRows = new List<(string? Name, string Points, string Handicap, string Raw, int Position)>();

                foreach (var p in parsed)
                {
                    var raw = (p.RawLine ?? string.Empty).Trim();
                    if (!Regex.IsMatch(raw, @"^\s*\d") || !raw.Contains(')')) continue;

                    var idx = raw.IndexOf(')');
                    var truncated = idx >= 0 ? raw.Substring(0, idx + 1) : raw;

                    var m = truncatedEntryRx.Match(truncated);
                    if (m.Success)
                    {
                        var name = Regex.Replace(m.Groups["name"].Value.Trim(), @"\s+", " ");
                        var points = m.Groups["points"].Value.Trim();
                        var hc = m.Groups["hc"].Value.Trim();
                        var hcDisplay = hc.StartsWith("+") ? hc.Substring(1) : hc;
                        var posMatch = Regex.Match(raw, @"^\s*(\d+)");
                        var pos = posMatch.Success ? int.Parse(posMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                        extractedRows.Add((name, points, hcDisplay, raw, pos));
                        continue;
                    }

                    var par = parAny.Match(truncated);
                    if (par.Success)
                    {
                        var inside = par.Groups["inside"].Value;
                        var numMatch = Regex.Match(inside, @"[+-]?\d{1,2}(?:\.\d)?");
                        var hcFound = numMatch.Success ? numMatch.Value.Trim() : "—";

                        var before = truncated.Substring(0, par.Index).Trim();
                        var tokens = before.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                        string pointsToken = string.IsNullOrWhiteSpace(p.Result) ? "—" : p.Result;
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

                        var posMatch = Regex.Match(raw, @"^\s*(\d+)");
                        var pos = posMatch.Success ? int.Parse(posMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                        var nameNormalized = string.IsNullOrWhiteSpace(p.Name) ? Regex.Replace(nameToken, @"\s+", " ").Trim() : p.Name;
                        extractedRows.Add((Regex.Replace(nameNormalized, @"\s+", " "), pointsToken, hcFound, raw, pos));
                        continue;
                    }

                    // fallback
                    var fallbackName = string.IsNullOrWhiteSpace(p.Name) ? Regex.Replace(truncated, @"\s+", " ").Trim() : p.Name;
                    var fallbackPoints = string.IsNullOrWhiteSpace(p.Result) ? "—" : p.Result;
                    var fallbackHc = string.IsNullOrWhiteSpace(p.HandicapIndex) ? "—" : p.HandicapIndex.TrimStart('+');
                    var posFallbackMatch = Regex.Match(raw, @"^\s*(\d+)");
                    var posFallback = posFallbackMatch.Success ? int.Parse(posFallbackMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                    extractedRows.Add((fallbackName, fallbackPoints, fallbackHc, raw, posFallback));
                }

                // write extracted preview lines to temp file (tab-separated) for sharing
                var previewPath = Path.Combine(tempDir, $"parsed_preview_{DateTime.Now:yyyyMMdd_HHmmss}.tsv");
                File.WriteAllLines(previewPath, new[] { "Name\tPoints\tHandicap\tPosition\tRawLine" }
                    .Concat(extractedRows.Select(e => $"{e.Name}\t{e.Points}\t{e.Handicap}\t{e.Position}\t{e.Raw}")));

                if (_openExplorerAfterExport)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("explorer", $"/select,\"{previewPath}\"") { UseShellExecute = true });
                    }
                    catch { /* ignore */ }
                }

                // Build preview UI using Grid for fixed columns so Points/Handicap remain visible
                var panel = new StackPanel { Spacing = 6 };

                // Header: Grid with columns: Name (*), Points (80), Handicap (80)
                var headerGrid = new Grid
                {
                    ColumnDefinitions = {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = new GridLength(80) },
                        new ColumnDefinition { Width = new GridLength(80) }
                    },
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var nameHeader = new TextBlock { Text = "Name", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(nameHeader, 0);
                headerGrid.Children.Add(nameHeader);
                var pointsHeader = new TextBlock { Text = "Points", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(pointsHeader, 1);
                headerGrid.Children.Add(pointsHeader);
                var hcHeader = new TextBlock { Text = "Handicap", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(hcHeader, 2);
                headerGrid.Children.Add(hcHeader);

                panel.Children.Add(headerGrid);

                // Data rows: use Grid per row to align columns (no import checkbox)
                foreach (var e in extractedRows.Take(500))
                {
                    var rowGrid = new Grid
                    {
                        ColumnDefinitions = {
                            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                            new ColumnDefinition { Width = new GridLength(80) },
                            new ColumnDefinition { Width = new GridLength(80) }
                        },
                        Margin = new Thickness(0, 2, 0, 2)
                    };

                    var nameTb = new TextBlock { Text = e.Name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(nameTb, 0);
                    rowGrid.Children.Add(nameTb);

                    var pointsTb = new TextBlock { Text = e.Points, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(pointsTb, 1);
                    rowGrid.Children.Add(pointsTb);

                    var hcText = string.IsNullOrWhiteSpace(e.Handicap) || e.Handicap == "—" ? "—" : (e.Handicap.StartsWith("(") ? e.Handicap : $"({e.Handicap})");
                    var hcTb = new TextBlock { Text = hcText, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(hcTb, 2);
                    rowGrid.Children.Add(hcTb);

                    panel.Children.Add(rowGrid);
                }

                // Place panel inside a ScrollViewer
                var scroll = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 640
                };

                // Build dialog content with Save button in content and use ContentDialog buttons for Update/ClubPreview/Close
                var contentRoot = new StackPanel { Spacing = 8 };
                // Show header info (file name + count)
                contentRoot.Children.Add(new TextBlock
                {
                    Text = $"Parsed file: {file.Name} — lines: {extractedRows.Count}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                contentRoot.Children.Add(scroll);

                // In-content Save button (user chooses folder)
                var inContentBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8 };
                var saveBtn = new Button { Content = "Save", Width = 100 };
                inContentBar.Children.Add(saveBtn);
                contentRoot.Children.Add(inContentBar);

                var previewDlg = new ContentDialog
                {
                    Title = $"PDF Parse Preview — {file.Name}",
                    Content = contentRoot,
                    PrimaryButtonText = "Update",
                    SecondaryButtonText = "Club Preview",
                    CloseButtonText = "Close",
                    XamlRoot = this.Content?.XamlRoot
                };

                // Save handler (runs while dialog is open)
                saveBtn.Click += async (_, _) =>
                {
                    try
                    {
                        var folderPicker = new FolderPicker();
                        InitializeWithWindow.Initialize(folderPicker, WindowNative.GetWindowHandle(this));
                        folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                        folderPicker.FileTypeFilter.Add("*");

                        var folder = await folderPicker.PickSingleFolderAsync().AsTask();
                        if (folder == null)
                        {
                            UpdateStatus("Save cancelled.");
                            return;
                        }

                        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                        var rawDest = Path.Combine(folder.Path, $"parsed_raw_{stamp}.txt");
                        var previewDest = Path.Combine(folder.Path, $"parsed_preview_{stamp}.tsv");

                        File.WriteAllLines(rawDest, parsed.Select(p => p.RawLine ?? string.Empty));
                        File.WriteAllLines(previewDest, new[] { "Name\tPoints\tHandicap\tPosition\tRawLine" }
                            .Concat(extractedRows.Select(e => $"{e.Name}\t{e.Points}\t{e.Handicap}\t{e.Position}\t{e.Raw}")));

                        UpdateStatus($"Saved parsed files to: {folder.Path}");
                        await LocalShowErrorAsync("Save parsed files", $"Saved files to:\n{folder.Path}");
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus("Save failed: " + ex.Message);
                        await LocalShowErrorAsync("Save failed", ex.Message);
                    }
                };

                // Dialog loop: secondary shows grouped preview and returns to this dialog; primary performs update and exits; close exits.
                if (this.Content?.XamlRoot == null)
                {
                    UpdateStatus($"Parsed {parsed.Count} lines (preview unavailable). Files saved to: {tempDir}");
                    return;
                }

                bool keepShowing = true;
                while (keepShowing)
                {
                    var result = await previewDlg.ShowAsync();

                    if (result == ContentDialogResult.Primary)
                    {
                        // Update logic (persist known-club results)
                        if (_db is null)
                        {
                            UpdateStatus("Database not initialized.");
                            await LocalShowErrorAsync("Update failed", "Database not initialized.");
                            return;
                        }

                        UpdateStatus("Updating results for known clubs...");

                        // build name->club map (exact normalized name)
                        var clubs = await _db.GetAllClubsAsync();
                        var nameToClub = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var c in clubs)
                        {
                            var players = await _db.GetPlayersByClubAsync(c.ShortName);
                            foreach (var pl in players)
                            {
                                var key = NormalizeName(pl.Name ?? string.Empty);
                                if (!string.IsNullOrWhiteSpace(key) && !nameToClub.ContainsKey(key))
                                    nameToClub[key] = c.ShortName;
                            }
                        }

                        int stored = 0, skipped = 0, failures = 0;
                        var errors = new List<string>();

                        foreach (var r in extractedRows)
                        {
                            var normalized = NormalizeName(r.Name ?? string.Empty);
                            if (!nameToClub.TryGetValue(normalized, out var clubShort))
                            {
                                skipped++;
                                continue;
                            }

                            try
                            {
                                var rec = new ResultRecord
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    Date = ResultsDatePicker?.Date.Date ?? DateTime.Now.Date,
                                    Club = clubShort,
                                    Venue = ResultsVenueCombo?.SelectedItem?.ToString() ?? string.Empty,
                                    PlayerName = r.Name ?? string.Empty,
                                    Partner = string.Empty,
                                    Hcp = int.TryParse(r.Handicap.Replace("(", "").Replace(")", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var h) ? h : 0,
                                    Result = int.TryParse(r.Points, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0,
                                    Position = r.Position
                                };

                                var err = await _db.UpsertResultAsync(rec);
                                if (err != null)
                                {
                                    failures++;
                                    errors.Add($"{rec.PlayerName}: {err}");
                                }
                                else
                                {
                                    stored++;
                                }
                            }
                            catch (Exception ex)
                            {
                                failures++;
                                errors.Add($"{r.Name}: {ex.Message}");
                            }
                        }

                        var summary = $"Update finished. Stored: {stored}. Skipped (unknown club): {skipped}. Failures: {failures}.";
                        UpdateStatus(summary);
                        if (errors.Count > 0)
                        {
                            var details = string.Join("\n", errors.Take(50));
                            await LocalShowErrorAsync("Update completed with errors", summary + "\n\n" + details);
                        }
                        else
                        {
                            await LocalShowErrorAsync("Update complete", summary);
                        }

                        keepShowing = false; // exit dialog loop
                    }
                    else if (result == ContentDialogResult.Secondary)
                    {
                        // Show grouped club preview then loop to re-show initial preview
                        await ShowClubGroupedPreviewAsync(extractedRows);
                        // loop continues to re-show preview dialog
                    }
                    else
                    {
                        // Close pressed
                        keepShowing = false;
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Preview failed: " + ex.Message);
                await LocalShowErrorAsync("Preview failed", ex.Message);
            }
        }

        // ShowClubGroupedPreviewAsync unchanged (keeps grouping UI)
        private async Task ShowClubGroupedPreviewAsync(List<(string? Name, string Points, string Handicap, string Raw, int Position)> extractedRows)
        {
            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await LocalShowErrorAsync("Club Preview", "Database not initialized.");
                return;
            }

            // Build a lookup of player normalized name -> club short name by iterating clubs and players
            var clubs = await _db.GetAllClubsAsync();
            var nameToClub = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var clubLookup = clubs.ToDictionary(c => c.ShortName, c => c);

            foreach (var club in clubs)
            {
                var players = await _db.GetPlayersByClubAsync(club.ShortName);
                foreach (var pl in players)
                {
                    var key = NormalizeName(pl.Name ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(key) && !nameToClub.ContainsKey(key))
                    {
                        nameToClub[key] = club.ShortName;
                    }
                }
            }

            // Group parsed results by discovered club (or "Unknown")
            var grouped = new Dictionary<string, List<(string Name, string Points, string Handicap, int Position)>>();

            string unknownKey = "__UNKNOWN__";
            foreach (var r in extractedRows)
            {
                var normalized = NormalizeName(r.Name ?? string.Empty);

                if (nameToClub.TryGetValue(normalized, out var clubShort))
                {
                    if (!grouped.ContainsKey(clubShort)) grouped[clubShort] = new List<(string, string, string, int)>();
                    grouped[clubShort].Add((r.Name ?? string.Empty, r.Points, r.Handicap, r.Position));
                    continue;
                }

                // Fuzzy fallback using token-aware metrics
                double best = 0.0;
                string? bestKey = null;
                (double combined, double firstSim, double lastSim) bestMetrics = (0, 0, 0);
                const double GroupThreshold = 0.95;
                const double GroupMinFirstSim = 0.70;
                const double GroupMinLastExact = 0.995;
                const double GroupMinFirstWhenLastExact = 0.65;

                foreach (var key in nameToClub.Keys)
                {
                    var metrics = ComputeNameMetrics(normalized, key);
                    if (metrics.combined > best)
                    {
                        best = metrics.combined;
                        bestKey = key;
                        bestMetrics = metrics;
                    }
                }

                if (!string.IsNullOrEmpty(bestKey) &&
                    ((bestMetrics.combined >= GroupThreshold && bestMetrics.firstSim >= GroupMinFirstSim) ||
                     (bestMetrics.lastSim >= GroupMinLastExact && bestMetrics.firstSim >= GroupMinFirstWhenLastExact)))
                {
                    var matchedClub = nameToClub[bestKey];
                    if (!grouped.ContainsKey(matchedClub)) grouped[matchedClub] = new List<(string, string, string, int)>();
                    grouped[matchedClub].Add((r.Name ?? string.Empty, r.Points, r.Handicap, r.Position));
                }
                else
                {
                    if (!grouped.ContainsKey(unknownKey)) grouped[unknownKey] = new List<(string, string, string, int)>();
                    grouped[unknownKey].Add((r.Name ?? string.Empty, r.Points, r.Handicap, r.Position));
                }
            }

            if (grouped.Count == 0)
            {
                await LocalShowErrorAsync("Club Preview", "No players matched clubs in the database.");
                return;
            }

            // Build UI: for each club show header and sorted rows by player name
            var panel = new StackPanel { Spacing = 10 };
            foreach (var kv in grouped.OrderBy(g => g.Key == unknownKey ? "ZZZ" : g.Key))
            {
                string clubShort = kv.Key;
                string clubDisplay = clubShort == unknownKey ? "Unknown Club" : (clubLookup.TryGetValue(clubShort, out var c) ? $"{c.LongName} ({c.ShortName})" : clubShort);

                // club header
                panel.Children.Add(new TextBlock
                {
                    Text = clubDisplay,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                });

                // header row (Grid to align columns)
                var headerRow = new Grid
                {
                    ColumnDefinitions = {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(80) },
                new ColumnDefinition { Width = new GridLength(80) }
            },
                    Margin = new Thickness(0, 4, 0, 2)
                };
                headerRow.Children.Add(new TextBlock { Text = "Name", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                var ph = new TextBlock { Text = "Points", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
                Grid.SetColumn(ph, 1);
                headerRow.Children.Add(ph);
                var hh = new TextBlock { Text = "Handicap", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
                Grid.SetColumn(hh, 2);
                headerRow.Children.Add(hh);
                panel.Children.Add(headerRow);

                foreach (var row in kv.Value.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var r = new Grid
                    {
                        ColumnDefinitions = {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(80) },
                    new ColumnDefinition { Width = new GridLength(80) }
                         },
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    r.Children.Add(new TextBlock { Text = row.Name, TextWrapping = TextWrapping.Wrap });
                    var pt = new TextBlock { Text = row.Points, TextAlignment = TextAlignment.Center };
                    Grid.SetColumn(pt, 1);
                    r.Children.Add(pt);
                    var hcText = string.IsNullOrWhiteSpace(row.Handicap) || row.Handicap == "—" ? "—" : (row.Handicap.StartsWith("(") ? row.Handicap : $"({row.Handicap})");
                    var hct = new TextBlock { Text = hcText, TextAlignment = TextAlignment.Center };
                    Grid.SetColumn(hct, 2);
                    r.Children.Add(hct);
                    panel.Children.Add(r);
                }
            }

            var scroll2 = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 640 };
            var dlg = new ContentDialog
            {
                Title = "Club Results Preview (grouped)",
                Content = scroll2,
                CloseButtonText = "Close",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot == null)
            {
                UpdateStatus("Club preview created (UI unavailable).");
                return;
            }

            await dlg.ShowAsync();
        }

        // Local UI helper used inside this file to avoid cross-file symbol issues during incremental edits.
        // This duplicates behavior of the shared ShowErrorAsync but is scoped to PdfImport.cs only.
        private async Task LocalShowErrorAsync(string title, string message)
        {
            UpdateStatus(message);
            var dlg = new ContentDialog { Title = title, Content = message, CloseButtonText = "OK", XamlRoot = this.Content?.XamlRoot };
            if (this.Content?.XamlRoot != null) await dlg.ShowAsync();
        }

        //Trim names and be case insensitive
        // Remove diacritics and normalize spacing/casing
        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var trimmed = name.Trim();
            var withoutDiacritics = RemoveDiacritics(trimmed);
            var collapsed = Regex.Replace(withoutDiacritics, @"\s+", " ").Trim();
            return collapsed.ToLowerInvariant();
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(capacity: normalized.Length);
            foreach (var ch in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // Compute combined/first/last metrics for two normalized names.
        // Returns (combined, firstSim, lastSim). Callers may apply thresholds.
        private static (double combined, double firstSim, double lastSim) ComputeNameMetrics(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return (0.0, 0.0, 0.0);
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return (1.0, 1.0, 1.0);

            var la = a.Length;
            var lb = b.Length;
            var lenRatio = Math.Min(la, lb) / (double)Math.Max(1, Math.Max(la, lb));
            if (lenRatio < 0.5) return (0.0, 0.0, 0.0);

            var tokensA = a.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var tokensB = b.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // common surname particles to treat as part of last name (e.g. "el mahdy")
            var particles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "el", "al", "abu", "bin", "ibn", "de", "van", "von", "le", "la" };

            if (tokensA.Length > 1 && tokensB.Length > 1)
            {
                var firstA = tokensA[0];
                var firstB = tokensB[0];

                string lastA, lastB;
                if (tokensA.Length >= 2 && particles.Contains(tokensA[^2]))
                    lastA = tokensA[^2] + " " + tokensA[^1];
                else
                    lastA = tokensA[^1];

                if (tokensB.Length >= 2 && particles.Contains(tokensB[^2]))
                    lastB = tokensB[^2] + " " + tokensB[^1];
                else
                    lastB = tokensB[^1];

                var lastSim = JaroWinkler(lastA, lastB);
                var firstSim = JaroWinkler(firstA, firstB);

                // This weight distribution favors last name more heavily
                var combined = (0.75 * lastSim) + (0.25 * firstSim);

                // If exact multi-token last-name match (including particle) boost high
                if (string.Equals(lastA, lastB, StringComparison.OrdinalIgnoreCase)) combined = Math.Max(combined, 0.995);

                return (combined, firstSim, lastSim);
            }

            var full = JaroWinkler(a, b);
            return (full, full, full);
        }

        // Jaro-Winkler similarity (standard implementation)
        private static double JaroWinkler(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return string.IsNullOrEmpty(s2) ? 1.0 : 0.0;
            if (string.IsNullOrEmpty(s2)) return 0.0;

            var jaro = JaroDistance(s1, s2);
            const double scaling = 0.1;
            int prefix = 0;
            for (int i = 0; i < Math.Min(4, Math.Min(s1.Length, s2.Length)); i++)
            {
                if (s1[i] == s2[i]) prefix++;
                else break;
            }
            return jaro + prefix * scaling * (1 - jaro);
        }

        private static double JaroDistance(string s1, string s2)
        {
            var cs1 = s1.ToCharArray();
            var cs2 = s2.ToCharArray();
            int len1 = cs1.Length, len2 = cs2.Length;
            if (len1 == 0) return len2 == 0 ? 1.0 : 0.0;

            int matchDistance = Math.Max(0, (Math.Max(len1, len2) / 2) - 1);

            var s1Matches = new bool[len1];
            var s2Matches = new bool[len2];

            int matches = 0;
            for (int i = 0; i < len1; i++)
            {
                int start = Math.Max(0, i - matchDistance);
                int end = Math.Min(i + matchDistance, len2 - 1);
                for (int j = start; j <= end; j++)
                {
                    if (s2Matches[j]) continue;
                    if (cs1[i] != cs2[j]) continue;
                    s1Matches[i] = true;
                    s2Matches[j] = true;
                    matches++;
                    break;
                }
            }

            if (matches == 0) return 0.0;

            double t = 0;
            int k = 0;
            for (int i = 0; i < len1; i++)
            {
                if (!s1Matches[i]) continue;
                while (!s2Matches[k]) k++;
                if (cs1[i] != cs2[k]) t += 0.5;
                k++;
            }

            double m = matches;
            return ((m / len1) + (m / len2) + ((m - t) / m)) / 3.0;
        }
    }
}