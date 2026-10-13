# Auto Shopper Buying API, schema 1

Lets another Dalamud plugin price items and run a bounded Auto Shopper shopping run from a list it supplies, then read the result. Every purchase still goes through Auto Shopper's journal, guard and reconciliation. The caller never sends a purchase.

## Transport

- Dalamud IPC call gates, registered by Auto Shopper with `GetIpcProvider` and unregistered on dispose.
- Every payload is a JSON string (System.Text.Json, camelCase property names, enums as strings). No custom type crosses plugin load contexts and no `Task` is returned across IPC: long work is started and then polled.
- Every request and response carries `"schema": 1`. Auto Shopper reads a request's top-level `schema` before anything else: when it is missing or anything but the number 1, the request is refused with `UnsupportedSchema` whatever the rest of it looks like, so a later schema that changes the shape of a request still gets that answer. Text that is not one well-formed JSON object is refused with `InvalidRequest` (field `json`).
- Calls may arrive on any thread. Auto Shopper reads and checks the request on the calling thread, then moves the work onto the framework thread before touching its controller, ledger or configuration, and answers from managed snapshots. A call made off the framework thread waits for the next framework tick.
- Requests are read leniently: property names match whatever their case, unknown properties are ignored, and an enum may be given by name or by number (a number outside the enum is refused like any other bad value).
- Quote ids and run ids are GUID strings such as `"4e1a0b3c-2d5f-4a6b-8c7d-9e0f1a2b3c4d"`. Any GUID format is accepted back.

## Gates

| Gate name | Dalamud shape | Consent needed | Purpose |
| --- | --- | --- | --- |
| `AutoShopper.Api.ApiVersion` | `Func<int>` | no | Returns `1` |
| `AutoShopper.Api.DryRun` | `Func<bool>` | no | Auto Shopper's own dry-run switch |
| `AutoShopper.Api.QuoteStart` | `Func<string, string>` | no | Starts a price quote, returns a quote id or a refusal |
| `AutoShopper.Api.QuoteResult` | `Func<string, string>` | no | Polls a quote by id |
| `AutoShopper.Api.StartRun` | `Func<string, string>` | yes | Starts a run from the caller's list |
| `AutoShopper.Api.RunStatus` | `Func<string, string>` | no | Polls a run by id, during and after the run |
| `AutoShopper.Api.StopRun` | `Func<string, bool>` | no | Stops a run started through this API; never a run started from Auto Shopper's UI |

## Callers

- `caller` is the calling plugin's internal name: 1 to 64 characters, each a letter, a digit, `.`, `_` or `-`. Consent is stored under it and it is matched exactly (case matters). Every request needs it.
- `callerDisplayName` is optional. Control characters and repeated spaces are dropped and it is cut to 64 characters; when it ends up empty, `caller` is shown instead. It is what the player sees on the consent card, in Settings and in History.

## Consent

- `StartRun` is refused with `ConsentRequired` until the player allows the caller once inside Auto Shopper.
- On that refusal Auto Shopper shows a card on its Shop page: "<caller display name> wants to buy through Auto Shopper." with Allow and Deny. Below it the card names the caller's internal name (`caller`) and the `maxSpend` of the request that asked, and says that a plugin's runs keep to the cap they send in place of the player's run spend cap. A caller that asks again before an answer updates the card with its newest display name and cap. At most 8 callers wait for an answer at a time: a ninth pushes the oldest question out, and that caller is asked again on its next `StartRun`.
- The answer is stored per caller internal name (`caller` field) and covers every later run of that caller, whatever cap it asks for. Settings, General has an "Other plugins" group listing every answered caller: an allowed caller has a Revoke button, a denied caller an Allow button.
- Revoke forgets the answer: the caller's next `StartRun` is refused with `ConsentRequired` and the card asks again.
- A denied caller is refused with `ConsentDenied` until the player allows it from Settings.
- Revoke and Deny also end what the caller has under way: its active API run is stopped exactly as `StopRun` stops it, and its quotes still waiting for their turn at the price fetch turn `Failed` with `PricesUnavailable`. A quote already fetching finishes. A run the player resumed from the Shop page is the player's and is left alone.
- A run checks consent once more right before it is authorized, after it has its prices and plan: a caller that is no longer allowed by then ends the run with `refusal` `ConsentRequired` or `ConsentDenied`, and nothing is bought.
- Quotes need no consent.

## QuoteStart request

```json
{
  "schema": 1,
  "caller": "AutoGcSupply",
  "callerDisplayName": "Auto GC Supply",
  "items": [
    { "itemId": 5057, "quantity": 3 }
  ]
}
```

- 1 to 100 items, `itemId` > 0, `quantity` 1 to 9999. Duplicate item ids are refused (`InvalidRequest`).
- Prices come from Auto Shopper's price service for Auto Shopper's configured world scope, fetched detached (it never replaces the Plan page's prices).
- Auto Shopper allows one Universalis fetch every 30 seconds across the Plan page, runs and quotes, and a quote always lets the player and runs go first: it fetches only once those 30 seconds have been over for 10 more seconds (time for the player to press Get prices on the Plan page), while no Plan page fetch is under way and no run is fetching its prices. Until then it stays `Pending`. Quotes that wait fetch one at a time, in the order they were asked for.
- A caller can have at most 2 quotes waiting for their turn at a time. Another `QuoteStart` from it is refused with `Busy` until one of them has started fetching.

## QuoteStart response

```json
{ "schema": 1, "accepted": true, "quoteId": "b6c0...", "refusal": null, "message": null }
```

or

```json
{ "schema": 1, "accepted": false, "quoteId": null, "refusal": "InvalidRequest", "message": "localized text" }
```

- Refusal codes: `UnsupportedSchema`, `InvalidRequest`, `LoggedOut` (no character is logged in, so there is no world scope to price), `Busy` (the caller already has 2 quotes waiting for their turn).
- `message` reads "Cannot price these items: <reason>." in Auto Shopper's language. For `InvalidRequest` the reason names the field at fault, for example `items[2].quantity`, `items[1].itemId` (zero or a duplicate), `items`, `caller` or `json` (the text is not a request).

## QuoteResult response

```json
{
  "schema": 1,
  "quoteId": "b6c0...",
  "state": "Ready",
  "refusal": null,
  "fetchedAtUtc": "2026-10-13T18:22:05Z",
  "items": [
    {
      "itemId": 5057,
      "quantity": 3,
      "normal": { "available": true, "units": 3, "total": 1890, "unitPrice": 630, "highestUnitPrice": 630, "oldestListingHours": 4.5, "worlds": 1 },
      "high":   { "available": false, "units": 0, "total": 0, "unitPrice": 0, "highestUnitPrice": 0, "oldestListingHours": 0, "worlds": 0 }
    }
  ]
}
```

- `state`: `Pending`, `Ready`, `Failed`, `Unknown` (no such quote id, or expired).
- `items` is filled and `fetchedAtUtc` set only when `state` is `Ready`; otherwise `items` is `[]` and `fetchedAtUtc` is `null`. `items` keeps the order of the request.
- `Failed` means the price fetch did not come back complete (any Universalis request of it failed, or it could not start); `refusal` is then `PricesUnavailable`. Ask for a new quote.
- Per quality, `total` is the cheapest combination of whole listings that covers `quantity` units within the scope, with the 5% market fee included. `units` is how many units that combination buys (it can exceed `quantity` when only bigger stacks exist, or when a bigger stack is cheaper). `unitPrice` is `total / units` rounded up. `highestUnitPrice` is the dearest listing of that combination per unit: each listing's stack price with the fee, divided by its units and rounded up, and the highest of those. A `StartRun` line whose `maxUnitPrice` is at least `highestUnitPrice` lets every listing of the combination through at its quoted price, where `unitPrice`, an average, can turn the dearer ones away. `available` is false when no combination covers the quantity, and every other number of that quality is then 0.
- `oldestListingHours` is the age of the oldest listing in the chosen combination at `fetchedAtUtc`, in hours with one decimal; a listing Universalis gives no age for is left out. `worlds` is how many worlds the chosen combination spans.
- `normal` covers NQ listings only, `high` HQ listings only. Items that have no HQ version report `high.available = false`.
- Quotes are kept 10 minutes or until 16 newer quotes exist.

## StartRun request

```json
{
  "schema": 1,
  "caller": "AutoGcSupply",
  "callerDisplayName": "Auto GC Supply",
  "callerRunId": "0d7f...",
  "label": "GC supply 2026-10-13",
  "maxSpend": 150000,
  "returnHome": true,
  "lines": [
    { "itemId": 5057, "quality": "NormalQuality", "quantity": 3, "maxUnitPrice": 700, "excessAllowance": 3 }
  ]
}
```

- `quality`: `Any`, `HighQuality`, `NormalQuality` (Auto Shopper's `ItemQuality` names). Omitted means `Any`.
- Lines are bought as fixed quantities (`QuantityMode.Buy`). `maxUnitPrice` is per unit with the 5% fee included; 0 means no per-line cap. `excessAllowance` is the extra units the line may take when only bigger stacks are listed.
- `maxSpend` is this run's spend cap in gil, fees included. It takes the place of the player's own run spend cap (Settings, Shopping) for this run and is not limited by it: an allowed caller can ask for any cap up to 999,999,999 on each run. The player sees it on the consent card (for the request that asked) and in the chat line Auto Shopper prints when a real run is authorized, and the run never spends past it.
- 1 to 100 lines, `maxSpend` 1 to 999,999,999, every number inside Auto Shopper's `ShoppingLimits`: `quantity` 1 to 999,999, `maxUnitPrice` 0 to 999,999,999, `excessAllowance` 0 to 999,999. Each item may appear once per quality: two lines with the same `itemId` and `quality` are refused (`InvalidRequest`); the same item as `NormalQuality` and `HighQuality` is fine.
- `callerRunId` is optional, at most 64 characters, and comes back unchanged in `RunStatus`.
- `label` is optional; it is cleaned like `callerDisplayName`, cut to 64 characters and names the run in chat and History. Without one the run is named after `callerDisplayName`.
- The list lives in memory for that run only; it is never added to the player's lists.
- The caller cannot turn dry run off, widen the world scope, lower the gil reserve, choose an after-run action (always stay logged in) or raise world revisits. Those come from Auto Shopper's own settings, and so do the plan mode and the most stops setting. `returnHome` decides the way home for this run only, a resume of it included.

## StartRun response

```json
{ "schema": 1, "accepted": true, "runId": "4e1a...", "refusal": null, "message": null }
```

Refusal codes: `UnsupportedSchema`, `InvalidRequest`, `ConsentRequired`, `ConsentDenied`, `Busy` (any run, UI or API, is active or has not finished ending, or any other Auto Shopper task such as a travel helper is running), and Auto Shopper's readiness names that apply: `SetupNeeded`, `LoggedOut`, `JournalDamaged`, `NeedsReconciliation`, `InterruptedRun`, `ReadingUnavailable`, `BuyingUnavailable`. `message` is the localized reason Auto Shopper would print: "Cannot start: <reason>."

- Checks run in this order: schema and request shape, consent, busy, readiness.
- `InvalidRequest` names the field at fault in its message, as for quotes (`lines[0].quality`, `maxSpend`, `callerRunId` and so on). Once Auto Shopper's item data has loaded, a line naming an item the market board does not sell (`lines[n].itemId`), or `HighQuality` for an item that has no HQ (`lines[n].quality`), is refused as `InvalidRequest` too. When the item data has not loaded yet, the run is accepted and checks those lines in its preflight.

## RunStatus response

```json
{
  "schema": 1,
  "runId": "4e1a...",
  "known": true,
  "callerRunId": "0d7f...",
  "dryRun": true,
  "phase": "Shopping",
  "state": "Searching",
  "status": "Searching the listings of Darksteel Ore",
  "revision": 42,
  "paused": false,
  "pauseReason": null,
  "terminal": false,
  "outcome": null,
  "needsPlayer": false,
  "confirmedSpend": 1890,
  "reservedSpend": 0,
  "lines": [
    { "itemId": 5057, "quality": "NormalQuality", "requested": 3, "acquired": 3, "missing": 0 }
  ],
  "issues": [ { "reason": "NoStock", "itemId": 5058 } ],
  "returnHome": "NotNeeded",
  "refusal": null
}
```

- `known` is false for an unknown or expired run id; other fields are then defaults, and `runId` repeats the id that was asked about.
- `phase`: Auto Shopper's `RunPhase` names (`Idle`, `Preparing`, `Travelling`, `Shopping`, `Paused`, `Finishing`, `Stopping`). `state`: `RunState` names. `outcome`: `RunOutcome` names (`Completed`, `CompletedPartial`, `Stopped`, `Failed`, `NeedsReconciliation`) once `terminal` is true.
- `status` is the localized line Auto Shopper shows for the run.
- `revision` counts changes to the run's own record (its state, route, travel, purchases, spend, issues and way home) and never goes down. The result kept once the run is terminal always carries a revision higher than any status served before it, so a terminal result never repeats a revision the caller already saw. `status`, `phase`, `paused` and `pauseReason` can change without a new revision (a pause, a stop that waits for a purchase in flight, a new status line), and so can a dry run's would-buy counts until the run's next state change: a caller that caches by revision should also compare `terminal`, `phase`, `paused` and `needsPlayer`.
- `paused` is true while the run is paused; `pauseReason` is then `Manual` (the player, or a trip held up by a party, a trade or an open window) or `InContent` (Auto Shopper's auto-pause in duties), and `null` otherwise. An `InContent` pause resumes on its own once the player leaves the duty; a `Manual` pause resumes only inside Auto Shopper.
- `terminal` turns true once Auto Shopper has let go of the run, after its way home. While the way home runs, `phase` is `Finishing` and `terminal` is still false. A terminal result reports `phase` `Idle`, `state` the run's last `RunState`, and `status` the outcome or the refusal reason.
- `acquired` counts confirmed units in a real run and would-buy units in a dry run; `confirmedSpend` likewise counts confirmed gil in a real run and would-spend gil in a dry run. `reservedSpend` is gil held for a purchase in flight or one without a clear answer.
- `lines` follows the request's lines in order. `issues` lists one entry per reason and item; `reason` is one of Auto Shopper's `RunIssueReason` names: `PriceCapExceeded`, `NoStock`, `InventoryFull`, `InsufficientGil`, `TravelFailed`, `DependencyBusy`, `UncertainPurchase`, `UnsupportedGameVersion`, `BudgetReached`, `SearchFailed`, `RetriesExhausted`, `ListingRejected`, `UniqueOwned`, `BoardUnreachable`, `JournalWriteFailed`, `CharacterChanged`, `NotMarketable`, `CrystalLimit`. `itemId` is 0 when an issue is not about one item.
- `needsPlayer` is true when the run waits for the player inside Auto Shopper: an uncertain purchase, a reconciliation, or an interrupted run. An uncertain purchase ends the run as `NeedsReconciliation` with `needsPlayer` true, and every later start is refused with `NeedsReconciliation` until the player answers. A real run interrupted by a logout, a character change or an unload after it was authorized ends here as `Stopped` with `needsPlayer` true: Auto Shopper's Shop page offers to resume it (that resume belongs to the player and is not an API run: `RunStatus` and `StopRun` do not reach it), and later starts are refused with `InterruptedRun` until it is resumed or discarded. The journal records the caller with the run's authorization, so a resumed run keeps the request's `returnHome`, stays logged in after it like any API run, and its History row still names the caller.
- `refusal` is set when the run was accepted but refused itself during preflight; `terminal` is then true and `outcome` is `Stopped`. Codes: the readiness names above (checked again), `ConsentRequired` and `ConsentDenied` (consent checked again before the authorization), `ListLoading` (Auto Shopper's item data did not load within 30 seconds), `InvalidRequest` (a line names an item the market board does not sell, or HQ for an item without HQ), `PricesUnavailable`, `PlanFailed`, `NothingToBuy`, `AuthorizationFailed`, `JournalWriteFailed`, and `CharacterChanged` (only after Auto Shopper restarted a faulted run on another character).
- The run plans on Auto Shopper's current prices or on a recent quote's when they cover every item of the run on the same worlds and are younger than Auto Shopper's stale-prices setting; otherwise it fetches its own, apart from the Plan page. Like every run, it reads the live listings at each stop before it buys.
- `returnHome`: a `ReturnHomeResult` name (`AlreadyHome`, `Underway`, `Arrived`, `Failed`, `Skipped`), or `NotNeeded` while no way home was asked for or decided yet (Auto Shopper's `NotAsked`).
- Auto Shopper's duty auto-pause and its GM alert stop apply to API runs as to any run. A GM alert stop ends the run as `Stopped`.
- Results are kept for the last 8 API runs, in memory for the session.

## StopRun

- `StopRun(runId)` returns true when that API run was active and a stop was requested. Stop waits for a purchase in flight, as the Stop button does. A run in its way home counts as active: the stop then ends the way home.
- Returns false for unknown, terminal, or UI-started runs.
