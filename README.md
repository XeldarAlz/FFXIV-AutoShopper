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

Write down what you want from the market board, and Auto Shopper works out where to buy it. It reads Universalis prices for the worlds you allow, plans the cheapest mix of whole listings, travels there with Lifestream, walks to the market board, and buys within the caps you set, confirming every purchase before it counts.

## Features

- **Shopping lists**: search any item sold on the market board by name or id, then set its quality (Any, HQ or NQ), whether to buy a set amount or top up to what you want to own, the most it may pay per unit with the 5% market fee included, and how many extra units it may take when only bigger stacks are listed. Owned counts cover your bags, crystals, armory chest and equipped gear. Lists are kept between sessions and can be renamed, duplicated and archived.
- **Imports**: paste plain text such as `3x Darksteel Ore`, a CSV, an Auto Shopper JSON export, an Artisan crafting list or a Teamcraft list. A crafting list is bought as finished items, as its direct ingredients, or expanded down to the raw materials. Nothing changes until you choose to add the rows, replace the list or start a new one.
- **Crystal helper**: pick elements and kinds (shards, crystals, clusters) and they become Top up lines, so a run buys only what your crystal bag is missing.
- **Exports**: lists as text, CSV or JSON, plans as a text or Markdown checklist, and run summaries as text or JSON. None of them hold character names, content ids, retainers or listing ids.
- **Universalis prices**: Get prices on the Plan page reads the current listings of the list's items on the worlds you allow, chosen from a preset (current world, home data center, current data center, or whole region) and a world picker. Prices older than the age you set are flagged.
- **Three plan modes**: Cheapest, Balanced (weighs the total against the stop and data center penalties you set) and Fewest stops (the fewest worlds, within a premium over the cheapest). The Plan page compares all three on the same prices and shows the route in visit order, the estimated total with the fee, your budget against the spend cap, price freshness, and every unit it could not place with the reason why. Plans buy whole listings only.
- **Travel**: world changes and data center transfers through Lifestream. A transfer logs the character out and back in, and the run carries on by itself afterwards. On each world it takes the aethernet when that is shorter and walks to a market board, in your preferred city when none is in reach.
- **Live listings at every stop**: the run searches each item on the board itself and plans what is left again from those listings, so a stale Universalis price never decides a purchase.
- **Guarded purchases**: every purchase is checked before it is sent, written to a journal, and confirmed against the server's answer and the change in your gil and bags. See [Safety](#safety).
- **Uncertain purchases**: a purchase without a clear answer stops all buying and puts a card on the Shop page with your gil and owned count before and after, so you can say whether it went through.
- **Run controls**: Start, Pause, Resume and Stop on the Shop page or as commands, auto-pause while you're in a duty, hold new moves while you type in chat, and auto-resume after an unexpected error (up to 3 times in 5 minutes).
- **Way home**: when a run ends on its own, back to your home world, crossing data centers when needed, then an optional after-run action (stay, return to the inn, log out, or close the game). A Stop or a fault runs neither.
- **Humanizer**: short random delays before the character moves off or casts a teleport.
- **GM alert**: ends a run when a GM is near, with an optional toast, chat line, beeps, custom commands, or closing the game.
- **History and console**: every run recorded with its outcome, list, world, data center, units, gil and time, with a summary to copy for each run. Every step is logged on the Console page, ready to copy into a bug report.
- **Other plugins**: another plugin can price items and start a run of its own list through the [Buying API](docs/BUYING_API.md), once you allow it on the Shop page. Its runs keep your worlds, gil reserve and purchase checks, never take an after-run action, and are marked with the plugin's name in History. Settings, General lists every plugin you answered, with Revoke, which also stops a run that plugin has under way.

## How a run works

1. Pick a list on the Shop page and press **Start** (or type `/ashop start`). The run gets fresh prices, makes the plan, and checks that a run spend cap is set.
2. It travels to the first stop through Lifestream, changing world or data center as the plan needs.
3. It takes the aethernet if that is shorter, walks to a market board and opens it.
4. For each item on that stop it reads the live listings and plans what is left again from what the board shows.
5. Each chosen listing passes the purchase checks, is written to the journal, and is bought, one at a time.
6. It waits for the answer and checks your gil and bags, then moves on to the next item and the next stop.
7. When the list is covered, the cap is reached or nothing affordable is left, it travels home (when Return home is on), runs the after-run action, and records the run in History.

## Safety

- **Caps.** No run starts without a run spend cap. Each line can have its own most per unit (fee included) and its allowance for extra units, a run always leaves the gil you choose to keep in your purse, and Most stops limits how many worlds a plan visits.
- **Every purchase is checked** before it is sent: the right character, on an allowed world it is standing on, the same item, quality and stack size as the listing chosen, a line that still needs units, the line's cap, the run's budget, the gil you keep, room in your bags or crystal pouch, unique items you already own, and no other purchase waiting for an answer.
- **Refusals.** Listings from your own retainers, items on a mannequin, and set listings are never bought.
- **Journal first.** The purchase is written to the purchase journal on disk before it is sent; when that write fails, nothing is sent.
- **Confirmed, not assumed.** A purchase counts only when the server's answer agrees with the game's purchase events and your purse and bags changed by what they should. Anything less stops buying and asks you on the Shop page.
- **One purchase at a time.** Never more than one purchase waits for an answer, and Stop waits for that answer before it ends the run.
- **Interrupted runs.** A run cut short by a logout or a reload never resumes on its own: the Shop page offers to resume it with fresh prices or to discard it. A journal that cannot be trusted keeps buying off until you set it aside.
- **What it never does**: sell or list items, touch your retainers, travel outside your home region, buy more than your list asks for beyond the extra units you allow, or spend past the run spend cap or below the gil you keep. The only network traffic it makes itself is reading prices from Universalis.

## Install

In-game: `/xlsettings` → **Experimental** → paste into **Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/XeldarAlz/DalamudPlugins/main/repo.json
```

Tick **Enabled**, click **+**, then **Save and Close**. Open `/xlplugins` → **All Plugins**, search for **Auto Shopper**, and install.

## Requirements

- **Lifestream** for world and data center travel. Runs that cross data centers also need data center travel turned on in Lifestream's own settings (`/li`).
- **vnavmesh** for walking to the market board.
- A **run spend cap** under Settings, Shopping. Plans work without one; runs do not start.

Open `/ashop deps` after install to see both helpers and one-click each missing one. `/ashop mbstatus` says whether the market board can be read and bought on your game version; when buying is off, runs do not start.

## Commands

| Command | Action |
|---|---|
| `/ashop` | Toggle the main window |
| `/shopper` | Alias for `/ashop` |
| `/ashop config` | Open the Settings page |
| `/ashop stats` | Open the History page (`/ashop history` works too) |
| `/ashop deps` | Open the Plugins page (`/ashop dependencies` works too) |
| `/ashop log` | Open the Console page |
| `/ashop changelog` | Open the Changelog page |
| `/ashop about` | Open the About page |
| `/ashop add <item name or id> [quantity]` | Add a Buy line at Any quality to the selected list, making a list first when there is none |
| `/ashop start` | Start a run of the selected list |
| `/ashop stop` | Stop the run; a purchase already sent is answered first |
| `/ashop pause` | Pause or resume the current run (`/ashop resume` works too) |
| `/ashop look <item name or id>` | Log every listing of one item at the market board in reach; it never buys (debug helper) |
| `/ashop mbstatus` | Report whether the market board checks of this game version pass (debug helper) |
| `/ashop board [city]` | Go to a market board and open it, in the given city (limsa, gridania, uldah, ishgard, kugane, crystarium, sharlayan, tuliyollal) or the preferred one (debug helper) |
| `/ashop board stop` | Cancel the trip to a market board (debug helper) |
| `/ashop target` | Log targeted NPC's BaseId (debug helper) |
| `/ashop goto <territory> <x> <y> <z>` | Travel to a point (debug helper) |
| `/ashop goto <world> [<aetheryte>] [i<n>]` | Travel to a world in your region, and on it to a named aetheryte and instance, e.g. `/ashop goto Zalera Wachunpelo i2` (debug helper) |
| `/ashop goto home` | Travel back to your home world (debug helper) |
| `/ashop goto stop` | Cancel a goto (debug helper) |

## Known limits

- **Universalis is an estimate.** Its prices come from player uploads and can lag behind the board, or miss a world nobody uploaded lately, and a world sends only its 100 cheapest listings. The Plan page totals are estimates for that reason, and a run re-plans from the live board at every stop.
- **Retainers and saddlebags are not counted** as owned, so a Top up line can buy what a retainer already holds.
- **Japanese routes are off by default.** World Visit between Japanese data centers changed on 13 October 2026, and that route is untested, so Japanese worlds outside the data center you stand on stay out of prices, plans and runs unless you allow them under Settings, Shopping.
- **Start with a small list.** A purchase is confirmed only when your purse drops by the listing price plus its fee as the board shows it. Your first purchases prove that on your client, so begin with one cheap item and a small spend cap, and keep the Console open.
- **Travel stays in your home region**, and a data center transfer takes as long as Lifestream needs, logging the character out and back in.

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
