$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $projectDir "src"
$outExe = Join-Path $projectDir "ClearVestige.exe"

$fwDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$wpfDir = Join-Path $fwDir "WPF"
$csc = Join-Path $fwDir "csc.exe"

if (-not (Test-Path $csc)) {
    Write-Error "Compilateur C# introuvable : $csc"
    exit 1
}

$sourceFiles = Get-ChildItem -Path $srcDir -Filter "*.cs" | ForEach-Object { $_.FullName }

$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/out:$outExe",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Xaml.dll",
    "/r:System.Windows.Forms.dll",
    "/r:Microsoft.VisualBasic.dll",
    "/r:$(Join-Path $wpfDir 'WindowsBase.dll')",
    "/r:$(Join-Path $wpfDir 'PresentationCore.dll')",
    "/r:$(Join-Path $wpfDir 'PresentationFramework.dll')"
)

# Icône de l'application (fichier .exe + fenêtres + barre des tâches)
$iconFile = Join-Path $projectDir "assets\app.ico"
if (Test-Path $iconFile) {
    $cscArgs += "/win32icon:$iconFile"
    $cscArgs += "/resource:$iconFile,ClearVestige.app.ico"
}

$cscArgs += $sourceFiles

Write-Host "Compilation de ClearVestige.exe en cours..." -ForegroundColor Cyan
Get-Process -Name "ClearVestige" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$oldExe = Join-Path $projectDir "ClearVestige.old.exe"
Remove-Item -Path $oldExe -Force -ErrorAction SilentlyContinue
if (Test-Path $outExe) {
    try {
        [IO.File]::OpenWrite($outExe).Close()
    } catch {
        Move-Item -Path $outExe -Destination $oldExe -Force -ErrorAction SilentlyContinue
    }
}
& $csc @cscArgs
Remove-Item -Path $oldExe -Force -ErrorAction SilentlyContinue

if ($LASTEXITCODE -eq 0) {
    Write-Host "Compilation réussie : $outExe" -ForegroundColor Green
} else {
    Write-Error "Échec de la compilation (Code: $LASTEXITCODE)"
    exit $LASTEXITCODE
}
