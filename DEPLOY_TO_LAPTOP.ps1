# DEPLOY_TO_LAPTOP.ps1
# Automated script to safely prepare GolfApp1 for Windows laptop deployment
# Run this script on your Mac/Parallels BEFORE copying to laptop

param(
    [string]$UsbDrive = "E:",  # Change this to your USB drive letter
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "GOLFAPP1 SAFE DEPLOYMENT TO LAPTOP" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$projectPath = "C:\Users\mohamedelafifi\source\repos\GolfApp1"
$testPath = "C:\Users\mohamedelafifi\source\repos\GolfApp1_ReleaseTest"
$deployPath = "C:\Users\mohamedelafifi\source\repos\GolfApp1_Deploy"

# ============================================
# PHASE 1: Create Test Environment
# ============================================
Write-Host "PHASE 1: Creating test environment..." -ForegroundColor Yellow
Write-Host ""

if (Test-Path $testPath) {
    Write-Host "Removing old test folder..." -ForegroundColor Gray
    Remove-Item $testPath -Recurse -Force
}

Write-Host "Copying project to test folder..." -ForegroundColor Gray
Copy-Item -Path $projectPath -Destination $testPath -Recurse -Force

Write-Host "Cleaning test folder..." -ForegroundColor Gray
Remove-Item -Path "$testPath\bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$testPath\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$testPath\.vs" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "? Test environment created" -ForegroundColor Green
Write-Host ""

# ============================================
# PHASE 2: Build for Windows x64
# ============================================
Write-Host "PHASE 2: Building for Windows x64..." -ForegroundColor Yellow
Write-Host ""

cd $testPath

Write-Host "Cleaning solution..." -ForegroundColor Gray
dotnet clean -c Release -v quiet

Write-Host "Building x64 Release..." -ForegroundColor Gray
$buildResult = dotnet build -c Release -p:Platform=x64 -v minimal 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "? BUILD FAILED!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Build output:" -ForegroundColor Red
    Write-Host $buildResult
    Write-Host ""
    Write-Host "FIX BUILD ERRORS BEFORE DEPLOYING!" -ForegroundColor Red
    exit 1
}

Write-Host "? Build succeeded" -ForegroundColor Green
Write-Host ""

# ============================================
# PHASE 3: Verify Output Files
# ============================================
Write-Host "PHASE 3: Verifying output files..." -ForegroundColor Yellow
Write-Host ""

$outputPath = "$testPath\bin\x64\Release\net8.0-windows10.0.19041.0"
$exePath = "$outputPath\GolfApp1.exe"

$criticalFiles = @(
    "GolfApp1.exe",
    "GolfApp1.dll",
    "Microsoft.ui.xaml.dll",
    "Microsoft.Data.Sqlite.dll",
    "AppSettings.dll",
    "GolfApp1.runtimeconfig.json"
)

$allFilesPresent = $true
foreach ($file in $criticalFiles) {
    $filePath = Join-Path $outputPath $file
    if (Test-Path $filePath) {
        $size = (Get-Item $filePath).Length
        Write-Host "  ? $file ($size bytes)" -ForegroundColor Green
    } else {
        Write-Host "  ? $file MISSING!" -ForegroundColor Red
        $allFilesPresent = $false
    }
}

Write-Host ""

if (-not $allFilesPresent) {
    Write-Host "? CRITICAL FILES MISSING! Cannot deploy." -ForegroundColor Red
    exit 1
}

Write-Host "? All critical files present" -ForegroundColor Green
Write-Host ""

# ============================================
# PHASE 4: Test Run (Optional)
# ============================================
if (-not $SkipTests) {
    Write-Host "PHASE 4: Test run (optional)..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "You should now:" -ForegroundColor Cyan
    Write-Host "1. Open Visual Studio" -ForegroundColor White
    Write-Host "2. Open $testPath\GolfApp1.sln" -ForegroundColor White
    Write-Host "3. Set Platform to x64, Configuration to Release" -ForegroundColor White
    Write-Host "4. Press F5 to test" -ForegroundColor White
    Write-Host ""
    Write-Host "Press Enter when testing is complete (or Ctrl+C to abort)..." -ForegroundColor Yellow
    Read-Host
}

# ============================================
# PHASE 5: Create Deployment Package
# ============================================
Write-Host "PHASE 5: Creating deployment package..." -ForegroundColor Yellow
Write-Host ""

if (Test-Path $deployPath) {
    Write-Host "Removing old deployment folder..." -ForegroundColor Gray
    Remove-Item $deployPath -Recurse -Force
}

Write-Host "Copying output files..." -ForegroundColor Gray
Copy-Item -Path $outputPath -Destination $deployPath -Recurse -Force

$deploySize = (Get-ChildItem $deployPath -Recurse | Measure-Object -Property Length -Sum).Sum
$deploySizeMB = [math]::Round($deploySize / 1MB, 2)

Write-Host "? Deployment package created ($deploySizeMB MB)" -ForegroundColor Green
Write-Host ""

# ============================================
# PHASE 6: Copy to USB (Optional)
# ============================================
if ($UsbDrive -ne "" -and (Test-Path $UsbDrive)) {
    Write-Host "PHASE 6: Copying to USB drive ($UsbDrive)..." -ForegroundColor Yellow
    Write-Host ""
    
    $usbDeployPath = Join-Path $UsbDrive "GolfApp1_Deploy"
    
    if (Test-Path $usbDeployPath) {
        Write-Host "Removing old deployment from USB..." -ForegroundColor Gray
        Remove-Item $usbDeployPath -Recurse -Force
    }
    
    Write-Host "Copying to USB..." -ForegroundColor Gray
    Copy-Item -Path $deployPath -Destination $usbDeployPath -Recurse -Force
    
    Write-Host "? Copied to USB: $usbDeployPath" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "PHASE 6: USB copy skipped (drive not found or not specified)" -ForegroundColor Yellow
    Write-Host ""
}

# ============================================
# SUMMARY
# ============================================
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "DEPLOYMENT PACKAGE READY!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Package location: $deployPath" -ForegroundColor White
Write-Host "Package size: $deploySizeMB MB" -ForegroundColor White
Write-Host ""

if ($UsbDrive -ne "" -and (Test-Path (Join-Path $UsbDrive "GolfApp1_Deploy"))) {
    Write-Host "USB copy: $UsbDrive\GolfApp1_Deploy" -ForegroundColor White
    Write-Host ""
    Write-Host "NEXT STEPS:" -ForegroundColor Cyan
    Write-Host "1. Take USB drive to Windows laptop" -ForegroundColor White
    Write-Host "2. Copy GolfApp1_Deploy folder to laptop (e.g., C:\Apps\GolfApp1)" -ForegroundColor White
    Write-Host "3. Run GolfApp1.exe from that folder" -ForegroundColor White
    Write-Host "4. TEST EVERYTHING before demo!" -ForegroundColor Yellow
} else {
    Write-Host "USB not used. Manually copy deployment package to laptop." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "NEXT STEPS:" -ForegroundColor Cyan
    Write-Host "1. Copy $deployPath to USB drive or network share" -ForegroundColor White
    Write-Host "2. Transfer to Windows laptop" -ForegroundColor White
    Write-Host "3. Run GolfApp1.exe" -ForegroundColor White
    Write-Host "4. TEST EVERYTHING before demo!" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "REMEMBER: TEST ON LAPTOP BEFORE DEMO!" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to exit..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
