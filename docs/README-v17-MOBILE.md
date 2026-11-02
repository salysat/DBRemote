# DB Remote v0.17 - Responsive Mobile Console

This update builds on v0.16. It changes responsive layout only, retaining the existing server, agent, pairing protocol and UI element IDs.

- Sticky compact blue navigation, horizontally scrollable on phones.
- Touch-friendly 44px buttons, mobile search and filters, stacked device cards.
- Session viewer adapts to phone widths; chat, files and clipboard stack underneath.
- Responsive forms and safe-area padding for phones with notches and home indicators.
- Reduced-motion respect and visible keyboard focus indicators.

Run: `./start-server.ps1` then open `http://localhost:5080` on the host. Mobile access requires a suitably secured reachable HTTPS endpoint; localhost on the phone will not refer to the PC.

QA completed: JavaScript syntax, static HTML structure, ZIP integrity. Device/Windows runtime and actual phone browser checks could not be performed in this environment. The live preview for each idle/offline device and multi-user RBAC remain future work; this update does not claim to add them.

Do not publish remote-control server publicly before security hardening including MFA, TLS and role permissions.
