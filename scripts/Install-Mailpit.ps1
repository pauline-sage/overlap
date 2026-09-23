$ErrorActionPreference = 'Stop'
$toolDirectory = Join-Path $PSScriptRoot '..\.local\mailpit'
New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
$version = 'v1.31.2'
$expected = '42c20e5c3254125ea7489847811f10d70e39de573fe41d03a61412c87913e995'
$archive = Join-Path $toolDirectory 'mailpit-windows-amd64.zip'
Invoke-WebRequest "https://github.com/axllent/mailpit/releases/download/$version/mailpit-windows-amd64.zip" -OutFile $archive -UseBasicParsing
if (-not $expected -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) {
    throw 'Mailpit checksum verification failed; nothing was extracted.'
}
Expand-Archive -LiteralPath $archive -DestinationPath $toolDirectory -Force
Write-Output "Installed Mailpit $version after SHA-256 verification."
Write-Output 'Run: .\.local\mailpit\mailpit.exe --smtp 127.0.0.1:1025 --listen 127.0.0.1:8025'
