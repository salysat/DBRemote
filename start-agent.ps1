$ErrorActionPreference = 'Stop'
$secretsFile = Join-Path $PSScriptRoot '.dbremote-secrets.ps1'
if (-not $env:DBREMOTE_AGENT_TOKEN -and (Test-Path -LiteralPath $secretsFile)) { . $secretsFile }
if (-not $env:DBREMOTE_AGENT_TOKEN -or $env:DBREMOTE_AGENT_TOKEN.Length -lt 24) { throw 'Run .\setup-local.ps1 first, or configure DBREMOTE_AGENT_TOKEN.' }
if (-not $env:DBREMOTE_SERVER) { $env:DBREMOTE_SERVER = 'ws://localhost:5080' }
if (-not $env:DBREMOTE_DEVICE_ID) { $env:DBREMOTE_DEVICE_ID = 'office-pc' }
dotnet run --project (Join-Path $PSScriptRoot 'src\DBRemote.Agent.Windows\DBRemote.Agent.Windows.csproj')
