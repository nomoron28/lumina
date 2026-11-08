# Security policy

## Reporting a vulnerability

Please report security problems **privately**, not in a public issue: use GitHub's [private vulnerability reporting](https://github.com/nomoron28/lumina/security/advisories/new) ("Security" tab, "Report a vulnerability"). Include what you found, how to reproduce it and the Lumina version. You will get a reply within a few days, and a fix or a plan within a reasonable time, with credit if you want it.

## Supported versions

Only the latest release receives fixes.

## What is in scope

Lumina runs locally with your user rights and talks to a few web services you configure. Of most interest: handling of files and network streams (libmpv and FFmpeg parsing), the installer and updater (the update feed is verified with a SHA-256 checksum before anything runs), storage of API keys and network logins, and the parental lock.

## Good to know

- API keys you enter are stored in plain text in `config.json` in your user profile, like most desktop apps; network share passwords are kept in Windows Credential Manager. Do not share your `config.json`.
- Release builds do not contain any API keys.
- The installer is not code-signed yet. Verify downloads against the `SHA256SUMS.txt` published with each release.
- Lumina bundles libmpv and FFmpeg. Vulnerabilities in those are fixed by updating the bundled libmpv; please report them to Lumina as well so a new release is made.
