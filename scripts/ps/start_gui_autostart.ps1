Param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'

$exePath = Join-Path $RepoRoot 'src\FixedRatioStressTest.Hosting.Gui\bin\Release\net8.0-windows\FixedRatioStressTest.Hosting.Gui.exe'
$projPath = Join-Path $RepoRoot 'src\FixedRatioStressTest.Hosting.Gui'

if (-not (Test-Path $exePath)) {
    Write-Host "GUI executable not found. Building the project in Release..."
    & dotnet build -c Release $projPath | Out-Host
}

if (-not (Test-Path $exePath)) {
    throw "Failed to locate GUI executable at: $exePath"
}

Write-Host "Launching GUI elevated with --start..."
Start-Process -FilePath $exePath -ArgumentList '--start' -Verb RunAs

Start-Sleep -Seconds 2
Write-Host "GUI launch invoked and waited 2 seconds."

exit 0


