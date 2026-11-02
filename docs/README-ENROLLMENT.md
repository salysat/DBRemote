# DB Remote – automatic branch enrollment (prototype)

This package adds: pending device registration, administrator approval and revocation, per-device credentials, Windows Agent polling, click-to-open device directory.

1. Run `start-server.ps1` on the admin PC.
2. Publish with `./publish-branch-agent.ps1 -ServerUrl wss://YOUR-SERVER-FQDN` after configuring HTTPS reverse proxy.
3. On Windows branch PC, run `DBRemote.BranchAgent.exe`. A unique Device ID is generated and a registration request is sent automatically. The branch PC does not need a shared registration key.
4. In `http://localhost:5080` on the management PC, enter the existing admin token once in the administration field, click Refresh Devices, and approve only the branch machine you verified out-of-band.
5. A few seconds later the Agent stores its per-device secret under the Windows user DPAPI profile, and the device shows online in the management dashboard. Click Open Screen.

**Security limitations**: the existing server is still a development MVP. Admin token is still an environment-managed secret, not user identity + MFA. Registration is deliberately administrator-approved, but open registration endpoints need internet-side rate limiting and anti-abuse protection. Approved device credentials are derived from a server-side secret, not stored in plaintext in JSON. Protect the server secret and implement expiring, one-time credential delivery before production. Revocation rejects future connections but does not forcibly terminate established WebSockets. Branch Agent requires an interactive Windows login. Use only in a trusted internal test network until hardened. No remote file transfer should be performed with unknown devices.
