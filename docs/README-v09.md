# DB Remote v0.9 — Re-enrollment and Settings UI

## Fixes
- Revoking a device invalidates its credential and disconnects the active session.
- Only the same original enrollment claim may re-enroll the same device ID. Different machines receive 409 Conflict.
- The Windows tray launcher polls its enrollment status about every 20 seconds, clears an invalidated local credential, then submits a new approval request automatically.
- A new approval in Workspace is required before reconnection.
- Rebuilt `/settings.html` to show a modern, light, functional preferences page instead of the obsolete manual connection screen.
- Display preferences (FPS/refresh) are saved in browser localStorage without storing credentials.

## Upgrade
Back up `.dbremote-secrets.ps1`, `src/DBRemote.Server/data/devices.json`, and the data protection key folder. Preserve the same server secrets when upgrading.

Restart the new server with `./start-server.ps1`. Publish and install the new Branch Agent with `./publish-branch-agent.ps1 -ServerUrl ws://localhost:5080` for local testing. Existing deployed agents **must be updated** to get automatic re-enrollment after revocation. Do not expose localhost test server publicly.

The first revocation and re-approval should be tested with the upgraded server and tray application on Windows. This build has not undergone full Windows integration testing.
