## What

<!-- One or two sentences on what this PR changes. -->

## Why

<!-- The motivating problem, linked issue, or user-visible behavior this fixes. -->

Closes #

## How to test

<!--
Minimum steps a reviewer can run to verify the change. Testing usually means building a short shopping list, pressing Start, and watching the plugin plan, travel and buy at least one listing; for travel changes, /ashop goto <world> [aetheryte] runs the journey on its own. Make sure the dependencies listed in /ashop deps are installed first. For UI-only changes, describe what to click.
-->

## Checklist

- [ ] `dotnet build -c Release` passes
- [ ] Verified in-game on at least one shopping run, or with /ashop goto for travel-only changes
- [ ] If this changes user-visible behavior, README is updated
- [ ] If this touches the automation loop, relevant `[AutoShopper]` log lines make the sequence auditable
