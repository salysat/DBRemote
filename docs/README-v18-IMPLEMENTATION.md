# DB Remote v0.18 — Enterprise Foundation / verified scope

## Implemented in source

- Device grouping and branch assignment API with persisted `studio.json` and management UI `/manage.html`.
- Device catalog filters and responsive UI retained from v0.17.
- Brand name and accent configuration API and UI for settings metadata.
- Persistent event history (agent/viewer connect/disconnect), admin-only history UI and CSV export.
- Read-only admin-protected device preview endpoint. Agent produces a low-resolution JPEG every 15 seconds **only if** owner opts in using its settings checkbox. Default OFF; no preview if offline or stale; no unauthenticated preview access.
- Existing chat, clipboard, file transfer, enrollment, revocation and per-device secret protocols retained.
- Original files `data/devices.json` and `.dbremote-secrets.ps1` are not supplied or overwritten by this archive.

## Important gaps — NOT production-ready

- **Multi-user accounts, granular permissions, and MFA**: not implemented. Only one local administrator cookie session exists. Do not expose this server to the internet.
- **Independent chat without a viewer session**: not implemented. Chat currently requires a connected viewer; messages not queued across disconnects.
- **Resumable file transfer, cryptographic integrity acknowledgement, and reliable backpressure**: not implemented. Existing transfer is an MVP and file reception must be checked on destination.
- **WebRTC / TURN**: not implemented; file and desktop traffic transit the WebSocket server.
- **Native Windows system service, pre-login access, signed MSI, auto-updates**: not implemented. Agent runs in a logged-in user session.
- **Complete security audit and real Windows/browser integration test**: not performed in this environment.

## Upgrade safely

1. Back up your existing `.dbremote-secrets.ps1`, `src/DBRemote.Server/data/devices.json`, `src/DBRemote.Server/data/studio.json` (if present), and `src/DBRemote.Server/data/dataprotection/` directory.
2. Extract into a **new** directory; copy the backed-up data and secrets to equivalent paths, carefully. Do not duplicate two servers on port 5080.
3. Run `./verify-source.ps1` in PowerShell, then `./start-server.ps1`.
4. Visit `http://localhost:5080/` and sign in, then open `http://localhost:5080/manage.html`.
5. Publish the new agent using `./publish-branch-agent.ps1 -ServerUrl "ws://localhost:5080"` for same-computer testing only.
6. In Agent Settings enable previews **only after the branch consents**. Keep unchecked for sensitive desktops.
7. Never deploy publicly until HTTPS, MFA, per-user RBAC, safe network/proxy configuration, and tests are implemented and reviewed.

## Validation

`node --check` on app.js, studio-ui.js, manage.js, and ZIP CRC can be validated here. `dotnet build` requires .NET 10 SDK and Windows for functional agent testing.
