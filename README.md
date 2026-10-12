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

> **In development, not usable yet.** This is an early scaffold: the window, settings, console and travel engine are in place, but the plugin cannot shop. Shopping lists, price planning and buying arrive in the next updates.

---

## What it does

Auto Shopper is being built to take your shopping list off your hands: it looks up market prices on Universalis across the worlds you allow, plans where each item is cheapest, travels there with Lifestream, walks to the market board, and buys whole listings within the price caps you set, confirming every purchase before it goes through.

## Features

Planned for the first usable release:

- **Shopping lists**: the items you want and how many, saved between sessions.
- **Price planning**: Universalis prices across the worlds and data centers you allow, turned into a plan of where to buy each item.
- **World and data center travel**: Lifestream carries the character between the worlds on the plan, and the plugin walks the rest of the way to the market board.
- **Guarded purchases**: only whole listings within your price caps, each purchase confirmed, and nothing bought that the plan did not call for.

Already in this build, carried over from the plugin family and waiting for the shopping runs to use them:

- **Travel engine**: world change, data center travel and instance switching through Lifestream, then teleports, aethernet shortcuts, flying and walking to the spot. This one works today: try it with `/ashop goto`.
- **Way home**: back to your home world after a run, then an optional after-run action (stay, return to the inn, log out, or close the game).
- **Pause & resume**: park a run without losing it, auto-pause while you're in a duty, and hold new moves while you type in chat.
- **Humanizer**: short random delays before the character moves off or casts a teleport.
- **GM alert**: ends a run when a GM is near, with optional toast, beeps, or custom commands.
- **History and console**: every run recorded with its world, data center and duration, and every step logged on the Console page, ready to copy into a bug report.

## Install

In-game: `/xlsettings` → **Experimental** → paste into **Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/XeldarAlz/DalamudPlugins/main/repo.json
```

Tick **Enabled**, click **+**, then **Save and Close**. Open `/xlplugins` → **All Plugins**, search for **Auto Shopper**, and install.

The plugin needs two helpers to be installed and loaded: vnavmesh for movement and Lifestream for world and data center travel. Open `/ashop deps` after install to see the list and one-click each missing one. Data center travel also needs to be enabled in Lifestream's own settings.

## Commands

| Command | Action |
|---|---|
| `/ashop` | Toggle the main window |
| `/shopper` | Alias for `/ashop` |
| `/ashop config` | Open the Settings page |
| `/ashop stats` | Open the History page |
| `/ashop deps` | Open the Plugins page |
| `/ashop log` | Open the Console page |
| `/ashop changelog` | Open the Changelog page |
| `/ashop about` | Open the About page |
| `/ashop pause` | Pause or resume the current run |
| `/ashop target` | Log targeted NPC's BaseId (debug helper) |
| `/ashop goto <territory> <x> <y> <z>` | Travel to a point, `/ashop goto stop` cancels (debug helper) |
| `/ashop goto <world> [<aetheryte>] [i<n>]` | Travel to a world in your region, and on it to a named aetheryte and instance, e.g. `/ashop goto Zalera Wachunpelo i2` (debug helper) |
| `/ashop goto home` | Travel back to your home world (debug helper) |

## Languages

The windows are available in English, Deutsch, Français, Español, Português (Brasil), Русский, Türkçe, 日本語, and 中文. The plugin picks a language from your Dalamud and game client settings on first launch; change it any time under Settings, General, Language. Game data such as zone, item, and world names always follows the game client.

Spotted a wrong or awkward translation? Open a [translation issue](https://github.com/XeldarAlz/FFXIV-AutoShopper/issues/new?template=translation_report.yml) and tell me what it should say instead.

## Community

Questions, ideas, or just want to hang out with other players? Come say hi on Discord.

→ [Join our Discord](https://discord.gg/hppkAvdBEE)

## More from me

If you liked this plugin, take a look at my other Dalamud work. You might find something else there for you.

→ [XeldarAlz Dalamud Plugins](https://github.com/XeldarAlz/DalamudPlugins)

## License

AGPL-3.0-or-later. See [LICENSE.md](LICENSE.md). [NOTICE](NOTICE) adds the attribution terms the AGPL allows: a fork, or any project that reuses this code, must credit the original author and must not pass itself off as the original. The license covers the code, not the name or the icon: read the [trademark and naming policy](TRADEMARK.md) before you publish a fork.

How AI is used to build this plugin is written down in [AI usage](AI-USAGE.md).
