// MainWindow.CreateGame.cs
//===========================
// VERSION: 2024-12-21 22:00 UTC - Fixed player counts per division
// BUILD TIMESTAMP: 2024-12-21 22:00:00 UTC

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GolfApp1
{
    public sealed partial class MainWindow
    {
        private const string BUILD_TIMESTAMP = "2024-12-21 22:00:00 UTC";

        // XLSX FILE STRUCTURE - Update these if the file format changes
        private static class XlsxStructure
        {
            public const int ClubNameRow = 2;
            public const int DivisionAStartRow = 6;
            public const int DivisionAEndRow = 8;     // Division A: 3 players (rows 6-8)
            public const int DivisionBStartRow = 12;
            public const int DivisionBEndRow = 14;    // Division B: 3 players (rows 12-14)
            public const int DivisionCStartRow = 18;
            public const int DivisionCEndRow = 19;    // Division C: 2 players (rows 18-19)
            public const int MaxDataRow = 20;         // Stop parsing after this row
        }

        private class GameProposal
        {
            public string ClubName { get; set; } = string.Empty;
            public List<ProposedPlayer> DivisionA { get; set; } = new();
            public List<ProposedPlayer> DivisionB { get; set; } = new();
            public List<ProposedPlayer> DivisionC { get; set; } = new();
            public string DebugInfo { get; set; } = string.Empty;
        }

        private class ProposedPlayer
        {
            public string Name { get; set; } = string.Empty;
            public string HcpIndex { get; set; } = string.Empty;
            public string NationalId { get; set; } = string.Empty;
            public string Division { get; set; } = string.Empty;
        }

        private async void OnCreateGameClicked(object sender, RoutedEventArgs e)
        {
            UpdateStatus($"[Build: {BUILD_TIMESTAMP}] Starting...");

            if (_db is null)
            {
                UpdateStatus("Database not initialized.");
                await ShowErrorAsync("Create Game", "Database not initialized.");
                return;
            }

            try
            {
                UpdateStatus($"[Build: {BUILD_TIMESTAMP}] Select XLSX file...");

                var file = await PickXlsxFileAsync();
                if (file == null)
                {
                    UpdateStatus("Create Game cancelled.");
                    return;
                }

                UpdateStatus($"[Build: {BUILD_TIMESTAMP}] Parsing: {file.Name}...");

                var gameProposal = await ParseGameProposalFileAsync(file);
                if (gameProposal == null)
                {
                    UpdateStatus($"[Build: {BUILD_TIMESTAMP}] Parse failed.");
                    return;
                }

                var totalPlayers = gameProposal.DivisionA.Count + gameProposal.DivisionB.Count + gameProposal.DivisionC.Count;

                if (totalPlayers == 0)
                {
                    UpdateStatus($"[Build: {BUILD_TIMESTAMP}] No players - showing debug.");
                    await ShowCompleteDebugInfoAsync(gameProposal);
                    return;
                }

                UpdateStatus($"[Build: {BUILD_TIMESTAMP}] Success: {gameProposal.ClubName} - {totalPlayers} players");
                await ShowGameProposalPreviewAsync(gameProposal);
            }
            catch (Exception ex)
            {
                UpdateStatus($"[Build: {BUILD_TIMESTAMP}] ERROR: {ex.Message}");
                await ShowErrorAsync("Error", $"{ex.Message}\n\n{ex.StackTrace}");
            }
        }

        private async Task<StorageFile?> PickXlsxFileAsync()
        {
            try
            {
                var picker = new FileOpenPicker();
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add(".xlsx");
                return await picker.PickSingleFileAsync().AsTask();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Picker error: {ex.Message}");
                return null;
            }
        }

        private async Task<GameProposal?> ParseGameProposalFileAsync(StorageFile file)
        {
            var debugLog = new StringBuilder();
            var proposal = new GameProposal();

            try
            {
                ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                await using var stream = await file.OpenStreamForReadAsync();
                using var package = new ExcelPackage(stream);

                if (package.Workbook.Worksheets.Count == 0)
                {
                    debugLog.AppendLine("ERROR: No worksheets found!");
                    proposal.DebugInfo = debugLog.ToString();
                    return proposal;
                }

                var worksheet = package.Workbook.Worksheets[0];
                var maxRow = worksheet.Dimension?.End.Row ?? 0;

                debugLog.AppendLine($"=== EXCEL FILE ({worksheet.Name}) ===");
                debugLog.AppendLine($"Total Rows: {maxRow}");
                debugLog.AppendLine($"XLSX Structure Configuration:");
                debugLog.AppendLine($"  Club Name Row: {XlsxStructure.ClubNameRow}");
                debugLog.AppendLine($"  Division A: Rows {XlsxStructure.DivisionAStartRow}-{XlsxStructure.DivisionAEndRow} (3 players max)");
                debugLog.AppendLine($"  Division B: Rows {XlsxStructure.DivisionBStartRow}-{XlsxStructure.DivisionBEndRow} (3 players max)");
                debugLog.AppendLine($"  Division C: Rows {XlsxStructure.DivisionCStartRow}-{XlsxStructure.DivisionCEndRow} (2 players max)");
                debugLog.AppendLine($"  Max Data Row: {XlsxStructure.MaxDataRow}\n");

                // Dump first 30 rows for debugging
                debugLog.AppendLine("=== FIRST 30 ROWS ===");
                for (int i = 1; i <= Math.Min(30, maxRow); i++)
                {
                    var c1 = worksheet.Cells[i, 1].Text?.Trim() ?? "(empty)";
                    var c2 = worksheet.Cells[i, 2].Text?.Trim() ?? "(empty)";
                    var c3 = worksheet.Cells[i, 3].Text?.Trim() ?? "(empty)";
                    var c4 = worksheet.Cells[i, 4].Text?.Trim() ?? "(empty)";
                    debugLog.AppendLine($"Row {i}: [{c1}] | [{c2}] | [{c3}] | [{c4}]");
                }
                debugLog.AppendLine();

                // Parse club name from configured row
                debugLog.AppendLine($"=== CLUB NAME (Row {XlsxStructure.ClubNameRow}) ===");
                if (maxRow < XlsxStructure.ClubNameRow)
                {
                    debugLog.AppendLine("ERROR: File too short!");
                    proposal.ClubName = "Unknown Club";
                    proposal.DebugInfo = debugLog.ToString();
                    return proposal;
                }

                var r2c1 = worksheet.Cells[XlsxStructure.ClubNameRow, 1].Text?.Trim();
                var r2c2 = worksheet.Cells[XlsxStructure.ClubNameRow, 2].Text?.Trim();
                debugLog.AppendLine($"Col1: '{r2c1}' | Col2: '{r2c2}'");

                string? clubName = null;
                if (!string.IsNullOrWhiteSpace(r2c2) && r2c2.Length > 3 && !r2c2.ToUpperInvariant().Contains("PLAYER"))
                    clubName = r2c2;
                else if (!string.IsNullOrWhiteSpace(r2c1) && r2c1.Length > 3 && !r2c1.ToUpperInvariant().Contains("PLAYER"))
                    clubName = r2c1;

                proposal.ClubName = clubName ?? "Unknown Club";
                debugLog.AppendLine($"Found: '{proposal.ClubName}'\n");

                // Parse divisions using configured row positions
                debugLog.AppendLine("=== PARSING DIVISIONS (Using Fixed Positions) ===");

                // Division A: rows 6-8 (3 players max)
                debugLog.AppendLine($"Division A: Rows {XlsxStructure.DivisionAStartRow}-{XlsxStructure.DivisionAEndRow}");
                ParseDivisionPlayersFixed(worksheet, XlsxStructure.DivisionAStartRow, XlsxStructure.DivisionAEndRow, "A", proposal.DivisionA, debugLog);

                // Division B: rows 12-14 (3 players max)
                debugLog.AppendLine($"Division B: Rows {XlsxStructure.DivisionBStartRow}-{XlsxStructure.DivisionBEndRow}");
                ParseDivisionPlayersFixed(worksheet, XlsxStructure.DivisionBStartRow, XlsxStructure.DivisionBEndRow, "B", proposal.DivisionB, debugLog);

                // Division C: rows 18-19 (2 players max)
                debugLog.AppendLine($"Division C: Rows {XlsxStructure.DivisionCStartRow}-{XlsxStructure.DivisionCEndRow}");
                ParseDivisionPlayersFixed(worksheet, XlsxStructure.DivisionCStartRow, XlsxStructure.DivisionCEndRow, "C", proposal.DivisionC, debugLog);

                debugLog.AppendLine("\n=== SUMMARY ===");
                debugLog.AppendLine($"Club: {proposal.ClubName}");
                debugLog.AppendLine($"Div A: {proposal.DivisionA.Count} players");
                debugLog.AppendLine($"Div B: {proposal.DivisionB.Count} players");
                debugLog.AppendLine($"Div C: {proposal.DivisionC.Count} players");
                debugLog.AppendLine($"Total: {proposal.DivisionA.Count + proposal.DivisionB.Count + proposal.DivisionC.Count} players");

                proposal.DebugInfo = debugLog.ToString();
                return proposal;
            }
            catch (Exception ex)
            {
                debugLog.AppendLine($"\nEXCEPTION: {ex.Message}");
                proposal.DebugInfo = debugLog.ToString();
                return proposal;
            }
        }

        private void ParseDivisionPlayersFixed(ExcelWorksheet worksheet, int startRow, int stopRow, string division, List<ProposedPlayer> playerList, StringBuilder debugLog)
        {
            debugLog.AppendLine($"  Parsing rows {startRow}-{stopRow} for Division {division}");

            for (int row = startRow; row <= stopRow; row++)
            {
                var name = worksheet.Cells[row, 2].Text?.Trim();
                var id = worksheet.Cells[row, 4].Text?.Trim() ?? "";

                // Skip if name is empty OR National ID is empty
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
                {
                    debugLog.AppendLine($"  Row {row}: (empty name or ID - skipped)");
                    continue;
                }

                var hcp = worksheet.Cells[row, 3].Text?.Trim() ?? "";
                var cleanId = new string(id.Where(char.IsDigit).ToArray());

                if (cleanId.Length == 6)
                    id = cleanId;
                else if (!string.IsNullOrWhiteSpace(id))
                    id = $"{id} (!)";

                playerList.Add(new ProposedPlayer { Name = name, HcpIndex = hcp, NationalId = id, Division = division });
                debugLog.AppendLine($"  Row {row}: {name} - HCP:{hcp} - ID:{id}");
            }

            debugLog.AppendLine($"  Division {division}: Added {playerList.Count(p => p.Division == division)} players\n");
        }

        private async Task ShowGameProposalPreviewAsync(GameProposal proposal)
        {
            var totalPlayers = proposal.DivisionA.Count + proposal.DivisionB.Count + proposal.DivisionC.Count;
            bool keepShowing = true;

            while (keepShowing)
            {
                var dialog = new ContentDialog
                {
                    Title = "Parsed Data",
                    Content = CreatePreviewContent(proposal, totalPlayers),
                    PrimaryButtonText = "View Clean",
                    SecondaryButtonText = "View Debug",
                    CloseButtonText = "Done",
                    XamlRoot = this.Content?.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                    await ShowActualResultsAsync(proposal);
                else if (result == ContentDialogResult.Secondary)
                    await ShowCompleteDebugInfoAsync(proposal);
                else
                    keepShowing = false;
            }
        }

        private ScrollViewer CreatePreviewContent(GameProposal proposal, int totalPlayers)
        {
            var panel = new StackPanel { Spacing = 16, Padding = new Thickness(8) };
            panel.Children.Add(new TextBlock { Text = $"Club: {proposal.ClubName}", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            panel.Children.Add(new TextBlock { Text = $"Total: {totalPlayers} (A:{proposal.DivisionA.Count}, B:{proposal.DivisionB.Count}, C:{proposal.DivisionC.Count})", FontSize = 14 });

            if (proposal.DivisionA.Count > 0) panel.Children.Add(CreateDivisionPreviewPanel("Division A", proposal.DivisionA));
            if (proposal.DivisionB.Count > 0) panel.Children.Add(CreateDivisionPreviewPanel("Division B", proposal.DivisionB));
            if (proposal.DivisionC.Count > 0) panel.Children.Add(CreateDivisionPreviewPanel("Division C", proposal.DivisionC));

            return new ScrollViewer { Content = panel, MaxHeight = 600, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement CreateDivisionPreviewPanel(string divisionName, List<ProposedPlayer> players)
        {
            var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(new TextBlock { Text = divisionName, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold });

            foreach (var player in players)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                row.Children.Add(new TextBlock { Text = $"{players.IndexOf(player) + 1}.", Width = 30 });
                row.Children.Add(new TextBlock { Text = player.Name, Width = 250 });
                row.Children.Add(new TextBlock { Text = $"HCP:{player.HcpIndex}", Width = 80 });
                row.Children.Add(new TextBlock { Text = $"ID:{player.NationalId}", Width = 120 });
                panel.Children.Add(row);
            }

            return panel;
        }

        private async Task ShowActualResultsAsync(GameProposal proposal)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"CLUB: {proposal.ClubName}\n");

            if (proposal.DivisionA.Count > 0)
            {
                sb.AppendLine("=== DIVISION A ===");
                foreach (var p in proposal.DivisionA)
                    sb.AppendLine($"{proposal.DivisionA.IndexOf(p) + 1}. {p.Name} - HCP:{p.HcpIndex} - ID:{p.NationalId}");
                sb.AppendLine();
            }

            if (proposal.DivisionB.Count > 0)
            {
                sb.AppendLine("=== DIVISION B ===");
                foreach (var p in proposal.DivisionB)
                    sb.AppendLine($"{proposal.DivisionB.IndexOf(p) + 1}. {p.Name} - HCP:{p.HcpIndex} - ID:{p.NationalId}");
                sb.AppendLine();
            }

            if (proposal.DivisionC.Count > 0)
            {
                sb.AppendLine("=== DIVISION C ===");
                foreach (var p in proposal.DivisionC)
                    sb.AppendLine($"{proposal.DivisionC.IndexOf(p) + 1}. {p.Name} - HCP:{p.HcpIndex} - ID:{p.NationalId}");
            }

            var dialog = new ContentDialog
            {
                Title = "Clean Results",
                Content = new ScrollViewer
                {
                    Content = new TextBlock { Text = sb.ToString(), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), IsTextSelectionEnabled = true },
                    MaxHeight = 700
                },
                CloseButtonText = "Close",
                XamlRoot = this.Content?.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async Task ShowCompleteDebugInfoAsync(GameProposal proposal)
        {
            var dialog = new ContentDialog
            {
                Title = "Debug Info",
                Content = new ScrollViewer
                {
                    Content = new TextBlock { Text = proposal.DebugInfo, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 10, IsTextSelectionEnabled = true },
                    MaxHeight = 700
                },
                CloseButtonText = "Close",
                XamlRoot = this.Content?.XamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}
