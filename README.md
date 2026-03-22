# VSC Owned Entity Tracking

Server-side Vintage Story mod that tracks owned entities (boats, mounts, tamed animals) in an SQLite database.

## Features

- Tracks entities when they are tagged/owned via the `ownedby` attribute
- Stores owner, entity type, location, and status in a persistent SQLite database
- Periodically updates positions of loaded tracked entities
- Automatically detects entity spawn, despawn, and death events

## Commands

| Command | Permission | Description |
|---------|-----------|-------------|
| `/track <playername>` | Configurable (default: `chat`) | Look up all entities owned by a player |
| `/trackscan` | `controlserver` | Bulk scan all loaded entities and add owned ones to the database |

## Tracked Entity Types

Boats (sailboat, raft), wolves, hyenas, aurochs, moose, bighorn sheep, sawtooths, elk/deer (tamed)

## Configuration

Edit `ModConfig/entitytracker.json` on the server:

```json
{
  "TrackCommandPrivilege": "chat",
  "UpdateIntervalSeconds": 300
}
```

- `TrackCommandPrivilege` - VS privilege required for `/track`. Options: `chat` (all players), `ban` (moderators), `controlserver` (admins)
- `UpdateIntervalSeconds` - How often to update positions of loaded tracked entities (default 300 = 5 minutes)

## Database

Stored at `VintagestoryData/ModData/entitytracker/entitytracker.db` (SQLite). Can be queried externally if needed.

## Install

Drop the zip into the server's `Mods` folder. Client-side installation is not required.
