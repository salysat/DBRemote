# DB Remote — Windows Remote Desktop Reference MVP

A working **source-code MVP** for remote viewing and basic mouse/keyboard input, explicit file transfer in both directions, text clipboard, and pre-authorized/unattended agent connections. Built with .NET 10, ASP.NET Core WebSockets, Windows Forms and browser JavaScript, with no NuGet packages required for the server or agent. Source code is supplied; **not compiled or run here** because the build environment does not have the .NET SDK or Windows.

## IMPORTANT: architecture and scope

This implementation uses **TLS WebSockets via the server as a relay**, NOT WebRTC, TURN, SignalR or end-to-end encrypted peer-to-peer media. The server can see relayed file/screen content and must be trusted. It is for **self-hosted development / authorized devices**, not a hardened public AnyDesk replacement.

The agent captures the Windows **interactive user session**. To enable unattended access to an already signed-in Windows desktop, run it at sign-in using Windows Task Scheduler. It **does not support Windows login screen, locked secure desktop, UAC secure desktop, service/session-0 capture, or full privilege elevation**. Explicit owner enrollment is required; controller authentication must remain mandatory.

## Prerequisites

- Windows 10 or 11 on controlled machine, .NET 10 SDK.
- Any modern browser on phone/another computer.
- Localhost testing: direct HTTP localhost is allowed. Across networks: terminate HTTPS/WSS and configure trusted reverse proxy, firewall and ACLs. Restrict access; do not expose raw development server to the public internet.

## Run on Windows (PowerShell, two terminals)

From the project folder, run these commands in **Windows PowerShell 5.1** or PowerShell 7. Tokens are generated once with `RandomNumberGenerator.Create()` and stored in `.dbremote-secrets.ps1` (excluded from Git). Keep this file private; it is a **local development credential file**. Do not use it for public hosting.

**PowerShell terminal 1:**

```powershell
.\setup-local.ps1
.\start-server.ps1
```

**PowerShell terminal 2:**

```powershell
.\start-agent.ps1
```

To get the admin token again for the browser, in a local trusted PowerShell session use:

```powershell
. .\.dbremote-secrets.ps1
$env:DBREMOTE_ADMIN_TOKEN
```

For testing across multiple PCs, do **not** copy the local secrets file wholesale. Configure device-specific identities and TLS first; this MVP's shared agent token is unsuitable for public/fleet deployment.

Open `http://localhost:5080` on your PC, enter `office-pc` and the ADMIN token from terminal 1, click **اتصال**. For a phone, deploy behind HTTPS and point the agent at the `wss://` endpoint. When the screen appears, click/touch to interact. Focus the screen to send physical keyboard keys; use the text entry bar for Unicode text.

### Transfer files
- Browser → Windows: choose files, click **إرسال الملفات للكمبيوتر**. Windows receives them in `%USERPROFILE%\Downloads\DBRemote Incoming\` with a generated prefix to avoid overwrites.
- Windows → Browser: on the Agent console type `send C:\path\yourfile.pdf` while a browser is connected. Browsers trigger a download.
- Clipboard (text only): **استلام النص المنسوخ** to pull from Windows; edit textbox and **إرسال نص الحافظة** to push to Windows. Automatic continuous clipboard sync is intentionally **not implemented** (avoid accidentally disclosing passwords).

### Unattended access
Owner configures agent and server tokens and starts the agent at Windows sign-in; after that controller connections need no additional on-device approval, but must authenticate. Do not set up hidden persistence, bypass elevation prompts, or provide access to anyone without device owner's permission.

## Security checklist before production

1. Implement individual user accounts, strong MFA, per-device secrets, enrollment and revocation; do not share a single global token for fleet deployment.
2. Implement server-side per-session ACLs, one-controller enforcement, explicit per-permission checks and session expiry. The MVP uses one global controller secret and client-side UI switch; it **does not provide enterprise authorization**.
3. Host on secured HTTPS/WSS with firewall, reverse proxy and trusted forwarding rules; configure rate limiting, message quotas and audit log.
4. Use WebRTC with DTLS/SRTP/RTCDataChannel or implement application-layer E2EE if the relay must not see content.
5. Implement integrity hashing, stream-based browser receiving, reconnect/resume, malware screening/limits, and server backpressure. Browser currently holds received files in RAM; use small files only.
6. Add Windows UIAccess/secure desktop functionality only if legitimately required, documented and reviewed. The current agent cannot operate secure UAC prompts.
7. For public use, review consent UI, privacy policy, code signing, updater, monitoring, and security testing.

## Known MVP constraints

- Streaming is JPEG screenshots over WebSocket, ~5 fps default, not H.264/low-latency media.
- Screen capture supports virtual desktop. Precision of clicks on browsers with letterboxing/zoom may vary.
- Agent supports only one control session; a second viewer may see the screen but only the active viewer controls it. The server does not yet enforce the single-controller policy (agent does).
- Files limited to 1 GiB on Windows receiving; browser downloads buffered in memory.
- A local user sign-in session and executable console are required; no installer, tray app or Windows Service included.
- Tokens are held in process environment and memory; browser admin token only used for initial WebSocket authentication.

## Build

```powershell
dotnet build .\src\DBRemote.Server\DBRemote.Server.csproj -c Release
dotnet build .\src\DBRemote.Agent.Windows\DBRemote.Agent.Windows.csproj -c Release
```

All real deployment requires validation on Windows. Do not treat this MVP as a production-grade AnyDesk clone.

## Windows Branch Agent (tray app and installer)

The `DBRemote.Agent.Launcher` project adds a Windows GUI application with tray icon, an Arabic first-run configuration wizard, per-user encrypted token storage (Windows DPAPI), reconnect/stop controls and opt-in auto-start after Windows sign-in. It wraps the existing `DBRemote.Agent.Windows.exe` capture/input engine. On a Windows build PC with .NET 10 SDK, run:

```powershell
.\publish-branch-agent.ps1
```

This creates `dist\DBRemote-BranchAgent-win-x64.zip`. You can copy the extracted contents onto branch PCs (self-contained; no .NET runtime installation required). For an optional conventional Setup.exe, install Inno Setup 6 and compile `installer\DBRemote-Agent.iss` from its IDE or with `ISCC.exe`. Neither binary artifact can be built here (the current execution environment lacks the .NET SDK and Windows).

**Do not use the shared agent token for all branches**: the original MVP server accepts a single global agent key, lacks enterprise-grade per-device authorization, and is not suitable for public deployment. Implement per-device enroll/revoke, identities, MFA, sessions authorization, and properly configured HTTPS/WSS before remote branches connect over the internet. The tray app runs after interactive Windows sign-in; it is not a Windows service and does not allow locked-screen or UAC secure-desktop control. Because it is a user agent, installing an EXE alone does not grant Windows permissions or run before sign-in.
