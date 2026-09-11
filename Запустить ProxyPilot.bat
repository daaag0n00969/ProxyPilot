@echo off
setlocal
set "ROOT=%~dp0"
set "EXE=%ROOT%ProxyPilot\bin\Release\net8.0-windows\ProxyPilot.exe"
if not exist "%EXE%" (
  echo Building ProxyPilot...
  dotnet build "%ROOT%ProxyPilot\ProxyPilot.csproj" -c Release
  if errorlevel 1 exit /b 1
)
powershell -NoProfile -Command "Start-Process -FilePath '%EXE%' -Verb RunAs"
