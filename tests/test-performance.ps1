param([string]$SourceDirectory, [string]$Label = 'optimized')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $projectRoot 'src' }
$outputDirectory = Join-Path $projectRoot "test-output\performance-$Label"
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$runner = Join-Path $outputDirectory 'PerformanceTests.exe'
$sources = @(Get-ChildItem -LiteralPath $SourceDirectory -Filter '*.cs' | ForEach-Object FullName)
$defines = if (Select-String -LiteralPath (Join-Path $SourceDirectory 'CaretTracker.cs') -Pattern 'bool TryAutomation\(' -Quiet) { '/define:LEGACY' } else { '/define:NATIVE' }
# WPF references allow the same runner to compile the pre-optimization baseline.
& (Join-Path $frameworkDirectory 'csc.exe') /nologo $defines /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001 /main:InputBeacon.PerformanceTests "/out:$runner" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:Accessibility.dll "/reference:$(Join-Path $frameworkDirectory 'WPF\UIAutomationClient.dll')" "/reference:$(Join-Path $frameworkDirectory 'WPF\UIAutomationTypes.dll')" "/reference:$(Join-Path $frameworkDirectory 'WPF\WindowsBase.dll')" "/win32manifest:$(Join-Path $projectRoot 'app.manifest')" @sources (Join-Path $PSScriptRoot 'PerformanceTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Performance runner compilation failed.' }
$report = Join-Path $outputDirectory 'results.txt'
$process = Start-Process -FilePath $runner -ArgumentList ('"' + $report + '"') -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath $report
if ($process.ExitCode -ne 0) { throw 'Performance tests failed.' }
