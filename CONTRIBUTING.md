# Contributing to Lumina

Thank you for helping. Bug reports, ideas, documentation fixes and code are all welcome.

## Reporting a bug

Open an [issue](https://github.com/nomoron28/lumina/issues/new/choose) and include:

- your Windows version and Lumina version (**Settings, Advanced, About Lumina**),
- what you did, what you expected and what happened,
- for playback problems: the file's format (**Playback panel, Media information**) and, if you can, the statistics from pressing `i` during playback,
- the newest file from the log folder (**Settings, Advanced, Log files**), with anything private removed.

Please do not post API keys or other secrets.

## Suggesting a feature

Open an issue describing the problem you want solved before the solution. Small, focused features that fit a media library and player are the best fit.

## Sending code

1. Fork, then create a branch from `main`.
2. Set up as described in [docs/development.md](docs/development.md) (.NET 10 SDK, and a libmpv for running the player).
3. Make your change, following the conventions below.
4. Run `dotnet test` and, when you can, `powershell -File scripts\gate.ps1 -SkipUi`. The build must have **zero warnings**.
5. Open a pull request that says what changed and why, with a screenshot for anything visual.

### Conventions

- **Settings go through the catalog.** Add a row to `src/Lumina/Settings/Catalog.cs`; never read ad-hoc config keys.
- **Database changes are appended migrations** (with the backup that `Db` already makes), and config keys are only ever added.
- **Every interactive control has an accessible name** (`AutomationProperties.Name`).
- **Fix bugs test-first.** A regression test that fails, then the fix, and the test stays.
- **Test what you add.** Logic gets unit tests; anything involving libmpv gets a headless test (the controller tests are the pattern); clickable things belong in the real-input tour.
- **Comments say why**, not what, and match the density of the surrounding code.
- Do not commit API keys, `secrets.local.json`, third-party fonts, or other people's artwork.

### The real-input UI tour

`scripts/e2e.ps1` drives the real mouse and keyboard. Run it only when you are away from the computer. It is not part of the automated checks on GitHub.

## Code of conduct

Be kind and assume good faith. See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Licence

By contributing you agree that your contribution is licensed under the project's licence, **GPL-3.0-or-later** (see [LICENSE](LICENSE)).
