$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$sdk = Join-Path $repo 'libs\C#\dll\SReaderAPI.dll'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) {
    throw 'No se encontro csc.exe (.NET Framework 4.x).'
}
if (-not (Test-Path -LiteralPath $sdk)) {
    throw "No se encontro el SDK: $sdk"
}

$dist = Join-Path $here 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

& $csc /nologo /target:winexe /optimize+ /out:"$dist\PortalIpTool.exe" `
    "$here\Program.cs" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:"$sdk"

if ($LASTEXITCODE -ne 0) {
    throw "csc fallo con codigo $LASTEXITCODE"
}

Copy-Item -LiteralPath $sdk -Destination $dist -Force
Write-Host ('OK: ' + (Join-Path $dist 'PortalIpTool.exe'))
