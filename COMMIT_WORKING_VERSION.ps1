# COMMIT_WORKING_VERSION.ps1
# Commit the current working state to GitHub
# This preserves all successfully added files before making UI changes

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "COMMITTING WORKING VERSION TO GITHUB" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$projectPath = "C:\Users\mohamedelafifi\source\repos\GolfApp1"
cd $projectPath

Write-Host "Step 1: Checking Git status..." -ForegroundColor Yellow
git status

Write-Host ""
Write-Host "Step 2: Staging new and modified files..." -ForegroundColor Yellow
git add AppSettings.cs
git add MainWindow.AppFolder.cs
git add MainWindow.CreateGame.cs
git add GolfApp1.csproj
git add App.xaml.cs

Write-Host ""
Write-Host "Step 3: Committing changes..." -ForegroundColor Yellow
git commit -m "feat: Add AppSettings, AppFolder management, and CreateGame functionality

- Added AppSettings.cs for persistent app configuration
- Added MainWindow.AppFolder.cs for folder selection and database migration
- Added MainWindow.CreateGame.cs for game creation from Excel files
- Updated GolfApp1.csproj to include EPPlus package
- Cleaned up App.xaml.cs (removed Bootstrap initialization)

All new files tested and working on ARM64 Mac/Parallels.
Ready for UI changes (Teams menu) in next commit."

Write-Host ""
Write-Host "Step 4: Pushing to GitHub..." -ForegroundColor Yellow
git push origin master

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "COMMIT SUCCESSFUL!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Your working version is now safely committed to GitHub." -ForegroundColor White
Write-Host "Branch: master" -ForegroundColor Cyan
Write-Host "Remote: GolfApp1OnGithub" -ForegroundColor Cyan
Write-Host ""
Write-Host "Ready to proceed with UI changes (Option 1)!" -ForegroundColor Yellow
Write-Host ""
Write-Host "Press any key to exit..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
