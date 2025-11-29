using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GolfApp1.Services;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {

        private async Task ImportPdfAtPathAsync(string filePath)
        {
            try
            {
                UpdateStatus("Importing PDF...");
                var svc = new ResultsImportService();
                var parsed = await svc.ParsePdfAsync(filePath);

                UpdateStatus($"Imported {parsed.Count} candidate lines (page sample: {parsed.Select(p => p.Page).Distinct().Take(3).DefaultIfEmpty(0).Aggregate((a, b) => a)})");
                // TODO: inspect 'parsed' and map to your Player/Club model or show preview.
            }
            catch (Exception ex)
            {
                UpdateStatus($"Import failed: {ex.Message}");
                await ShowErrorAsync("Import failed", ex.Message);
            }
        }
    }
}