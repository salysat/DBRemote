# DB Remote v0.14 — Branch header corrections

This source release fixes the invalid multiline string literal in the Windows branch-agent header, increases top-header space, adjusts layout minimum size and spacing, and retains the chat, files, settings and agent registration features from earlier versions.

## Run

1. Back up the server `.dbremote-secrets.ps1`, device registry, and local branch-agent settings before updating.
2. In PowerShell from the DBRemote project folder: `./verify-source.ps1`
3. Build the distributable using `./publish-branch-agent.ps1 -ServerUrl 'ws://localhost:5080'` for loopback testing on the same Windows machine only.
4. Exit the old tray agent completely and launch the freshly published app; do not run two copies.

No Windows/.NET build has been executed in this authoring environment. Production connections to branches require TLS, hardened authentication and an appropriate approval policy. This release is for internal development/testing.
