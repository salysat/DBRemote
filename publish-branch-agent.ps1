param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [string]$ServerUrl = '')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET 10 SDK on the build PC first: https://dotnet.microsoft.com/download/dotnet/10.0' }
$dist = Join-Path $PSScriptRoot 'dist\DBRemote-BranchAgent'
if (Test-Path $dist) { Remove-Item $dist -Force -Recurse }
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$agent = 'src\DBRemote.Agent.Windows\DBRemote.Agent.Windows.csproj'
$launcher = 'src\DBRemote.Agent.Launcher\DBRemote.Agent.Launcher.csproj'
Write-Host 'Publishing remote control engine...'
dotnet publish $agent -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o $dist
if ($LASTEXITCODE -ne 0) { throw 'Agent publish failed' }
Write-Host 'Publishing Windows tray application...'
dotnet publish $launcher -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o $dist
if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed' }
if ($ServerUrl) {
  $validPublic = $ServerUrl -match '^wss://[a-zA-Z0-9.-]+(:[0-9]+)?$'
  $validLocal = $ServerUrl -match '^ws://(localhost|127\.0\.0\.1)(:[0-9]+)?$'
  if (-not ($validPublic -or $validLocal)) { throw 'Use wss://your-domain for branches, or ws://localhost:5080 for same-PC testing only (no path).' }
  Set-Content -LiteralPath (Join-Path $dist 'server-url.txt') -Value $ServerUrl -Encoding Ascii
}
$guide = @'
DB Remote Agent - Branch deployment

1. Copy the whole folder to a Windows 10/11 branch PC.
2. Run DBRemote.BranchAgent.exe.
3. Configure server once when building with -ServerUrl wss://remote.yourdomain.com. For same-machine tests only, ws://localhost:5080 is accepted. Never use localhost from a different branch PC.
4. Each installed Agent sends a registration request automatically.
5. Open the server administration page and approve the trusted device.
6. After approval the Agent saves its private credential and reconnects automatically.
7. Enable autostart when configuring an Agent; Windows must have an interactive signed-in session.
8. Do not publish the server on the public internet without proper HTTPS, operator login/MFA, rate limiting, protected DB storage, and a deployment security review.
'@
Set-Content -LiteralPath (Join-Path $dist 'READ-FIRST.txt') -Value $guide -Encoding UTF8
$zip = Join-Path $PSScriptRoot ('dist\DBRemote-BranchAgent-' + $Runtime + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Prepared package: $zip" -ForegroundColor Green
Write-Host 'To create an installer, install Inno Setup 6 and run ISCC.exe installer\DBRemote-Agent.iss'
