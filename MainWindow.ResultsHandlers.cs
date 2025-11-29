using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        // Re-added Results menu functionality.
        private async void OnResultsClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Results menu opened.");

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = "Choose an action for results:",
                TextWrapping = TextWrapping.Wrap
            });

            var dlg = new ContentDialog
            {
                Title = "Results",
                Content = panel,
                PrimaryButtonText = "Import PDF",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot == null)
            {
                UpdateStatus("Results action: UI unavailable.");
                return;
            }

            // IMPORTANT: ShowAsync() returns IAsyncOperation<T> — use AsTask() before awaiting
            var result = await dlg.ShowAsync().AsTask();
            if (result == ContentDialogResult.Primary)
            {
                await InvokeImportHandlerAsync();
            }
            else
            {
                UpdateStatus("Results action cancelled.");
            }
        }

        private Task InvokeImportHandlerAsync()
        {
            OnImportPdfClicked(this, new RoutedEventArgs());
            return Task.CompletedTask;
        }
    }
}