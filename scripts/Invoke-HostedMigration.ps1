param([string]$ConfigPath = "$PSScriptRoot/../.local/deployment/migration.json")
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
if (-not $config.ConnectionStrings__AccountsMigration) { throw 'The private migration configuration is incomplete.' }
# Only the migration process receives credentials. They never appear in command arguments.
$processInfo = New-Object System.Diagnostics.ProcessStartInfo
$processInfo.FileName = 'dotnet'
$processInfo.Arguments = 'services/api/bin/Release/net9.0/Overlap.Api.dll --migrate'
$processInfo.WorkingDirectory = $workspace
$processInfo.UseShellExecute = $false
$processInfo.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Production'
$processInfo.EnvironmentVariables['ConnectionStrings__AccountsMigration'] = $config.ConnectionStrings__AccountsMigration
$processInfo.EnvironmentVariables['Database__ServerVersion'] = $config.Database__ServerVersion
$process = [System.Diagnostics.Process]::Start($processInfo)
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw 'Migration failed. No automatic retry or database reset was performed.' }
