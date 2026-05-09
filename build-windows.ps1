<#
.SYNOPSIS
Build a distributable Windows Yinka.exe release.

.DESCRIPTION
Publishes the WPF Windows app as a self-contained win-x64 application. The
output folder contains Yinka.exe, the .NET runtime files needed to run without
installing .NET, and Data\en_kjv.json for offline scripture lookup.

Run from Windows PowerShell or PowerShell 7:

  .\build-windows.ps1

Optional:

  .\build-windows.ps1 -Runtime win-arm64
  .\build-windows.ps1 -OutputDir C:\Builds\Yinka
  .\build-windows.ps1 -SkipZip
#>

[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$OutputDir = (Join-Path $PSScriptRoot "dist\windows"),

    [switch]$SkipZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Command([string]$Name, [string]$InstallHint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found. $InstallHint"
    }
}

if ($PSVersionTable.PSEdition -eq "Core" -and -not $IsWindows) {
    throw "This script must run on Windows because Yinka is a WPF application."
}
if ($PSVersionTable.PSEdition -eq "Desktop") {
    # Windows PowerShell 5.1 does not define $IsWindows, but only runs on Windows.
}

Assert-Command "dotnet" "Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"

$sdkList = dotnet --list-sdks
if (-not ($sdkList -match "^8\.")) {
    throw "The .NET 8 SDK is required. Installed SDKs:`n$sdkList"
}

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot "Yinka\Yinka.csproj"
$solution = Join-Path $repoRoot "Yinka.sln"
$publishDir = Join-Path $OutputDir "Yinka-$Runtime"
$zipPath = Join-Path $OutputDir "Yinka-$Runtime.zip"

Write-Step "Publishing Yinka for $Runtime ($Configuration)"
Write-Host "Repo:        $repoRoot"
Write-Host "Project:     $project"
Write-Host "Output:      $publishDir"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Step "Restoring NuGet packages"
dotnet restore $solution

Write-Step "Publishing self-contained application"
dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    --output $publishDir

$exePath = Join-Path $publishDir "Yinka.exe"
$dataPath = Join-Path $publishDir "Data\en_kjv.json"

if (-not (Test-Path $exePath)) {
    throw "Publish failed: expected executable not found at $exePath"
}
if (-not (Test-Path $dataPath)) {
    throw "Publish failed: bundled KJV not found at $dataPath"
}

Write-Step "Verifying output"
$exe = Get-Item $exePath
$data = Get-Item $dataPath
Write-Host ("Yinka.exe:       {0:N1} MB" -f ($exe.Length / 1MB))
Write-Host ("Data/en_kjv.json:{0,7:N1} MB" -f ($data.Length / 1MB))

if (-not $SkipZip) {
    Write-Step "Creating zip package"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    $zip = Get-Item $zipPath
    Write-Host ("Zip:            {0} ({1:N1} MB)" -f $zip.FullName, ($zip.Length / 1MB))
}

Write-Step "Done"
Write-Host "Run locally:"
Write-Host "  $exePath"
Write-Host ""
Write-Host "Distribute:"
Write-Host "  Send the whole folder: $publishDir"
if (-not $SkipZip) {
    Write-Host "  Or send the zip:       $zipPath"
}
