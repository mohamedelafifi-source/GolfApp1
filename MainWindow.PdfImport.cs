
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

        // Preview-only (does not save) — now shows only lines that start with a number and contain a trailing ")"
        // Extracts Name, Points and Handicap and ignores anything after the closing parenthesis.
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

                // Regex: start with number, then name, then points, optional "pts", then "(" handicap ")".
                // We stop parsing at the closing parenthesis and ignore anything that follows.
                var entryRx = new Regex(
                    @"^\s*\d+\s+(?<name>.+?)\s+(?<points>\d+)(?:\s*pts?)?\s*\(\s*(?<hc>\d{1,2}(?:\.\d)?)\s*\)",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);

                // Filter parsed lines using RawLine (the original extracted text)
                var matches = parsed
                    .Select(p => p.RawLine ?? string.Empty)
                    .Select(line => new { Line = line, Match = entryRx.Match(line) })
                    .Where(x => x.Match.Success)
                    .Select(x => new
                    {
                        Name = Regex.Replace(x.Match.Groups["name"].Value.Trim(), @"\s+", " "),
                        Points = x.Match.Groups["points"].Value.Trim(),
                        Handicap = x.Match.Groups["hc"].Value.Trim()
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

                // Show extracted rows (limit to 500 to keep dialog responsive)
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