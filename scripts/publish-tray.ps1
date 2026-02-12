param(
  [ValidateSet("Release", "Debug")]
  [string]$Configuration = "Release",

  # Pick a runtime so publish produces a runnable .exe (self-contained).
  [ValidateSet("win-x64", "win-arm64")]
  [string]$Runtime = "win-x64",

  # Output directory (relative to repo root).
  [string]$OutDir = "dist\\Qcapi.Tray\\$Runtime",

  [switch]$CleanOut
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "windows\\Qcapi.Tray\\Qcapi.Tray.csproj"
$out = Join-Path $repoRoot $OutDir

if ($CleanOut -and (Test-Path $out)) {
  try {
    Remove-Item -LiteralPath $out -Recurse -Force
  } catch {
    throw "Failed to clean output directory '$out'. Is Qcapi.Tray.exe still running?"
  }
}

dotnet publish $project `
  -c $Configuration `
  -r $Runtime `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $out

if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed (exit code $LASTEXITCODE). If you published to the same folder, make sure Qcapi.Tray.exe is not running."
}

Write-Host ("Published to: " + $out)
