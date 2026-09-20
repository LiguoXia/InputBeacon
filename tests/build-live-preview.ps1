$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $projectRoot 'build\live-test'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath $frameworkDirectory)) { $frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$runner = Join-Path $outputDirectory 'InputBeacon-preview.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& (Join-Path $frameworkDirectory 'csc.exe') /nologo /target:winexe /platform:anycpu /warnaserror+ /codepage:65001 /main:InputBeacon.LivePreview "/out:$runner" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:Accessibility.dll "/win32manifest:$(Join-Path $projectRoot 'app.manifest')" @sources (Join-Path $PSScriptRoot 'LivePreview.cs')
if ($LASTEXITCODE -ne 0) { throw 'Live preview compilation failed.' }
Write-Output $runner
