$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$base = 'http://localhost:1201'
$inbox = 'http://127.0.0.1:8025'
$email = 'smoke-' + [Guid]::NewGuid().ToString('N') + '@example.test'
$password = [Guid]::NewGuid().ToString('N') + '-initial'
$changedPassword = [Guid]::NewGuid().ToString('N') + '-changed'
$resetPassword = [Guid]::NewGuid().ToString('N') + '-reset'
$first = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$second = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$mysql = Join-Path $workspace '.local\mysql\mysql-8.4.11-winx64\bin\mysql.exe'
$admin = Join-Path $workspace '.local\mysql\overlapadmin.cnf'

function Send-AccountRequest($session, $action, $body, $expected = 200) {
    Invoke-WebRequest "$base/api/auth/csrf" -WebSession $session -UseBasicParsing | Out-Null
    $csrf = [Uri]::UnescapeDataString($session.Cookies.GetCookies([Uri]$base)['XSRF-TOKEN'].Value)
    try {
        $response = Invoke-WebRequest "$base/api/auth/$action" -Method Post -WebSession $session -UseBasicParsing -ContentType 'application/json' -Headers @{ 'X-XSRF-TOKEN' = $csrf } -Body ($body | ConvertTo-Json)
        $status = [int]$response.StatusCode
    } catch [Net.WebException] {
        if (-not $_.Exception.Response) { throw }
        $status = [int]$_.Exception.Response.StatusCode
    }
    if ($status -ne $expected) { throw "Unexpected status $status for $action (expected $expected)." }
}

function Assert-Session($session, $expected) {
    try {
        $response = Invoke-WebRequest "$base/api/auth/me" -WebSession $session -UseBasicParsing
        $status = [int]$response.StatusCode
    } catch [Net.WebException] {
        if (-not $_.Exception.Response) { throw }
        $status = [int]$_.Exception.Response.StatusCode
    }
    if ($status -ne $expected) { throw "Unexpected session status $status (expected $expected)." }
}

function Wait-Email($subject) {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $messages = (Invoke-RestMethod "$inbox/api/v1/messages").messages
        $message = $messages | Where-Object { $_.Subject -eq $subject -and @($_.To | ForEach-Object { $_.Address }) -contains $email } | Select-Object -First 1
        if ($message) { return Invoke-RestMethod "$inbox/api/v1/message/$($message.ID)" }
        Start-Sleep -Seconds 1
    }
    throw "The local email '$subject' did not arrive."
}

function Get-LinkToken($message) {
    $match = [regex]::Match($message.Text, 'token=([^&\s]+)')
    if (-not $match.Success) { throw 'No token found in the local email.' }
    return [Uri]::UnescapeDataString($match.Groups[1].Value)
}

try {
    Send-AccountRequest $first 'register' @{ email = $email; password = $password }
    Send-AccountRequest $first 'login' @{ email = $email; password = $password } 401
    $confirmation = Wait-Email 'Confirm your Overlap account'
    Send-AccountRequest $first 'confirm-email' @{ email = $email; token = (Get-LinkToken $confirmation) }
    Send-AccountRequest $first 'login' @{ email = $email; password = $password }
    Send-AccountRequest $second 'login' @{ email = $email; password = $password }
    Assert-Session $first 200
    Send-AccountRequest $first 'change-password' @{ currentPassword = $password; newPassword = $changedPassword }
    Assert-Session $second 401
    Send-AccountRequest $first 'login' @{ email = $email; password = $changedPassword }
    $null = Wait-Email 'Your Overlap password was changed'
    # Move the synthetic account's cooldown back instead of waiting ten minutes.
    & $mysql "--defaults-extra-file=$admin" --batch -e "UPDATE overlap.AspNetUsers SET LastLinkEmailUtc = NULL WHERE Email = '$email';" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to prepare the synthetic recovery test.' }
    Send-AccountRequest $second 'forgot-password' @{ email = $email }
    Send-AccountRequest $second 'forgot-password' @{ email = $email }
    $recovery = Wait-Email 'Reset your Overlap password'
    $token = Get-LinkToken $recovery
    Send-AccountRequest $second 'reset-password' @{ email = $email; token = $token; password = $resetPassword }
    Assert-Session $first 401
    Send-AccountRequest $second 'reset-password' @{ email = $email; token = $token; password = $password } 400
    Send-AccountRequest $second 'login' @{ email = $email; password = $resetPassword }
    $null = Wait-Email 'Your Overlap password was reset'
    $messages = (Invoke-RestMethod "$inbox/api/v1/messages").messages | Where-Object { @($_.To | ForEach-Object { $_.Address }) -contains $email }
    if (@($messages | Where-Object { $_.Subject -eq 'Reset your Overlap password' }).Count -ne 1) {
        throw 'Recovery email cooldown failed.'
    }
    Send-AccountRequest $second 'logout' @{} 204
    Assert-Session $second 401
    Write-Output 'Passed: MySQL signup, email confirmation, login, password change, recovery, token reuse rejection, email cooldown, session invalidation and logout.'
} finally {
    # Remove only this script's unique synthetic account, never user-created accounts.
    & $mysql "--defaults-extra-file=$admin" --batch -e "DELETE FROM overlap.AspNetUsers WHERE Email = '$email';" | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Warning 'The synthetic test account could not be removed.' }
    $password = $changedPassword = $resetPassword = $token = $null
}
