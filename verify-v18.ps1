$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$projects = @(
    '.\src\DBRemote.Server\DBRemote.Server.csproj',
    '.\src\DBRemote.Agent.Windows\DBRemote.Agent.Windows.csproj',
    '.\src\DBRemote.Agent.Launcher\DBRemote.Agent.Launcher.csproj'
)
foreach ($project in $projects) {
    Write-Host "Building: $project" -ForegroundColor Cyan
    dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
Write-Host 'Build verification completed. Manual UI/connection tests remain required.' -ForegroundColor Green
