## What

<!-- One or two sentences on what this PR changes. -->

## Why

<!-- The motivating problem, linked issue, or user-visible behavior this fixes. -->

Closes #

## How to test

<!--
Minimum steps a reviewer can run to verify the change. Testing usually means building a short shopping list, getting prices on the Plan page, pressing Start with a small spend cap, and watching the plugin plan, travel and buy; for travel changes, /ashop goto <world> [aetheryte] runs the journey on its own. When the change touches sending or confirming a purchase, say which purchase you made. Make sure the dependencies listed in /ashop deps are installed first. For UI-only changes, describe what to click.
-->

## Checklist

- [ ] `dotnet build -c Release` passes and `dotnet test -c Release` is green
- [ ] Verified in-game on at least one run of a short, cheap list, or with /ashop goto for travel-only changes
- [ ] New or changed user-facing text is in `L*.cs` and all nine catalogs, same keys in the same order
- [ ] If this changes user-visible behavior, README is updated
- [ ] If this touches the run or the purchase path, relevant `[ASHOP]` log lines make the sequence auditable
