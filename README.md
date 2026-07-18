# Solar Expanse Launch Windows

A BepInEx plugin for Solar Expanse that adds a **Launch Windows** panel showing
optimal and fastest transfer windows to every planet (and any other body you add)
from your current origin.

## What it does

The panel appears as a button next to the Life Support / Power Tracker buttons in
the top bar. Click it to open, drag to reposition.

For each destination it shows two windows — one minimising Δv (Optimal) and one
minimising travel time within your spacecraft's Δv budget (Fastest) — plus a
dimmed second row with the next synodic-period window for longer-term planning.

**From / Craft dropdowns** — change origin body or filter by a spacecraft in your
fleet. Destinations outside the selected craft's Δv budget are shown in red.

**Presets** — dropdown that adds a group of destinations in one click: *My Bases*
(every body where you have a built facility) plus the game's own celestial body
groups — Near-Earth Objects, Inner Belt, Middle Belt, Outer Belt, Jupiter
Trojans, Kuiper Belt, and so on — exactly as the game classifies them.

**Clear** — header button that removes all destinations at once.

**Search** — type any body name to add it to the table.

### A note on accuracy

Windows are computed with the same Lambert solver and the same grid parameters the
game's Mission Planner uses internally, so departure dates match what you'll see
when you open the planner. Δv figures are close but not identical: the planner
applies company research bonuses and exact fuel-load optimisation that this panel
does not replicate. Treat the Δv numbers as planning estimates; the dates are the
important output.

## Installation

1. Install **BepInEx 5.4** if you haven't already:
   https://docs.bepinex.dev/articles/user_guide/installation/index.html
2. Run Solar Expanse once after installing BepInEx to generate the `BepInEx/plugins/` folder.
3. Download the latest release zip.
4. Extract `SolarExpanseLaunchWindows.dll` from the zip into:
   ```
   Solar Expanse/BepInEx/plugins/
   ```
5. Launch the game. The **LAUNCH WINDOWS** button will appear in the top bar.

## Building from source

See [CONTRIBUTING.md](CONTRIBUTING.md).
