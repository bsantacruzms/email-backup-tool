#requires -Version 5.1
<#
.SYNOPSIS
    Publishes single-file, self-contained portable executables for the Email Backup Tool.
.DESCRIPTION
    Produces two portable .exe files in the dist/ folder (no .NET runtime required on the
    target machine):
      * EmailBackupTool-<version>-portable.exe  (the WPF desktop app)
      * ebk-<version>-portable.exe              (the command-line tool)
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($version)) { $version = "0.0.0" }

Write-Host "Building Email Backup Tool v$version ($Configuration / $Runtime)" -ForegroundColor Cyan

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$publishArgs = @(
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:IncludeAllContentForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=none",
    "--nologo"
)

Write-Host "Publishing desktop app..." -ForegroundColor Yellow
dotnet publish src/EmailBackup.App/EmailBackup.App.csproj @publishArgs -o (Join-Path $dist "_gui")
if ($LASTEXITCODE -ne 0) { throw "GUI publish failed." }
Copy-Item (Join-Path $dist "_gui/EmailBackupTool.exe") (Join-Path $dist "EmailBackupTool-$version-portable.exe") -Force

Write-Host "Publishing command-line tool..." -ForegroundColor Yellow
dotnet publish src/EmailBackup.Cli/EmailBackup.Cli.csproj @publishArgs -o (Join-Path $dist "_cli")
if ($LASTEXITCODE -ne 0) { throw "CLI publish failed." }
Copy-Item (Join-Path $dist "_cli/ebk.exe") (Join-Path $dist "ebk-$version-portable.exe") -Force

Remove-Item (Join-Path $dist "_gui"), (Join-Path $dist "_cli") -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Portable executables:" -ForegroundColor Green
Get-ChildItem $dist -Filter *.exe | ForEach-Object {
    "  {0,-42} {1,6:N1} MB" -f $_.Name, ($_.Length / 1MB)
}
Write-Host "Output folder: $dist" -ForegroundColor Green
