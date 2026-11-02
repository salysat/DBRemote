$ErrorActionPreference = 'Stop'
$secretsFile = Join-Path $PSScriptRoot '.dbremote-secrets.ps1'
if (-not $env:DBREMOTE_ADMIN_TOKEN -or -not $env:DBREMOTE_AGENT_TOKEN -or -not $env:DBREMOTE_ADMIN_PASSWORD) {
    if (-not (Test-Path -LiteralPath $secretsFile)) {
        & (Join-Path $PSScriptRoot 'setup-local.ps1')
        if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Failed to configure local secrets.' }
    }
    & (Join-Path $PSScriptRoot 'setup-local.ps1')
    . $secretsFile
}
if ($env:DBREMOTE_ADMIN_TOKEN.Length -lt 24 -or $env:DBREMOTE_AGENT_TOKEN.Length -lt 24 -or $env:DBREMOTE_ADMIN_TOKEN -eq $env:DBREMOTE_AGENT_TOKEN) { throw 'Set distinct 24+ character admin and agent tokens.' }
$env:ASPNETCORE_URLS = 'http://localhost:5080'
Write-Host 'Server: http://localhost:5080'
dotnet run --project (Join-Path $PSScriptRoot 'src\DBRemote.Server\DBRemote.Server.csproj')
