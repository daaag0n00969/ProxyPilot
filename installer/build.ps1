#requires -Version 5.1
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Publish = Join-Path $Root "artifacts\publish"
$Artifacts = Join-Path $Root "artifacts"
$Iss = Join-Path $PSScriptRoot "ProxyPilot.iss"

New-Item -ItemType Directory -Force -Path $Publish, $Artifacts | Out-Null
Remove-Item -Recurse -Force "$Publish\*" -ErrorAction SilentlyContinue

Write-Host "Publishing self-contained win-x64..."
dotnet publish (Join-Path $Root "ProxyPilot\ProxyPilot.csproj") `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -p:Version=1.0.6 `
  -o $Publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Copy-Item (Join-Path $Root "third_party\WinDivert\WinDivert.dll") $Publish -Force
Copy-Item (Join-Path $Root "third_party\WinDivert\WinDivert64.sys") $Publish -Force
Copy-Item (Join-Path $Root "third_party\WinDivert\LICENSE.txt") (Join-Path $Publish "WinDivert-LICENSE.txt") -Force

$iscc = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  throw "Inno Setup 6 (ISCC.exe) not found. Install from https://jrsoftware.org/isinfo.php"
}

Write-Host "Compiling installer with $iscc ..."
& $iscc $Iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Get-ChildItem $Artifacts -Filter "ProxyPilot-Setup-*.exe" | Select-Object FullName, Length
