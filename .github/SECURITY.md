# Security Policy

## Supported versions
Only the latest release receives fixes.

## Reporting a vulnerability
Please do **not** open a public issue for security problems. Use GitHub's private vulnerability reporting
(repository **Security** tab > **Report a vulnerability**) and include steps to reproduce.
I will acknowledge reports as soon as I can and credit you in the changelog if you wish.

## Verifying a download
- Every release lists SHA256 checksums in `SHA256SUMS.txt`.
  PowerShell: `Get-FileHash .\Gridify-x.y.z-win-x64-portable.zip -Algorithm SHA256`
- Release files are built by GitHub Actions from the tagged source and carry a build provenance attestation:
  `gh attestation verify <file> --repo pignitionstudio333/Gridify`
- Builds are currently **not code-signed**, so Windows SmartScreen may warn on first run (see README).

## What Gridify touches
Gridify never moves or deletes your files, makes no network connections, needs no administrator rights,
and writes to the registry only if you enable "Start with Windows".
