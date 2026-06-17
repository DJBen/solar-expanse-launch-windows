# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

## [1.2.0] - 2026-06-17
### Added
- **Launch window alarms** — checkbox on each window row fires a real game notification when the departure window arrives, pausing the game. Notification shows origin and destination planet icons with highlighted names.
- **Clickable destination names** — click a destination to open its in-game info panel. Planet icon shown alongside each name.
- **My Bases** now adds the parent planet when a facility is on a moon (e.g. a base on Callisto also adds Jupiter).
- My Bases tooltip explaining inclusion criteria.
- Per-origin window cache — switching back to a previously-calculated origin restores cached data immediately instead of recalculating.
- Column headers (DESTINATION / OPTIMAL / FASTEST) use the game's own locale strings.

### Fixed
- Column headers, sub-headers, and data cells now align correctly.
- Solar sail craft (e.g. Daedalus) no longer shows incorrect Fastest windows; solar range shown in AU; out-of-range destinations greyed out.
- Spacecraft in transit (not docked) now appear in the Craft dropdown.
- Saved destination lists now persist across multiple saves of the same campaign.
- Selected craft is restored when reloading a save.
- My Bases excludes exploration probes.
- Switching to an origin with no saved destinations now picks a sensible default instead of showing an empty panel.
- Search dropdown no longer lists bodies already in the table.
- Search input no longer loses keyboard focus while typing.
- Saved data now loads correctly on game startup.

## [1.1.0] - 2026-06-06
### Added
- From dropdown is now a typeahead: opens with a filter box, list sorted alphabetically.

## [1.0.0] - 2026-06-03
### Added
- Initial release.
- Launch Windows panel showing optimal and fastest transfer windows for all planets.
- Second row per destination with next synodic-period window for longer-term planning.
- From dropdown to change origin body.
- Craft dropdown to set Δv budget; destinations outside budget shown in red.
- My Bases button to auto-add bodies where the player has built facilities.
- Body search to add any celestial body to the table.
- Calculating overlay shown during background Lambert grid computation.
- Data clears immediately on origin or craft change so stale values are never visible.
