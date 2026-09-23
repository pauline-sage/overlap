$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$mysqlDirectory = Join-Path $workspace '.local\mysql'
$dataDirectory = Join-Path $mysqlDirectory 'data'
$localConfig = Join-Path $workspace 'services\api\appsettings.Local.json'
if ((Test-Path -LiteralPath $dataDirectory) -or (Test-Path -LiteralPath $localConfig)) {
    throw 'Existing database data or local account configuration found. Refusing to overwrite it.'
}
$probe = New-Object Net.Sockets.TcpClient
try {
    $task = $probe.ConnectAsync('127.0.0.1', 3307)
    if ($task.Wait(500) -and $probe.Connected) { throw 'Port 3307 is already in use.' }
} catch [AggregateException] { } finally { $probe.Dispose() }
New-Item -ItemType Directory -Path $mysqlDirectory -Force | Out-Null
# Only this Windows identity and SYSTEM can access the local database and bootstrap credentials.
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($identity in @($sid, (New-Object Security.Principal.SecurityIdentifier('S-1-5-18')))) {
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $mysqlDirectory -AclObject $acl
$archive = Join-Path $mysqlDirectory 'mysql-8.4.11-winx64.zip'
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest 'https://cdn.mysql.com/Downloads/MySQL-8.4/mysql-8.4.11-winx64.zip' -OutFile $archive -UseBasicParsing
}
# Oracle publishes this checksum over HTTPS. The executable publisher is verified below as well.
if ((Get-FileHash -LiteralPath $archive -Algorithm MD5).Hash -ne '2e833921898a9a030ea6bfe81bd811bc') {
    throw 'The MySQL archive does not match the published checksum.'
}
$installDirectory = Join-Path $mysqlDirectory 'mysql-8.4.11-winx64'
if (-not (Test-Path -LiteralPath $installDirectory)) {
    Expand-Archive -LiteralPath $archive -DestinationPath $mysqlDirectory
}
$server = Join-Path $installDirectory 'bin\mysqld.exe'
$signature = Get-AuthenticodeSignature -LiteralPath $server
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Oracle') {
    throw 'The MySQL server executable does not have a valid Oracle signature.'
}
function New-LocalSecret {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
}
$rootPassword = New-LocalSecret
$adminPassword = New-LocalSecret
$appPassword = New-LocalSecret
$migrationPassword = New-LocalSecret
$utf8 = New-Object Text.UTF8Encoding($false)
$iniPath = Join-Path $mysqlDirectory 'my.ini'
$basePath = $installDirectory.Replace('\', '/')
$dataPath = $dataDirectory.Replace('\', '/')
[IO.File]::WriteAllText($iniPath, "[mysqld]`nbasedir=$basePath`ndatadir=$dataPath`nport=3307`nbind-address=127.0.0.1`nmysqlx=0`n", $utf8)
$initialize = Start-Process -FilePath $server -ArgumentList @("--defaults-file=$iniPath", '--initialize', '--console') -WindowStyle Hidden -PassThru -Wait -RedirectStandardError (Join-Path $mysqlDirectory 'initialize.log')
if ($initialize.ExitCode -ne 0) { throw 'MySQL initialization failed. See the private .local/mysql/initialize.log.' }
$bootstrap = Join-Path $mysqlDirectory 'bootstrap.sql'
$sql = @"
ALTER USER 'root'@'localhost' IDENTIFIED BY '$rootPassword';
CREATE USER 'overlapadmin'@'localhost' IDENTIFIED BY '$adminPassword';
GRANT ALL PRIVILEGES ON *.* TO 'overlapadmin'@'localhost' WITH GRANT OPTION;
CREATE DATABASE overlap CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER 'overlap_app'@'localhost' IDENTIFIED BY '$appPassword';
GRANT SELECT, INSERT, UPDATE, DELETE ON overlap.* TO 'overlap_app'@'localhost';
CREATE USER 'overlap_migrate'@'localhost' IDENTIFIED BY '$migrationPassword';
GRANT ALL PRIVILEGES ON overlap.* TO 'overlap_migrate'@'localhost';
"@
[IO.File]::WriteAllText($bootstrap, $sql, $utf8)
[IO.File]::WriteAllText((Join-Path $mysqlDirectory 'admin.cnf'), "[client]`nuser=root`npassword=$rootPassword`nhost=127.0.0.1`nport=3307`n", $utf8)
[IO.File]::WriteAllText((Join-Path $mysqlDirectory 'overlapadmin.cnf'), "[client]`nuser=overlapadmin`npassword=$adminPassword`nhost=127.0.0.1`nport=3307`n", $utf8)
$process = Start-Process -FilePath $server -ArgumentList @("--defaults-file=$iniPath", "--init-file=$bootstrap", '--console') -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $mysqlDirectory 'server.log')
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Seconds 1
        if ($process.HasExited) { throw 'MySQL exited. See .local/mysql/server.log.' }
        $client = Join-Path $installDirectory 'bin\mysql.exe'
        $result = & $client "--defaults-extra-file=$(Join-Path $mysqlDirectory 'admin.cnf')" --batch --skip-column-names -e 'SELECT COUNT(*) FROM mysql.user WHERE User IN (''overlapadmin'', ''overlap_app'', ''overlap_migrate'');' 2>$null
        if ($LASTEXITCODE -eq 0 -and $result -eq '3') { $ready = $true; break }
    }
    if (-not $ready) { throw 'MySQL did not finish initializing the local accounts.' }
    $config = @{
        ConnectionStrings = @{
            Accounts = "Server=127.0.0.1;Port=3307;Database=overlap;User=overlap_app;Password=$appPassword;SslMode=Required"
            AccountsMigration = "Server=127.0.0.1;Port=3307;Database=overlap;User=overlap_migrate;Password=$migrationPassword;SslMode=Required"
        }
        Database = @{ ServerVersion = '8.4.11-mysql' }
        Auth = @{ PublicAppUrl = 'http://127.0.0.1:4200' }
        Email = @{ Host = '127.0.0.1'; Port = 1025; From = 'Overlap <accounts@overlap.test>'; LocalCapture = $true }
    }
    [IO.File]::WriteAllText($localConfig, ($config | ConvertTo-Json -Depth 4), $utf8)
    $fileAcl = New-Object Security.AccessControl.FileSecurity
    $fileAcl.SetAccessRuleProtection($true, $false)
    $fileAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'Allow')))
    Set-Acl -LiteralPath $localConfig -AclObject $fileAcl
    [IO.File]::WriteAllText((Join-Path $mysqlDirectory 'server.pid'), [string]$process.Id, $utf8)
    Write-Output 'MySQL 8.4.11 is running at 127.0.0.1:3307. Root, overlapadmin, application and migration credentials were generated without printing them.'
} finally {
    # Empty this one known credential bootstrap file; do not enumerate or recursively remove paths.
    [IO.File]::WriteAllText($bootstrap, '', $utf8)
    $rootPassword = $adminPassword = $appPassword = $migrationPassword = $sql = $null
}
