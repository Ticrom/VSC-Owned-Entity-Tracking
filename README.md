# VSC Owned Entity Tracking

Server-side Vintage Story mod that tracks owned entities (boats, mounts, tamed animals) in an SQLite database.

## Features

- Tracks entities when they are tagged/owned via the `ownedby` attribute
- Stores owner, entity type, location, and status in a persistent SQLite database
- Periodically updates positions of loaded tracked entities
- Automatically detects entity spawn, despawn, death, and ownership change events
- Configurable tracked entity types, update interval, and command permissions

## Install

Drop the zip into the server's `Mods` folder. Client-side installation is not required.

## Commands

| Command | Permission | Description |
|---------|------------|-------------|
| `/selftrack` | Configurable (default: `chat`) | Look up your own tracked entities |
| `/track <playername>` | `ban` | Look up all entities owned by a specific player |
| `/trackscan` | `ban` | Bulk scan all loaded entities and add owned ones to the database |
| `/tracktp <entityid>` | `ban` | Teleport a tracked entity to its owner's current location |
| `/trackreload` | `ban` | Reload the config file from disk without restarting the server |

Coordinates reported by commands are relative to the world spawn point.

## Configuration

Edit `ModConfig/entitytracker.json` on the server. The file is created with defaults on first run.

```json
{
  "TrackCommandPrivilege": "chat",
  "UpdateIntervalSeconds": 300,
  "TrackedEntityTypes": [
    "sailboat", "boat", "raft", "canoe",
    "wolf", "hyena", "aurochs", "moose", "bighorn",
    "sawtooth", "tameddeer", "elk", "deer", "horse"
  ]
}
```

| Option | Default | Description |
|--------|---------|-------------|
| `TrackCommandPrivilege` | `"chat"` | Privilege required for `/selftrack`. See privilege levels below. |
| `UpdateIntervalSeconds` | `300` | How often (in seconds) to update positions of loaded tracked entities. |
| `TrackedEntityTypes` | *(list above)* | Entity type codes to track. Only entities whose type appears in this list are recorded. |

**Privilege levels:** `chat` (all players) → `ban` (moderators) → `controlserver` (admins)

## Entity Statuses

| Status | Meaning |
|--------|---------|
| `active` | Entity is currently loaded and being tracked |
| `unloaded` | Entity was unloaded (chunk unload); will reactivate when loaded again |
| `destroyed` | Entity died or was deleted |
| `removed` | Entity's owner was explicitly removed |
| `untagged` | The `ownedby` attribute was stripped from the entity |

## Database

Stored at `VintagestoryData/ModData/entitytracker/entitytracker.db` (SQLite). Can be queried externally if needed.

## Building from Source

Requires .NET 8 SDK and a local Vintage Story installation.

```bash
# Debug build (default)
./build.sh

# Release build
./build.sh -c Release
# or on Windows:
./build.ps1 -c Release
```

Output zip is placed in `Releases/`.
