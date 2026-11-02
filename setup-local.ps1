# Compatible with Windows PowerShell 5.1 and PowerShell 7+.
$ErrorActionPreference = 'Stop'
$secretsFile = Join-Path $PSScriptRoot '.dbremote-secrets.ps1'
function New-Secret {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
}
if (Test-Path -LiteralPath $secretsFile) {
    Write-Host "Existing local configuration found: $secretsFile"
    Write-Host 'Delete it manually only if you intend to rotate both credentials.'
    if (-not (Select-String -LiteralPath $secretsFile -SimpleMatch 'DBREMOTE_ADMIN_PASSWORD' -Quiet)) {
        $password = New-Secret
        Add-Content -LiteralPath $secretsFile -Value ("`$env:DBREMOTE_ADMIN_PASSWORD = '$password'")
        Write-Host "NEW admin login password: $password" -ForegroundColor Cyan
    }
    exit 0
}

$admin = New-Secret
$agent = New-Secret
$password = New-Secret
if ($admin -eq $agent) { throw 'Unexpected duplicate secret. Try again.' }
$content = @'
# GENERATED LOCAL DEVELOPMENT TOKENS — DO NOT COMMIT OR SHARE.
$env:DBREMOTE_ADMIN_TOKEN = '__ADMIN__'
$env:DBREMOTE_AGENT_TOKEN = '__AGENT__'
$env:DBREMOTE_ADMIN_PASSWORD = '__PASSWORD__'
'@.Replace('__ADMIN__', $admin).Replace('__AGENT__', $agent).Replace('__PASSWORD__', $password)
[System.IO.File]::WriteAllText($secretsFile, $content, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Created: $secretsFile" -ForegroundColor Green
Write-Host 'Keep the file secret. This is for localhost development, not public deployment.' -ForegroundColor Yellow
Write-Host "Admin login password: $password"
