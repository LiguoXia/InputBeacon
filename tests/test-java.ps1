param([Parameter(Mandatory=$true)][string]$RuntimeBin)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $projectRoot 'test-output\java'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
& (Join-Path $RuntimeBin 'javac.exe') -encoding UTF-8 -d $outputDirectory (Join-Path $PSScriptRoot 'JavaCaretFixture.java')
if ($LASTEXITCODE -ne 0) { throw 'Java fixture compilation failed.' }
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$runner = Join-Path $outputDirectory 'JavaIntegrationTests.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& (Join-Path $frameworkDirectory 'csc.exe') /nologo /target:winexe /platform:anycpu /warnaserror+ /codepage:65001 /main:InputBeacon.JavaIntegrationTests "/out:$runner" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:Accessibility.dll "/reference:$(Join-Path $frameworkDirectory 'WPF\UIAutomationClient.dll')" "/reference:$(Join-Path $frameworkDirectory 'WPF\UIAutomationTypes.dll')" "/reference:$(Join-Path $frameworkDirectory 'WPF\WindowsBase.dll')" "/win32manifest:$(Join-Path $projectRoot 'app.manifest')" @sources (Join-Path $PSScriptRoot 'JavaIntegrationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Java test runner compilation failed.' }
$testProcess = Start-Process -FilePath $runner -ArgumentList @(('"' + $RuntimeBin + '"'), ('"' + $outputDirectory + '"')) -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $outputDirectory 'java-results.txt')
if ($testProcess.ExitCode -ne 0) { throw 'Java integration tests failed.' }
