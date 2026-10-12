# Contributing

Thanks for taking an interest. This is a small solo project, but PRs are welcome and I'll review them.

## Quick start

```bash
git clone --recurse-submodules https://github.com/XeldarAlz/FFXIV-AutoShopper.git
cd FFXIV-AutoShopper
dotnet build AutoShopper.sln -c Release
```

You need the .NET 10 SDK. The plugin requires Dalamud at runtime; CI pulls a Dalamud dev build automatically and that's enough to compile. See `.github/workflows/release.yml` if you want to reproduce CI locally.

Load the built plugin via `/xlsettings` -> **Experimental** -> **Dev Plugin Locations**, pointing at `AutoShopper/bin/Release/AutoShopper.dll`.

## Project layout

- `AutoShopper/Core/`: the run controller, the travel engine, IPC adapters, and the console log.
- `AutoShopper/Windows/`: ImGui main window, settings, dependencies.
- `AutoShopper/`: plugin entry points, config, command wiring.
- `ECommons/`: submodule, shared Dalamud helpers. Don't patch this directly; upstream it.

Keep logic small and direct. This plugin has one job.

## Before you open a PR

1. `dotnet build -c Release` cleanly.
2. Test in-game on at least one shopping run, or with `/ashop goto` when you changed travel. Worlds, data centers, and market board spots differ between regions and cities, so a fix that works on one data center may not work on another.
3. Keep the diff focused. One concern per PR.
4. Match the existing style. No heavy abstractions "for later."
5. If your change affects what a user sees or types (commands, window layout, settings), update the README.
6. If you used AI beyond autocomplete, say which level in the PR description. It is one line, and [AI-USAGE.md](AI-USAGE.md) explains the level names and why I ask.

## Good first issues

Check the tracker for anything labeled `good first issue`. Travel quirks (a world it can't get to, an aetheryte it misses, a market board it can't walk up to) are usually the lowest-friction way to help: name where it misbehaved, attach a log of what the plugin did vs. what should have happened, and a fix is usually a small change.

## Security

Please don't file public issues for security problems; see [SECURITY.md](SECURITY.md).

## Code of conduct

See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Be decent.

## License

By contributing, you agree your contributions are licensed under AGPL-3.0-or-later with the additional terms in [NOTICE](NOTICE), the same as the project.
