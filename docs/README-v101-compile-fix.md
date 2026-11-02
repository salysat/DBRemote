# DB Remote v0.10.1 — Agent compile fix

The publishing failure at `src/DBRemote.Agent.Windows/Program.cs:112` was caused by literal newlines inside a C# string in the new chat handler. The line now uses valid C# character escapes:

```csharp
Console.WriteLine("CHAT|" + v + "|" + txt.Replace('\r', ' ').Replace('\n', ' '));
```

The Web Agent, Launcher, Server and new workspace UI remain in the package. No stored device credentials or runtime data have been recreated or reset.

## Windows PowerShell (from the folder containing `publish-branch-agent.ps1`)

```powershell
dotnet build .\src\DBRemote.Agent.Windows\DBRemote.Agent.Windows.csproj -c Release
.\publish-branch-agent.ps1 -ServerUrl "ws://localhost:5080"
```

The localhost URL is for testing when both agent and server run on the same PC. For a remote branch, use your actual WSS server endpoint secured by TLS.

## Updating an existing installation

- Stop the previous Agent process (tray menu: Exit), and stop the old Server.
- Extract this ZIP in a **new folder**, avoiding overwriting local secrets and device registry.
- Back up and keep your existing `.dbremote-secrets.ps1`, `devices.json` (wherever `DBREMOTE_DEVICES_PATH` points), Data Protection keys and Agent-local credentials. Do not share secrets.
- Publish the updated Agent. Install the entire published output on the branch rather than copying the source archive to the branch.

## Validation limitation

The source and archive were statically checked, but no .NET SDK or Windows runtime is available in this environment to run `dotnet build` or the remote desktop workflow. The correct verification of the fix is a successful `dotnet build` on your Windows computer.
