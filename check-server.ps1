$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$program = Join-Path $root 'src\DBRemote.Server\Program.cs'
if (-not (Test-Path -LiteralPath $program)) { throw 'Run from the DB Remote project folder.' }
Write-Host 'Checking DB Remote local server...' -ForegroundColor Cyan
try {
  $health = Invoke-RestMethod -Uri 'http://localhost:5080/health' -TimeoutSec 5
  Write-Host "Health: $($health.status); connected Agents: $($health.agents)" -ForegroundColor Green
} catch {
  Write-Warning 'Server is not responding at http://localhost:5080/health'
  Write-Warning $_.Exception.Message
  exit 1
}
try {
  $response = Invoke-WebRequest -Uri 'http://localhost:5080/api/devices' -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
  Write-Warning "Unexpected status $($response.StatusCode) without authentication."
} catch {
  $status = $null
  if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
  if ($status -eq 401) { Write-Host 'Device API available (401 without admin token is expected).' -ForegroundColor Green }
  elseif ($status -eq 404) { Write-Warning 'Device API returned 404: likely an old server process on port 5080.'; exit 2 }
  else { Write-Warning "Device API returned $status. $($_.Exception.Message)"; exit 3 }
}
