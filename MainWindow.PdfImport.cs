
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

        // Step 1: show unsorted parsed lines for confirmation.
        // Dialog buttons: Primary = "Preview" (go to grouped preview), Close = "Exit".
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

                // temp storage for raw & preview files
                var tempDir = Path.Combine(Path.GetTempPath(), "GolfApp1_Parsed");
                Directory.CreateDirectory(tempDir);
                var rawPath = Path.Combine(tempDir, $"parsed_raw_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllLines(rawPath, parsed.Select(p => p.RawLine ?? string.Empty));

                // parsing regexes and extraction
                var truncatedEntryRx = new Regex(
                    @"^\s*(?:\d+\s+)?(?<name>.+?)\s+(?<points>-?\d+|WD|DQ|DNS)(?:\s*pts?)?\s*\(\s*(?<hc>[+-]?\d{1,2}(?:\.\d)?)\s*\)",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var parAny = new Regex(@"\((?<inside>[^)]*)\)", RegexOptions.Compiled);

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

                // write preview temp file
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

                // Build confirmation UI (unsorted parsed lines)
                var panel = new StackPanel { Spacing = 6 };

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

                var scroll = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 640
                };

                var contentRoot = new StackPanel { Spacing = 8 };
                contentRoot.Children.Add(new TextBlock
                {
                    Text = $"Parsed file: {file.Name} — lines: {extractedRows.Count}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                contentRoot.Children.Add(scroll);

                var confirmDlg = new ContentDialog
                {
                    Title = $"PDF Parse — Confirm: {file.Name}",
                    Content = contentRoot,
                    PrimaryButtonText = "Preview",
                    CloseButtonText = "Exit",
                    XamlRoot = this.Content?.XamlRoot
                };

                if (this.Content?.XamlRoot == null)
                {
                    UpdateStatus($"Parsed {parsed.Count} lines (preview unavailable). Files saved to: {tempDir}");
                    return;
                }

                var confirmResult = await confirmDlg.ShowAsync();

                if (confirmResult == ContentDialogResult.Primary)
                {
                    // go to grouped preview that has Save/Update/Close
                    await ShowClubGroupedPreviewWithActionsAsync(extractedRows);
                }
                else
                {
                    UpdateStatus("Preview cancelled");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Preview failed: " + ex.Message);
                await LocalShowErrorAsync("Preview failed", ex.Message);
            }
        }

        // Step 2: grouped preview with dialog chrome actions:
        // Primary = Update (placeholder), Secondary = Save (choose folder), Close = Exit.
        private async Task ShowClubGroupedPreviewWithActionsAsync(List<(string? Name, string Points, string Handicap, string Raw, int Position)> extractedRows)
        {
            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await LocalShowErrorAsync("Club Preview", "Database not initialized.");
                return;
            }

            // Build name->club map
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
            var grouped = new Dictionary<string, List<(string Name, string Points, string Handicap, int Position, string Raw)>>();
            string unknownKey = "__UNKNOWN__";

            foreach (var r in extractedRows)
            {
                var normalized = NormalizeName(r.Name ?? string.Empty);

                if (nameToClub.TryGetValue(normalized, out var clubShort))
                {
                    if (!grouped.ContainsKey(clubShort)) grouped[clubShort] = new List<(string, string, string, int, string)>();
                    grouped[clubShort].Add((r.Name ?? string.Empty, r.Points, r.Handicap, r.Position, r.Raw));
                    continue;
                }

                // Fuzzy fallback
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
                    if (!grouped.ContainsKey(matchedClub)) grouped[matchedClub] = new List<(string, string, string, int, string)>();
                    grouped[matchedClub].Add((r.Name ?? string.Empty, r.Points, r.Handicap, r.Position, r.Raw));
                }
                else
                {
                    if (!grouped.ContainsKey(unknownKey)) grouped[unknownKey] = new List<(string, string, string, int, string)>();
                    grouped[unknownKey].Add((r.Name ?? string.Empty, r.Points, r.Handicap, r.Position, r.Raw));
                }
            }

            if (grouped.Count == 0)
            {
                await LocalShowErrorAsync("Club Preview", "No players matched clubs in the database.");
                return;
            }

            // Build grouped UI content
            var panel = new StackPanel { Spacing = 10 };
            foreach (var kv in grouped.OrderBy(g => g.Key == unknownKey ? "ZZZ" : g.Key))
            {
                string clubShort = kv.Key;
                string clubDisplay = clubShort == unknownKey ? "Unknown Club" : (clubLookup.TryGetValue(clubShort, out var c) ? $"{c.LongName} ({c.ShortName})" : clubShort);

                panel.Children.Add(new TextBlock
                {
                    Text = clubDisplay,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                });

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

            // Dialog with actions: Update (primary), Save (secondary), Close (close)
            var dlgContent = new StackPanel { Spacing = 8 };
            dlgContent.Children.Add(new TextBlock
            {
                Text = "Grouped by Club",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            dlgContent.Children.Add(scroll2);

            var previewDlg = new ContentDialog
            {
                Title = "Club Results Preview (grouped)",
                Content = dlgContent,
                PrimaryButtonText = "Update",
                SecondaryButtonText = "Save",
                CloseButtonText = "Exit",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot == null)
            {
                UpdateStatus("Club preview created (UI unavailable).");
                return;
            }

            bool keepShowing = true;
            while (keepShowing)
            {
                var result = await previewDlg.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // Placeholder for Update
                    UpdateStatus("Update invoked (not implemented).");
                    await LocalShowErrorAsync("Update", "Update action not implemented yet. Placeholder only.");
                    keepShowing = false;
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    // Save CSV including Date and Venue (no Raw line)
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
                            continue;
                        }

                        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                        var previewDest = Path.Combine(folder.Path, $"club_parsed_preview_{stamp}.csv");

                        // Date and Venue from UI (fallbacks)
                        var dateValue = ResultsDatePicker?.Date.Date ?? DateTime.Now.Date;
                        var dateStr = dateValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        var venue = ResultsVenueCombo?.SelectedItem?.ToString() ?? string.Empty;

                        var lines = new List<string>();
                        // Header
                        lines.Add("Date,Venue,Club,Name,Points,Handicap,Position");

                        foreach (var kv in grouped.OrderBy(g => g.Key == unknownKey ? "ZZZ" : g.Key))
                        {
                            string clubShort = kv.Key;
                            string clubDisplay = clubShort == unknownKey ? "Unknown Club" : (clubLookup.TryGetValue(clubShort, out var c) ? $"{c.LongName} ({c.ShortName})" : clubShort);

                            foreach (var row in kv.Value)
                            {
                                var name = CsvEscape(row.Name ?? string.Empty);
                                var points = CsvEscape(row.Points ?? string.Empty);
                                var hc = CsvEscape(row.Handicap ?? string.Empty);
                                var position = row.Position.ToString(CultureInfo.InvariantCulture);
                                var club = CsvEscape(clubDisplay);
                                // Date and Venue same for all rows (from UI)
                                lines.Add($"{CsvEscape(dateStr)},{CsvEscape(venue)},{club},{name},{points},{hc},{position}");
                            }
                        }

                        File.WriteAllLines(previewDest, lines, Encoding.UTF8);

                        UpdateStatus($"Saved grouped preview to: {folder.Path}");
                        await LocalShowErrorAsync("Save parsed files", $"Saved grouped preview to:\n{previewDest}");
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus("Save failed: " + ex.Message);
                        await LocalShowErrorAsync("Save failed", ex.Message);
                    }
                }
                else
                {
                    // Exit
                    keepShowing = false;
                }
            }
        }

        // Original grouped preview kept for compatibility (close-only dialog)
        private async Task ShowClubGroupedPreviewAsync(List<(string? Name, string Points, string Handicap, string Raw, int Position)> extractedRows)
        {
            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await LocalShowErrorAsync("Club Preview", "Database not initialized.");
                return;
            }

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

            var panel = new StackPanel { Spacing = 10 };
            foreach (var kv in grouped.OrderBy(g => g.Key == unknownKey ? "ZZZ" : g.Key))
            {
                string clubShort = kv.Key;
                string clubDisplay = clubShort == unknownKey ? "Unknown Club" : (clubLookup.TryGetValue(clubShort, out var c) ? $"{c.LongName} ({c.ShortName})" : clubShort);

                panel.Children.Add(new TextBlock
                {
                    Text = clubDisplay,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                });

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
        private async Task LocalShowErrorAsync(string title, string message)
        {
            UpdateStatus(message);
            var dlg = new ContentDialog { Title = title, Content = message, CloseButtonText = "OK", XamlRoot = this.Content?.XamlRoot };
            if (this.Content?.XamlRoot != null) await dlg.ShowAsync();
        }

        //Trim names and be case insensitive
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

                var combined = (0.75 * lastSim) + (0.25 * firstSim);

                if (string.Equals(lastA, lastB, StringComparison.OrdinalIgnoreCase)) combined = Math.Max(combined, 0.995);

                return (combined, firstSim, lastSim);
            }

            var full = JaroWinkler(a, b);
            return (full, full, full);
        }

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

        // CSV escaping helper
        private static string CsvEscape(string s)
        {
            if (s is null) return string.Empty;
            var escaped = s.Replace("\"", "\"\"");
            if (escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r'))
                return $"\"{escaped}\"";
            return escaped;
        }
    }
}