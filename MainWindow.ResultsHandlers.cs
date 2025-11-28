using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        // Temporary: make invocation obvious and debuggable.
        private async void OnResultsClicked(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("OnResultsClicked invoked");
            UpdateStatus("Results clicked (handler invoked).");

            // Show a simple dialog so you can see the handler ran.
            var dlg = new ContentDialog
            {
                Title = "Debug",
                Content = "OnResultsClicked handler invoked.",
                CloseButtonText = "OK",
                XamlRoot = this.Content?.XamlRoot
            };

            if (this.Content?.XamlRoot != null)
            {
                await dlg.ShowAsync();
            }
        }
    }
}