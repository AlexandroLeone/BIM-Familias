param([string]$Dotnet = (Join-Path $env:LOCALAPPDATA 'Codex\dotnet-sdk-10\dotnet.exe'))
$ErrorActionPreference = 'Stop'
& $Dotnet build (Join-Path $PSScriptRoot 'Addin\Addin.csproj') -c Release -o (Join-Path $PSScriptRoot 'dist\revit-final')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o complemento' }
& $Dotnet build (Join-Path $PSScriptRoot 'Desktop\Desktop.csproj') -c Release -o (Join-Path $PSScriptRoot 'dist\catalogo-final')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o catalogo' }
& $Dotnet build (Join-Path $PSScriptRoot 'Activator\Activator.csproj') -c Release -o (Join-Path $PSScriptRoot 'dist\ativador')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o ativador' }
& $Dotnet build (Join-Path $PSScriptRoot 'Installer\Installer.csproj') -c Release -o (Join-Path $PSScriptRoot 'dist\instalador-exe')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador EXE' }
& (Join-Path $PSScriptRoot 'build-single-installer.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar o instalador EXE único' }
& $Dotnet run --project (Join-Path $PSScriptRoot 'Tests\Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes' }

