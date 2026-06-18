#!/usr/bin/env bash
# Build and install SolarExpanseLaunchWindows.dll.
# Reads SOLAR_EXPANSE_ROOT from the environment (set by mise or manually).
# Legacy alias SOLAR_EXPANSE_GAME is also accepted.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Accept legacy SOLAR_EXPANSE_GAME alias
if [[ -z "${SOLAR_EXPANSE_ROOT:-}" && -n "${SOLAR_EXPANSE_GAME:-}" ]]; then
    export SOLAR_EXPANSE_ROOT="$SOLAR_EXPANSE_GAME"
fi

bash "$HERE/scripts/build"
