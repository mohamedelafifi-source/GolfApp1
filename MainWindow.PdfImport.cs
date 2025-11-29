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
        // Called from Results menu. Ensure this is the only OnImportPdfClicked in the project.
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

        // Preview-only (does not save)
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

                var panel = new StackPanel { Spacing = 6 };
                panel.Children.Add(new TextBlock
                {
                    Text = $"Parsed {parsed.Count} lines. Sample (first {Math.Min(50, parsed.Count)}):",
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });

                foreach (var p in parsed.Take(50))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"Page {p.Page}  Confidence {p.Confidence:F2}  →  {p.RawLine}",
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                var scroll = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 540
                };

                var previewDlg = new ContentDialog
                {
                    Title = "PDF Parse Preview",
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