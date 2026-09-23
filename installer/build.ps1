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
  -p:PublishSingleFile=false `
  -p:IncludeNativeLibrariesForSelfExtract=false `
  -p:Version=1.0.4 `
  -o $Publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$LauncherOut = Join-Path $Root "artifacts\launcher"
New-Item -ItemType Directory -Force -Path $LauncherOut | Out-Null
Write-Host "Compiling native root launcher..."
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "MSVC not found (need Visual Studio C++ tools to build the launcher)." }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
$cfile = Join-Path $Root "installer\launcher.c"
$ico = Join-Path $Root "assets\app.ico"
$rc = Join-Path $LauncherOut "launcher.rc"
Set-Content -Path $rc -Value "1 ICON `"$($ico.Replace('\','\\'))`"" -Encoding ASCII
$cmd = "call `"$vcvars`" >nul && cd /d `"$LauncherOut`" && rc /nologo /fo launcher.res launcher.rc && cl /nologo /O2 /W3 /DUNICODE /D_UNICODE `"$cfile`" launcher.res /Fe:ProxyPilot.exe /link /SUBSYSTEM:WINDOWS user32.lib /MANIFESTUAC:""level='requireAdministrator' uiAccess='false'"""
cmd.exe /c $cmd
if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $LauncherOut "ProxyPilot.exe"))) {
    throw "native launcher compile failed"
}
Remove-Item -Force (Join-Path $LauncherOut "launcher.obj") -ErrorAction SilentlyContinue

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
