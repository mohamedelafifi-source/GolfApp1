
//MainWindow.PdfImport.cs
//============================
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using GolfApp1.Services;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
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
        // Extract Name, Points and Handicap (show handicap in parentheses).
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

                // Pattern: starts with position number, name, points (or WD/DQ/DNS), then (handicap)
                // Notes:
                //  - points group accepts negative numbers or WD/DQ/DNS
                //  - hc group accepts optional sign and leading zeros, e.g. -01 or 01 or 17 or 17.5
                var entryRx = new Regex(
                    @"^\s*\d+\s+(?<name>.+?)\s+(?<points>-?\d+|WD|DQ|DNS)(?:\s*pts?)?\s*\(\s*(?<hc>[+-]?\d{1,2}(?:\.\d)?)\s*\)",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);

                // Filter parsed lines: must start with number and contain a closing parenthesis.
                var matches = parsed
                    .Select(p => new { Raw = (p.RawLine ?? string.Empty).Trim(), Parsed = p })
                    .Where(x => Regex.IsMatch(x.Raw, @"^\s*\d") && x.Raw.Contains(')'))
                    .Select(x =>
                    {
                        // Truncate at first closing parenthesis (include it) to ignore trailing noise
                        var idx = x.Raw.IndexOf(')');
                        var truncated = idx >= 0 ? x.Raw.Substring(0, idx + 1) : x.Raw;

                        var m = entryRx.Match(truncated);
                        if (m.Success)
                        {
                            var name = Regex.Replace(m.Groups["name"].Value.Trim(), @"\s+", " ");
                            var points = m.Groups["points"].Value.Trim();
                            var hc = m.Groups["hc"].Value.Trim();

                            // Ensure handicap is shown in parentheses exactly as in source (preserve sign/leading zeros)
                            var hcDisplay = $"({hc})";
                            return new { Name = name, Points = points, Handicap = hcDisplay, ScoreConfidence = 1.0 };
                        }

                        // Fallback to parsed.Record fields if regex didn't match the truncated text,
                        // but still require that the original raw line started with a number and had ')'.
                        var fallbackName = string.IsNullOrWhiteSpace(x.Parsed.Name) ? Regex.Replace(truncated, @"\s+", " ").Trim() : x.Parsed.Name;
                        var fallbackPoints = string.IsNullOrWhiteSpace(x.Parsed.Result) ? "—" : x.Parsed.Result;
                        var fallbackHc = string.IsNullOrWhiteSpace(x.Parsed.HandicapIndex) ? "—" : $"({x.Parsed.HandicapIndex})";
                        return new { Name = fallbackName, Points = fallbackPoints, Handicap = fallbackHc, ScoreConfidence = x.Parsed.Confidence };
                    })
                    .ToList();

                if (matches.Count == 0)
                {
                    UpdateStatus("No matching result lines found (format: leading number and closing ')' ).");
                    await ShowErrorAsync("No Matches", "No lines matched the required format (e.g. \"1 Mohamed Kabbani 42 pts (17)\").");
                    return;
                }

                // Build UI: header + rows with Name | Points | Handicap
                var panel = new StackPanel { Spacing = 6 };

                var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                header.Children.Add(new TextBlock
                {
                    Text = "Name",
                    Width = 420,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                header.Children.Add(new TextBlock
                {
                    Text = "Points",
                    Width = 80,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center
                });
                header.Children.Add(new TextBlock
                {
                    Text = "Handicap",
                    Width = 100,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center
                });
                panel.Children.Add(header);

                // Show extracted rows (limit to 500)
                foreach (var p in matches.Take(500))
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                    row.Children.Add(new TextBlock
                    {
                        Text = p.Name,
                        Width = 420,
                        TextWrapping = TextWrapping.Wrap
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = p.Points,
                        Width = 80,
                        TextAlignment = TextAlignment.Center
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = p.Handicap,
                        Width = 100,
                        TextAlignment = TextAlignment.Center
                    });
                    panel.Children.Add(row);
                }

                var scroll = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 640
                };

                var previewDlg = new ContentDialog
                {
                    Title = $"PDF Parse Preview — {file.Name}",
                    Content = scroll,
                    PrimaryButtonText = "Close",
                    CloseButtonText = null,
                    XamlRoot = this.Content?.XamlRoot
                };

                if (this.Content?.XamlRoot == null)
                {
                    UpdateStatus($"Parsed {parsed.Count} lines (preview unavailable).");
                    return;
                }

                await previewDlg.ShowAsync().AsTask();
                UpdateStatus($"Previewed {matches.Count} matching lines from '{file.Name}'. No data was saved.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Preview failed: " + ex.Message);
                await ShowErrorAsync("Preview failed", ex.Message);
            }
        }
    }
}