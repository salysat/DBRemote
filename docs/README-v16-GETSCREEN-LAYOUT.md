# DB Remote v0.16 — Device Cards Console

A DB Solutions-branded administrative interface closely following the provided layout reference, without reusing the third-party trademark or artwork.

## Layout
- Blue left navigation with Permanent Access, Quick Support, Branding, Integrations, History, Settings and Help.
- Search by device name/ID and filter by status.
- Grid and list views, device cards, status, session preview for the active viewer, connect, approve or revoke.
- Integrated remote viewer, chat, file transfer, clipboard and activity log.
- Browser login via existing server-side administrator session.

## Implemented vs not implemented
- Functional: existing device enrollment/approval, remote viewer, mouse/keyboard, file upload, clipboard and chat while connected. Added search, filter, layout switch, device-specific open and UI guidance.
- Screenshot thumbnails: currently shown only when a viewer is streaming that device; does not capture all registered machines in the background.
- Sidebar Branding/Integrations are explanatory panels, not third-party integrations or editable branding settings.
- No remote agent app changes in this release; existing v0.14+ agent can be retained if matching protocol.

## Run
1. Back up `.dbremote-secrets.ps1`, and the server's `data/devices.json` and `data/dataprotection` from the existing installation. Preserve the same `DBREMOTE_AGENT_TOKEN`.
2. Stop existing server.
3. Unzip into a separate location, migrate preserved secrets and registry only after backing up.
4. Start from `DBRemote` root with `./start-server.ps1`.
5. Browse `http://localhost:5080/` and hard refresh (Ctrl+F5).

For publishing across branches, configure HTTPS/TLS, access control and MFA before exposing the application to the internet.

Verification: JavaScript syntax and HTML element IDs checked; actual .NET build and Windows UI/remote control cannot be tested in this environment.
