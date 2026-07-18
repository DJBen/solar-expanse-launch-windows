# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]
### Added
- **Presets dropdown** replaces the My Bases button. Contains My Bases plus one entry per in-game celestial body group (`ObjectInfoGroups`): Near-Earth Objects, Inner Belt, Middle Belt, Outer Belt, Jupiter Trojans, Kuiper Belt, etc. — the same classification the game's search window uses, listed sunward-out. Clicking a preset adds every group member known to the ephemeris.
- **Clear** button in the panel header removes all destinations at once.
### Changed
- Removed the status line ("Updated…", "Calculating…", "Added N…") between the header and the table; the full-panel Calculating overlay already covers refresh feedback.
- Entire UI enlarged ~1.5×: fonts (labels 15pt, row values 16pt, tooltips 13pt), panel 650×380→975×570, and all column widths, row heights, dropdowns, toasts, and the toggle button scaled proportionally.
- Per-row × delete button moved from the name cell to the trailing edge of the row.
- Both opportunity rows now use the same 15pt value font (first row is no longer larger — dimming alone distinguishes the next window), and the Δv columns are narrower (95/110px).
### Fixed
- Departure column widened (62→72 / 70→80 px) so the sort arrow ("Departs ▲/▼") is no longer ellipsized at the larger font size.
- Destroyed bodies (`ObjectInfo.IsInGameDestroy` — impacted, nuked, or mined-out asteroids like EX0-99) are now hidden from presets, search results, and the origin dropdown, and are auto-removed from the destination table on refresh.

## [1.2.6] - 2026-07-03
### Fixed
- Clicking a body name in the launch window panel now correctly opens the body's detail panel. Previously used a reflection-based method that broke after a game update.

## [1.2.5] - 2026-06-27
### Changed
- Origin dropdown sort is now three-tier: presence → planet/non-planet → alphabetical.

## [1.2.4] - 2026-06-24
### Added
- Origin dropdown now shows planets where you have ships first, then everything else. Both groups alphabetized.
- Destinations are now saved to the sidecar file continuously as you make changes, not only on game save. Fixes destinations disappearing after switching origins or reloading.

## [1.2.3] - 2026-06-18
### Fixed
- Fastest checkboxes in the second window row were misaligned with the first row.
- Unchecked checkboxes no longer show a hazy background box.

## [1.2.2] - 2026-06-18
### Fixed
- `_firedAlarms` set is now cleared on sidecar load, preventing unbounded growth across a long session.
- Per-origin window caches restored from the sidecar now go through the same opt2/fst promotion logic as the active origin; previously a restored non-active origin that had been promoted at save-time would never compute its second window after being switched to.
- Pending opt2/fst recalc sets are now saved and restored when switching origins, so a partial recalc in-flight when the user switches away is correctly resumed on switch-back.

## [1.2.1] - 2026-06-18
### Fixed
- Craft change now clears cached fastest windows for all origins, not just the active one; previously switching craft then switching away and back to an origin could show fastest windows computed with the old craft's dV budget.
- Per-origin window caches are now persisted to the sidecar file and restored on load, so switching origins after a reload no longer forces a full recalculation.
- Background Lambert grid calculations now cap thread usage at ProcessorCount−2, leaving headroom for the game's render thread.

### Changed
- Build system aligned with FleetTracker: uses `SOLAR_EXPANSE_ROOT` environment variable (set via `.mise.toml`); csproj validates the path and auto-copies the DLL after build. `SOLAR_EXPANSE_GAME` is still accepted as a legacy alias.
- Added `mise run test` task (`scripts/test`) for running the unit suite without a game install.
- Save format bumped to version 3 (backwards compatible; old saves load without data loss).

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
