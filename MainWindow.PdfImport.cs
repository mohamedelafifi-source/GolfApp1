

//MainWindow.PdfImport.cs
//============================
using System;
using System.Linq;
using System.Threading.Tasks;
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

        // Preview-only (does not save) — now shows Name | Points | Handicap for review.
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

                // Build simple header + rows showing only Name | Points | Handicap
                var panel = new StackPanel { Spacing = 6 };

                // Header row
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

                // Rows (limit to avoid very tall dialog; user can re-run if needed)
                var toShow = parsed.Take(200).ToList();
                foreach (var p in toShow)
                {
                    var name = string.IsNullOrWhiteSpace(p.Name) ? "—" : p.Name;
                    var points = string.IsNullOrWhiteSpace(p.Result) ? "—" : p.Result;
                    var hc = string.IsNullOrWhiteSpace(p.HandicapIndex) ? "—" : p.HandicapIndex;

                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                    row.Children.Add(new TextBlock
                    {
                        Text = name,
                        Width = 420,
                        TextWrapping = TextWrapping.Wrap
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = points,
                        Width = 80,
                        TextAlignment = TextAlignment.Center
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = hc,
                        Width = 100,
                        TextAlignment = TextAlignment.Center
                    });
                    panel.Children.Add(row);
                }

                var scroll = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 540
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
                UpdateStatus($"Previewed {parsed.Count} parsed lines from '{file.Name}'. No data was saved.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Preview failed: " + ex.Message);
                await ShowErrorAsync("Preview failed", ex.Message);
            }
        }
    }
}