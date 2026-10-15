<p align="center">
  <img src="AutoShopper/Images/Icon.png" width="180" alt="Auto Shopper icon" />
</p>

<h1 align="center">Auto Shopper</h1>

<p align="center">
  <a href="https://discord.gg/hppkAvdBEE"><img alt="Discord" src="https://img.shields.io/badge/Discord-join-5865F2?style=flat-square&logo=discord&logoColor=white"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoShopper/releases/latest"><img alt="Release" src="https://img.shields.io/github/v/release/XeldarAlz/FFXIV-AutoShopper?style=flat-square&color=blue"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoShopper/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/XeldarAlz/FFXIV-AutoShopper/total?style=flat-square&color=blue&cacheSeconds=300"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoShopper/actions/workflows/release.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/XeldarAlz/FFXIV-AutoShopper/release.yml?style=flat-square"></a>
  <a href="LICENSE.md"><img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0--or--later-blue?style=flat-square"></a>
</p>

<p align="center">
  <em>Your market board shopping list, bought for you. Built on Dalamud.</em>
</p>

---

## What it does

Write down what you want, and Auto Shopper works out where to buy it. It reads Universalis prices for the worlds you allow, plans the cheapest route, travels there with Lifestream, walks to the market board, and buys within the caps you set, confirming every purchase before it counts.

## Features

- **Shopping lists**: any marketable item, with quality, a set amount or a top up to what you own, and a price cap per unit.
- **Imports**: paste or open plain text, CSV, Artisan crafting lists, Teamcraft lists, and MakePlace / ReMakePlace house layouts (furniture, fixtures, wallpaper and dyes).
- **Crystal helper**: top up shards, crystals and clusters in one step.
- **Three plan modes**: Cheapest, Balanced and Fewest stops, compared side by side on the same prices.
- **Travel**: world and data center changes through Lifestream, then the aethernet and a walk to the board.
- **Live listings**: re-plans from the board itself at every stop, so a stale price never decides a purchase.
- **Guarded purchases**: every purchase is checked, journaled and confirmed against your gil and bags. Anything uncertain stops buying and asks you.
- **Spend cap and gil reserve**: no run starts without a cap, and it never spends the gil you keep.
- **Run controls**: Start, Pause, Resume and Stop, auto-pause in duties, auto-resume after an error.
- **Way home**: back to your home world, then stay, return to the inn, log out or close the game.
- **Humanizer**: short random delays before moving off or teleporting.
- **GM alert**: ends a run when a GM is near, with optional toast, beeps or custom commands.
- **History and exports**: every run recorded, with summaries, plan checklists and lists to copy.
- **Other plugins**: can price items and start runs through the [Buying API](docs/BUYING_API.md) once you allow them.

## Install

In-game: `/xlsettings` → **Experimental** → paste into **Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/XeldarAlz/DalamudPlugins/main/repo.json
```

Tick **Enabled**, click **+**, then **Save and Close**. Open `/xlplugins` → **All Plugins**, search for **Auto Shopper**, and install.

The plugin needs **Lifestream** and **vnavmesh**. Open `/ashop deps` after install to see them and one-click each missing one, then set a run spend cap under Settings, Shopping before your first run. Start with one cheap item and a small cap.

## Commands

| Command | Action |
|---|---|
| `/ashop` | Toggle the main window |
| `/shopper` | Alias for `/ashop` |
| `/ashop config` | Open the Settings page |
| `/ashop stats` | Open the History page |
| `/ashop deps` | Open the Plugins page |
| `/ashop log` | Open the Console page (live plugin log, copy it for bug reports) |
| `/ashop changelog` | Open the Changelog page |
| `/ashop about` | Open the About page |
| `/ashop add <item> [quantity]` | Add an item to the selected list |
| `/ashop start` | Start a run of the selected list |
| `/ashop stop` | Stop the run |
| `/ashop pause` | Pause or resume the current run |
| `/ashop mbstatus` | Check whether the market board works on your game version (debug helper) |
| `/ashop goto <world> [<aetheryte>]` | Travel to a world, e.g. `/ashop goto Zalera Wachunpelo` (debug helper) |

## Languages

The windows are available in English, Deutsch, Français, Español, Português (Brasil), Русский, Türkçe, 日本語, and 中文. The plugin picks a language from your Dalamud and game client settings on first launch; change it any time under Settings, General, Language. Game data such as zone, item, and world names always follows the game client.

Spotted a wrong or awkward translation? Open a [translation issue](https://github.com/XeldarAlz/FFXIV-AutoShopper/issues/new?template=translation_report.yml) and tell me what it should say instead.

## Community

Questions, ideas, or just want to hang out with other players? Come say hi on Discord.

→ [Join our Discord](https://discord.gg/hppkAvdBEE)

## More from me

If you liked this plugin, take a look at my other Dalamud work. You might find something else there for you.

→ [XeldarAlz Dalamud Plugins](https://github.com/XeldarAlz/DalamudPlugins)

## Credits

Market data from Universalis (universalis.app).

## License

AGPL-3.0-or-later. See [LICENSE.md](LICENSE.md). [NOTICE](NOTICE) adds the attribution terms the AGPL allows: a fork, or any project that reuses this code, must credit the original author and must not pass itself off as the original. The license covers the code, not the name or the icon: read the [trademark and naming policy](TRADEMARK.md) before you publish a fork.

How AI is used to build this plugin is written down in [AI usage](AI-USAGE.md).
