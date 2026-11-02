# DB Remote — Remote Studio edition

This edition redesigns the web management workspace. The **existing ASP.NET Core 10 server**, Windows branch agent, WebSocket protocol, authentication, device enrollment, remote screen, clipboard, chat, and transfer handlers are preserved.

## UI
- `/` and `/workspace.html`: unified Remote Studio layout.
- Device column: search, enroll approval, device state, and open screen.
- Center: screen, input controls, fullscreen, status, text entry.
- Right panel: chat, files/clipboard, settings, activity; no page switch during a session.
- Previous HTML pages redirect to the unified workspace.

## Windows setup
1. Back up `.dbremote-secrets.ps1` and `src/DBRemote.Server/data/devices.json` (or your custom `DBREMOTE_DEVICES_PATH`), and the data-protection key directory. Do not disclose these files.
2. Extract the new folder. Restore the secrets/registry only to the appropriate location, without overwriting existing unrelated data.
3. From the DBRemote root run `dotnet build src/DBRemote.Server/DBRemote.Server.csproj`, then `./start-server.ps1` in PowerShell.
4. In a browser open `http://localhost:5080` and hard-refresh (Ctrl+F5).
5. To publish branch agent for a local test, run `./publish-branch-agent.ps1 -ServerUrl "ws://localhost:5080"`.

Branch machines outside this computer require a reachable trusted `wss://` endpoint, proper TLS/reverse proxy setup and security controls. This is a **development/source build**, not an audited production remote-access deployment. MFA and device permission controls still require comprehensive production validation.

## Validation
JavaScript syntax and HTML ID uniqueness were checked in the packaging environment. `.NET` and Windows UI builds could not be executed here; build and verify on your Windows host.
