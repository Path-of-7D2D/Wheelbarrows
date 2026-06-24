# Wheelbarrow

A 7 Days To Die V3.0 mod that adds a craftable, pushable wheelbarrow with storage.

The wheelbarrow is meant to be a small hand cart: walk behind it, push it around, use it for extra storage, and pick it back up when you are done.

## Features

- Craftable wheelbarrow placeable item.
- Walk-behind push interaction.
- Built-in storage.
- Custom item and compass icons.
- Wheelbarrow-specific interaction prompt.
- Admin/testing console commands for spawning, cleanup, and diagnostics.

## Requirements

- 7 Days To Die V3.0.
- Easy Anti-Cheat disabled.

This mod includes a DLL for the push behavior and console commands, so it is marked with `SkipWithAntiCheat`.

For multiplayer, install the mod on the server and on each client that connects.

## Install

1. Download or clone this repository.
2. Copy the `1A-Wheelbarrow` folder into your 7 Days To Die `Mods` folder.
3. Start the game with Easy Anti-Cheat disabled.

Typical Steam install path:

```text
C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods\1A-Wheelbarrow
```

If the `Mods` folder does not exist, create it.

## Using The Wheelbarrow

Craft the wheelbarrow at a workbench:

```text
1x vehicle wheel
30x wood
12x forged iron
2x mechanical parts
4x leather
```

Place the wheelbarrow like a vehicle. Look at it and press the interact key when prompted:

```text
( E ) to Push Wheelbarrow
```

Press interact again to release it. Changing toolbelt slots while pushing also releases it before switching items.

Use the normal vehicle interaction options for storage, pickup, repair, lock, and related actions.

## Admin Commands

These are mostly for testing or cleanup:

```text
wb
wb 5
wb push
wb drop
wb cleanup
wb debug
```

Aliases:

```text
wheelbarrow
spawnwheelbarrow
wbspawn
```

Command behavior:

- `wb` spawns a wheelbarrow in front of you.
- `wb 5` spawns one 5 meters away.
- `wb push` starts pushing the nearest active wheelbarrow.
- `wb drop` releases the pushed wheelbarrow.
- `wb cleanup` removes active and unloaded wheelbarrow records.
- `wb debug` logs active wheelbarrow renderer and transform state.

## Current Limitations

This is still a custom vehicle prototype. The wheelbarrow uses 7D2D vehicle systems underneath, with custom code layered on top for walk-behind pushing. Some movement and physics edge cases may still need tuning.

For development notes, build instructions, and asset workflow details, see `CONTRIBUTORS.md`.
