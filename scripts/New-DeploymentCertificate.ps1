$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$directory = Join-Path $workspace '.local/deployment'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls $directory /inheritance:r /grant:r ($identity + ':(OI)(CI)F') 'SYSTEM:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not restrict the private deployment directory.' }
Push-Location $workspace
try {
    & dotnet services/api/bin/Release/net9.0/Overlap.Api.dll --generate-deployment-certificate
    if ($LASTEXITCODE -ne 0) { throw 'Certificate creation failed; existing files are never overwritten.' }
} finally { Pop-Location }
