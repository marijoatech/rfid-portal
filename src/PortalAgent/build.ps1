$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$sdk = 'C:\wamp64\www\rfid-portal\libs\Demo3.71311\SReaderAPI.dll'
$newton = Join-Path $here 'lib\Newtonsoft.Json.dll'
$dist = Join-Path $here 'dist'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $sdk)) { throw "No se encontro SReaderAPI.dll: $sdk" }
if (-not (Test-Path -LiteralPath $newton)) { throw "No se encontro Newtonsoft.Json.dll: $newton" }

New-Item -ItemType Directory -Force -Path $dist | Out-Null

$sources = @(
    (Join-Path $here 'Program.cs'),
    (Join-Path $here 'AgentConfig.cs'),
    (Join-Path $here 'Logger.cs'),
    (Join-Path $here 'ConsoleFilter.cs'),
    (Join-Path $here 'Reading.cs'),
    (Join-Path $here 'ApiClient.cs'),
    (Join-Path $here 'ReaderWorker.cs')
)

$args = @(
    '/nologo',
    '/target:exe',
    '/optimize+',
    ('/out:' + (Join-Path $dist 'PortalAgent.exe')),
    '/r:System.dll',
    '/r:System.Core.dll',
    ('/r:' + $sdk),
    ('/r:' + $newton)
) + $sources

& $csc $args
if ($LASTEXITCODE -ne 0) { throw "csc fallo con codigo $LASTEXITCODE" }

Copy-Item -LiteralPath $sdk -Destination $dist -Force
Copy-Item -LiteralPath $newton -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $here 'LEEME.md') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $here 'Aprender-fondo.cmd') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $here 'Limpiar-fondo.cmd') -Destination $dist -Force

$config = Join-Path $dist 'agent.json'
if (-not (Test-Path -LiteralPath $config)) {
    Copy-Item -LiteralPath (Join-Path $here 'agent.example.json') -Destination $config -Force
}

$zip = Join-Path $here 'PortalAgent.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -Force

Write-Host ('OK: ' + (Join-Path $dist 'PortalAgent.exe'))
Write-Host ('ZIP: ' + $zip)
