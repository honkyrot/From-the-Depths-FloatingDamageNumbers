# Floating Damage Numbers — From the Depths Mod

Floating Damage Numbers adds optional, world-space combat feedback to [From the Depths](https://store.steampowered.com/app/268650/From_the_Depths/).

Numbers appear above damaged blocks or, in Simple Mode, above the construct's center of mass. Damage types can be shown with colors and the game's damage icons.

![image](readme/image1.png)

## Features

- Simple Mode for a cleaner, consolidated view above each construct.
- Detailed Mode for hit-position-based numbers with spatial aggregation.
- Damage-type colors and optional damage icons or text tags.
- Aggregation of rapid repeated hits to reduce visual clutter.

## Installation

Subscribe to the mod on the Steam Workshop and enable it in From the Depths' mod/plugin list. Then restart the game.

## Performance

The mod aggregates multiple block hits by construct and damage type, reuses a bounded pool of world-space number objects, and limits the number of active numbers per ship. Normal combat should be inexpensive.

Large explosions, rapid-fire weapons, lasers, and battles involving many damaged blocks still cause the game to process many damage callbacks. Detailed Mode also performs hit-position tracking and construct coordinate conversions. Extremely large battles may produce short frame-time spikes.

If performance or readability is a problem:

1. Use Simple Mode.
2. Increase the minimum damage threshold.
3. Keep the maximum numbers per ship and world pool size near their defaults.
4. Disable damage numbers with the toggle during very large battles.

This mod has not been tested on Linux and Mac builds.