$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$sdk = Join-Path $repo 'libs\C#\dll\SReaderAPI.dll'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) { throw 'No se encontro csc.exe (.NET Framework 4.x).' }
if (-not (Test-Path -LiteralPath $sdk)) { throw "No se encontro el SDK: $sdk" }

$dist = Join-Path $here 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$sources = @(
    (Join-Path $here 'Program.cs'),
    (Join-Path $here 'Models.cs'),
    (Join-Path $here 'ReaderService.cs'),
    (Join-Path $here 'ProfileStore.cs'),
    (Join-Path $here 'MainForm.cs')
)

$compilerArgs = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    ('/out:' + (Join-Path $dist 'PortalCalibration.exe')),
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Data.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Xml.dll',
    ('/r:' + $sdk)
) + $sources

& $csc $compilerArgs
if ($LASTEXITCODE -ne 0) { throw "csc fallo con codigo $LASTEXITCODE" }

Copy-Item -LiteralPath $sdk -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $here 'LEEME.md') -Destination (Join-Path $dist 'LEEME.md') -Force

$zip = Join-Path $here 'PortalCalibration.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -Force

Write-Host ('OK: ' + (Join-Path $dist 'PortalCalibration.exe'))
Write-Host ('ZIP: ' + $zip)
