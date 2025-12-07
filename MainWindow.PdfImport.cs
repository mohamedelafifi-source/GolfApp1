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
    public sealed partial class MainWindow : Window
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
                return await picker.PickSingleFileAsync();
            }
            catch
            {
                return null;
            }
        }

        // Preview-only. Extracts Name, Points, Handicap and shows a selectable import UI.
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

                // Temp folder + raw lines dump for inspection
                var tempDir = Path.Combine(Path.GetTempPath(), "GolfApp1_Parsed");
                Directory.CreateDirectory(tempDir);
                var rawPath = Path.Combine(tempDir, $"parsed_raw_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllLines(rawPath, parsed.Select(p => p.RawLine ?? string.Empty));

                // Regexes
                var truncatedEntryRx = new Regex(
                    @"^\s*(?:\d+\s+)?(?<name>.+?)\s+(?<points>-?\d+|WD|DQ|DNS)(?:\s*pts?)?\s*\(\s*(?<hc>[+-]?\d{1,2}(?:\.\d)?)\s*\)",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var parAny = new Regex(@"\((?<inside>[^)]*)\)", RegexOptions.Compiled);

                // Build concrete list to guarantee reliable UI access
                var extractedRows = new List<(string Name, string Points, string Handicap, string Raw, int Position)>();

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
                        var hcFound = numMatch.Success ? numMatch.Value.Trim().TrimStart('+') : "—";

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

                // write preview TSV
                var previewPath = Path.Combine(tempDir, $"parsed_preview_{DateTime.Now:yyyyMMdd_HHmmss}.tsv");
                File.WriteAllLines(previewPath, new[] { "Name\tPoints\tHandicap\tPosition\tRawLine" }
                    .Concat(extractedRows.Select(e => $"{e.Name}\t{e.Points}\t{e.Handicap}\t{e.Position}\t{e.Raw}")));

                if (_openExplorerAfterExport)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("explorer", $"/select,\"{previewPath}\"") { UseShellExecute = true });
                    }
                    catch { }
                }

                // Build UI with proper layout
                var panel = new StackPanel { Spacing = 8, Margin = new Thickness(10) };

                // Header row using Grid for proper alignment
                var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                var importHeader = new TextBlock
                {
                    Text = "Import",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(importHeader, 0);
                header.Children.Add(importHeader);

                var nameHeader = new TextBlock
                {
                    Text = "Name",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(nameHeader, 1);
                header.Children.Add(nameHeader);

                var pointsHeader = new TextBlock
                {
                    Text = "Points",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(pointsHeader, 2);
                header.Children.Add(pointsHeader);

                var hcHeader = new TextBlock
                {
                    Text = "Handicap",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(hcHeader, 3);
                header.Children.Add(hcHeader);

                panel.Children.Add(header);

                // Add separator
                var separator = new Border
                {
                    Height = 1,
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                panel.Children.Add(separator);

                var checkBoxes = new List<CheckBox>();

                foreach (var e in extractedRows.Take(500))
                {
                    var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                    var cb = new CheckBox
                    {
                        IsChecked = true,
                        Tag = e,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    checkBoxes.Add(cb);
                    Grid.SetColumn(cb, 0);
                    row.Children.Add(cb);

                    var nameText = new TextBlock
                    {
                        Text = e.Name,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 5, 0)
                    };
                    Grid.SetColumn(nameText, 1);
                    row.Children.Add(nameText);

                    var pointsText = new TextBlock
                    {
                        Text = e.Points,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    };
                    Grid.SetColumn(pointsText, 2);
                    row.Children.Add(pointsText);

                    var hcDisplay = string.IsNullOrWhiteSpace(e.Handicap) || e.Handicap == "—"
                        ? "—"
                        : (e.Handicap.StartsWith("(") ? e.Handicap : $"({e.Handicap})");

                    var hcText = new TextBlock
                    {
                        Text = hcDisplay,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    };
                    Grid.SetColumn(hcText, 3);
                    row.Children.Add(hcText);

                    panel.Children.Add(row);
                }

                var scroll = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 600,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var previewDlg = new ContentDialog
                {
                    Title = $"PDF Parse Preview — {file.Name}",
                    Content = scroll,
                    PrimaryButtonText = "Import Selected",
                    CloseButtonText = "Close",
                    XamlRoot = this.Content?.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
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

                    var importDate = ResultsDatePicker?.Date.Date ?? DateTime.Now.Date;
                    var importClub = ResultsClubCombo?.SelectedItem?.ToString() ?? string.Empty;
                    var importVenue = ResultsVenueCombo?.SelectedItem?.ToString() ?? string.Empty;

                    int imported = 0, failed = 0;
                    var errors = new List<string>();

                    foreach (var cb in checkBoxes)
                    {
                        if (cb.IsChecked != true) continue;
                        var tag = ((string Name, string Points, string Handicap, string Raw, int Position))cb.Tag;
                        try
                        {
                            int.TryParse(tag.Handicap, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var hParsed);
                            int.TryParse(tag.Points, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sParsed);

                            var rec = new ResultRecord
                            {
                                Id = Guid.NewGuid().ToString(),
                                Date = importDate,
                                Club = importClub,
                                Venue = importVenue,
                                PlayerName = tag.Name,
                                Partner = string.Empty,
                                Hcp = hParsed,
                                Result = sParsed,
                                Position = tag.Position
                            };

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