using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        // Called from the "Bulk Add" button in the UI.
        private async void OnBulkAddClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Select file for bulk add (CSV/TXT) ...");

            var file = await PickSingleFileAsync(new[] { ".csv", ".txt" });
            if (file is null)
            {
                UpdateStatus("No file selected.");
                return;
            }

            UpdateStatus($"Selected bulk-file: {file.Name}");

            try
            {
                var text = await Windows.Storage.FileIO.ReadTextAsync(file);

                // Show confirmation (optional)
                await ShowErrorAsync("File Selected", $"File '{file.Name}' selected ({text.Length} bytes).");

                // Determine club short name to import into.
                // Prefer the current short name in the UI; otherwise ask the user to enter one.
                var clubShort = ShortNameTextBox.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(clubShort))
                {
                    var inputBox = new TextBox { PlaceholderText = "Enter 4-char club short name" };
                    var dlg = new ContentDialog
                    {
                        Title = "Select Club",
                        Content = new StackPanel
                        {
                            Children =
                            {
                                new TextBlock { Text = "No club selected in the editor. Enter the club short name to import into:", TextWrapping = TextWrapping.Wrap },
                                inputBox
                            },
                            Spacing = 8
                        },
                        PrimaryButtonText = "OK",
                        CloseButtonText = "Cancel",
                        XamlRoot = this.Content?.XamlRoot
                    };

                    var result = ContentDialogResult.None;
                    if (this.Content?.XamlRoot != null) result = await dlg.ShowAsync();
                    if (result != ContentDialogResult.Primary)
                    {
                        UpdateStatus("Import cancelled (no club selected).");
                        return;
                    }

                    clubShort = inputBox.Text?.Trim() ?? string.Empty;
                    if (clubShort.Length != 4)
                    {
                        UpdateStatus("Club short name must be exactly 4 characters.");
                        await ShowErrorAsync("Invalid Club", "Club short name must be exactly 4 characters (e.g. ABCD).");
                        return;
                    }
                }

                // Hand off to the existing parser/import flow (will prompt Auto Add or Review).
                await ParseAndBulkAddAsync(text, clubShort);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("File Open Failed", ex.Message);
            }
        }
    }
}