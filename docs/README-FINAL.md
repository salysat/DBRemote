# DB Remote v0.5.1 — Startup/Storage Repair

## What's fixed

- At server startup, `data/` and `data/devices.json` are automatically created if missing.
- The new file is `{}` because `DeviceRegistry` deserializes a dictionary keyed by device ID. **Do not initialize it with `[]`.**
- Existing device registrations are preserved; invalid JSON causes a clear error rather than silent replacement.
- Optional `DBREMOTE_DEVICES_PATH` can point to a persistent absolute JSON file, enabling upgrades without moving the registry. Keep that file and the agent-token secret together during server upgrades.
- Windows PowerShell 5.1-compatible server diagnostic command `./check-server.ps1` checks `/health` and the device API.

## Local run

From the extracted `DBRemote` folder:

```powershell
.\start-server.ps1
```

Then in another PowerShell window:

```powershell
.\check-server.ps1
```

Open `http://localhost:5080/dashboard.html`. On first run the folder `src/DBRemote.Server/data/` is created automatically. For the branch Agent, use:

```powershell
.\publish-branch-agent.ps1 -ServerUrl "ws://localhost:5080"
```

For a remote branch use a genuine public `wss://` endpoint with TLS, and do not use `localhost`.

## Preserving devices during updates

- Stop the old server before launching the new one.
- Keep the same `data/devices.json` and `DBREMOTE_AGENT_TOKEN`. Changing the token invalidates the issued credentials for approved Agents; plan to re-enroll them if rotating it.
- The admin token was visible in a screenshot: rotate it before any deployment. Keep secrets out of chat and source control.
- To use a stable location, configure `DBREMOTE_DEVICES_PATH` to an absolute file path accessible by the server account. The directory will be created automatically.
- Back up `devices.json` and the secrets separately before replacing old installations.

## Security / validation limitations

Local development MVP only. Not production-ready for unattended remote control over the Internet: needs session-based authentication with MFA, per-operator authorization, rate limiting on enrollment, security assessment, and Windows integration testing. This environment cannot build or run Windows-targeted .NET code; run `dotnet build` on Windows before distribution.
