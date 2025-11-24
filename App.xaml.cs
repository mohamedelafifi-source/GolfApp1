using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.DynamicDependency; // REQUIRED for Bootstrapper
using Windows.ApplicationModel.Activation;

namespace GolfApp1
{
    public partial class App : Application
    {
        public App()
        {
            // --- BOOTSTRAPPER INITIALIZATION ---
            // This must be called before InitializeComponent() for unpackaged apps.
            try
            {
                // 0x00010005 corresponds to Windows App SDK version 1.5
                Bootstrap.Initialize(0x00010005);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Windows App SDK initialization failed: {ex.Message}");
                // You may want to notify the user here that the runtime is missing.
            }
            // -----------------------------------

            this.InitializeComponent();

            this.UnhandledException += App_UnhandledException;
        }

        private void App_UnhandledException(object? sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Debug.WriteLine("Unhandled exception: " + e.Exception?.ToString());
            e.Handled = true;
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            base.OnLaunched(args);
            // Assuming your main window class is named MainWindow
            var wnd = new MainWindow();
            wnd.Activate();
        }

        // REMOVED: protected override void OnExit() {...}
        // This method does not exist in Microsoft.UI.Xaml.Application.
    }
}