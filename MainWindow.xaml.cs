using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ReliableMenuApp
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "Reliable WinUI Menu App";
        }

        // --- Utility Method to update status ---
        private void UpdateStatus(string message)
        {
            // StatusLabel is a TextBlock control in WinUI
            StatusLabel.Text = message;
        }

        // --- File Menu Handlers ---

        private void OnFileNewClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Action: File -> New was executed.");
        }

        private void OnFileOpenClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Action: File -> Open was executed.");
        }

        private void OnFileExitClicked(object sender, RoutedEventArgs e)
        {
            // Standard WinUI method to close the application window
            UpdateStatus("Action: Exiting Application...");
            this.Close();
        }

        // --- Edit Menu Handlers ---

        private void OnEditSettingsClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Action: Edit -> Settings was clicked.");
        }
    }
}