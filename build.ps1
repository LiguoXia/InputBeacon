param([switch]$Test, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$buildDirectory = Join-Path $projectRoot 'build'
$releaseDirectory = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'release' }
New-Item -ItemType Directory -Force -Path $buildDirectory, $releaseDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
$frameworkDirectory = Split-Path -Parent $compiler

# The small app icon is drawn from the same native shapes and lettering as the UI.
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 64, 64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([System.Drawing.Color]::FromArgb(20, 29, 43))
$font = New-Object System.Drawing.Font 'Segoe UI', 27, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(101, 225, 196))
$graphics.DrawString('Aa', $font, $brush, 7, 12)
$graphics.FillEllipse($brush, 47, 48, 8, 8)
$memory = New-Object System.IO.MemoryStream
$bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $memory.ToArray()
$iconPath = Join-Path $buildDirectory 'app.ico'
$stream = [System.IO.File]::Create($iconPath)
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
$writer.Write([byte]64); $writer.Write([byte]64); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22)
$writer.Write($png)
$writer.Dispose(); $memory.Dispose(); $brush.Dispose(); $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose()

$output = Join-Path $releaseDirectory '键盘状态.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4', '/warnaserror+', '/utf8output', '/codepage:65001',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:Accessibility.dll',
    "/win32manifest:$(Join-Path $projectRoot 'app.manifest')", "/win32icon:$iconPath", "/out:$output") + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $projectRoot '使用说明.txt') -Destination $releaseDirectory -Force
Write-Output "Built: $output"
if ($Test) {
    $testFolder = Join-Path $projectRoot 'test-output'
    $process = Start-Process -FilePath $output -ArgumentList @('--self-test', ('"' + $testFolder + '"')) -PassThru -Wait -WindowStyle Hidden
    Get-Content -LiteralPath (Join-Path $testFolder 'results.txt')
    if ($process.ExitCode -ne 0) { throw "Tests failed: $($process.ExitCode)" }
}
Get-FileHash -LiteralPath $output -Algorithm SHA256
