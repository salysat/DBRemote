# DB Remote v0.4 — Operations Redesign

## Changes
- Rebuilt web management dashboard in Arabic RTL with device overview, status counters, pending enrollment actions, remote screen workspace, clipboard / file controls, and appearance switch.
- Updated Branch Agent WinForms configuration with a dark/gold style and clearer enrollment messaging.
- Guarded one WebSocket authentication close-handshake error on server.
- Preserved existing API endpoints and browser DOM IDs used by app.js.

## Run
1. Extract ZIP to a NEW directory (do not mix files with v0.2/v0.3).
2. `./start-server.ps1` on Windows PowerShell; follow local key generation instructions.
3. `./publish-branch-agent.ps1 -ServerUrl "ws://localhost:5080"` for same-PC testing.
4. Launch published `DBRemote.BranchAgent.exe` and approve request from dashboard after entering admin key.
5. For branch deployment, use a real externally accessible HTTPS/WSS reverse proxy and unique authenticated devices; no public release before MFA and penetration testing.

## Validation limitations
- Syntax checked via node --check for app.js; ZIP integrity verified.
- .NET SDK/Windows not available in authoring environment, so C# build/functional testing still required on Windows.
- This is a functional redesign of existing MVP; not production-grade AnyDesk replacement.

## Skill
User-provided vercel-labs/skills `find-skills` is a skill discovery process, not a front-end styling template. Design is custom rather than claimed to be derived from a UI skill.
