using Vintagestory.API.Common;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;


namespace EntityTracking;

public class EntityTrackingModSystem : ModSystem
{

        private ICoreServerAPI sapi;
        private TrackerDatabase db;
        private TrackerConfig config;
        private long tickId;

        private static readonly HashSet<string> TrackedEntityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sailboat", "boat", "raft", "canoe",
            "wolf", "hyena", "aurochs", "moose", "bighorn",
            "sawtooth", "tameddeer", "elk", "deer","horse"
        };

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            config = api.LoadModConfig<TrackerConfig>("entitytracker.json") ?? new TrackerConfig();
            api.StoreModConfig(config, "entitytracker.json");

            db = new TrackerDatabase(GamePaths.DataPath);

            api.Event.OnEntitySpawn += OnEntitySpawn;
            api.Event.OnEntityLoaded += OnEntityLoaded;
            api.Event.OnEntityDespawn += OnEntityDespawn;
            api.Event.OnEntityDeath += OnEntityDeath;

            tickId = api.World.RegisterGameTickListener(OnPeriodicUpdate, config.UpdateIntervalSeconds * 1000);

            RegisterCommands(api);

            api.Logger.Notification($"[EntityTracker] Started. Privilege: {config.TrackCommandPrivilege}, Update interval: {config.UpdateIntervalSeconds}s");
        }

        public override void Dispose()
        {
            if (sapi != null)
            {
                sapi.Event.OnEntitySpawn -= OnEntitySpawn;
                sapi.Event.OnEntityLoaded -= OnEntityLoaded;
                sapi.Event.OnEntityDespawn -= OnEntityDespawn;
                sapi.Event.OnEntityDeath -= OnEntityDeath;
                sapi.World.UnregisterGameTickListener(tickId);
            }
            db?.Dispose();
            base.Dispose();
        }

        private void RegisterCommands(ICoreServerAPI api)
        {
            var parsers = api.ChatCommands.Parsers;

            api.ChatCommands.Create("track")
                .RequiresPrivilege(config.TrackCommandPrivilege)
                .WithDescription("Look up tracked entities by player name. Usage: /track <playername>")
                .WithArgs(parsers.Word("playername"))
                .HandleWith(CmdLookup);

            api.ChatCommands.Create("trackscan")
                .RequiresPrivilege(Privilege.controlserver)
                .WithDescription("Scan all loaded entities for owned ones and add to tracker.")
                .HandleWith(CmdScan);
        }

        // ------------------------------------------------------------------
        // Commands
        // ------------------------------------------------------------------

        private TextCommandResult CmdScan(TextCommandCallingArgs args)
        {
            int count = ScanLoadedEntities();
            return TextCommandResult.Success($"[EntityTracker] Scan complete. {count} owned entities found/updated.");
        }

        private TextCommandResult CmdLookup(TextCommandCallingArgs args)
        {
            string playerName = args[0] as string;
            if (string.IsNullOrWhiteSpace(playerName))
                return TextCommandResult.Success("[EntityTracker] Usage: /track <playername>");

            var results = db.FindByOwnerName(playerName);
            if (results.Count == 0)
                return TextCommandResult.Success($"[EntityTracker] No tracked entities found for '{playerName}'.");

            var spawn = sapi.World.DefaultSpawnPosition;
            string msg = $"[EntityTracker] Entities for '{playerName}' ({results.Count}):\n";
            foreach (var e in results)
            {
                int rx = (int)(e.X - spawn.X);
                int ry = (int)e.Y;
                int rz = (int)(e.Z - spawn.Z);
                string loc = $"({rx}, {ry}, {rz})";
                string statusLabel = e.Status switch
                {
                    "active" => "active",
                    "unloaded" => "unloaded, last at",
                    "destroyed" => "DESTROYED, was at",
                    "removed" => "REMOVED, was at",
                    "untagged" => "UNTAGGED, was at",
                    _ => e.Status + ", last at"
                };
                msg += $"  {e.EntityType} #{e.EntityId} - {statusLabel} {loc}\n";
            }

            return TextCommandResult.Success(msg.TrimEnd());
        }

        // ------------------------------------------------------------------
        // Entity events
        // ------------------------------------------------------------------

        private void OnEntitySpawn(Entity entity)
        {
            TryTrackEntity(entity);
        }

        private void OnEntityLoaded(Entity entity)
        {
            if (!IsTrackedType(entity)) return;

            string ownerUid = GetOwnerUid(entity);
            if (string.IsNullOrEmpty(ownerUid)) return;

            string ownerName = ResolvePlayerName(ownerUid);
            string entityType = entity.Code?.Path ?? "unknown";
            var pos = entity.ServerPos;

            // Re-activate if it was marked unloaded
            db.Upsert(ownerUid, ownerName, entityType, entity.EntityId, pos.X, pos.Y, pos.Z);
        }

        private void OnEntityDespawn(Entity entity, EntityDespawnData reason)
        {
            if (!IsTrackedType(entity)) return;

            string status = reason?.Reason switch
            {
                EnumDespawnReason.Unload => "unloaded",
                EnumDespawnReason.Death => "destroyed",
                EnumDespawnReason.Combusted => "destroyed",
                EnumDespawnReason.Removed => "removed",
                EnumDespawnReason.Expire => "removed",
                _ => "unloaded"
            };

            // Update position one last time before marking status
            var pos = entity.ServerPos;
            db.UpdatePosition(entity.EntityId, pos.X, pos.Y, pos.Z);
            db.UpdateStatus(entity.EntityId, status);
        }

        private void OnEntityDeath(Entity entity, DamageSource damageSource)
        {
            if (!IsTrackedType(entity)) return;

            var pos = entity.ServerPos;
            db.UpdatePosition(entity.EntityId, pos.X, pos.Y, pos.Z);
            db.UpdateStatus(entity.EntityId, "destroyed");
        }

        // ------------------------------------------------------------------
        // Tracking logic
        // ------------------------------------------------------------------

        private void TryTrackEntity(Entity entity)
        {
            if (!IsTrackedType(entity)) return;

            string ownerUid = GetOwnerUid(entity);
            if (string.IsNullOrEmpty(ownerUid)) return;

            string ownerName = ResolvePlayerName(ownerUid);
            string entityType = entity.Code?.Path ?? "unknown";
            var pos = entity.ServerPos;

            db.Upsert(ownerUid, ownerName, entityType, entity.EntityId, pos.X, pos.Y, pos.Z);
        }

        private int ScanLoadedEntities()
        {
            int count = 0;
            foreach (var entity in sapi.World.LoadedEntities.Values)
            {
                if (!IsTrackedType(entity)) continue;

                string ownerUid = GetOwnerUid(entity);
                if (string.IsNullOrEmpty(ownerUid)) continue;

                string ownerName = ResolvePlayerName(ownerUid);
                string entityType = entity.Code?.Path ?? "unknown";
                var pos = entity.ServerPos;

                db.Upsert(ownerUid, ownerName, entityType, entity.EntityId, pos.X, pos.Y, pos.Z);
                count++;
            }
            return count;
        }

        private void OnPeriodicUpdate(float dt)
        {
            var active = db.GetAllActive();
            foreach (var tracked in active)
            {
                var entity = sapi.World.GetEntityById(tracked.EntityId);
                if (entity == null) continue; // Not loaded, skip

                if (!entity.Alive)
                {
                    db.UpdateStatus(tracked.EntityId, "destroyed");
                    continue;
                }

                // Check if ownership was removed (tag taken off)
                string currentOwner = GetOwnerUid(entity);
                if (string.IsNullOrEmpty(currentOwner))
                {
                    db.UpdateStatus(tracked.EntityId, "untagged");
                    continue;
                }

                // Check if ownership changed to a different player
                if (currentOwner != tracked.OwnerUid)
                {
                    string newName = ResolvePlayerName(currentOwner);
                    string entityType = entity.Code?.Path ?? "unknown";
                    var pos = entity.ServerPos;
                    db.Upsert(currentOwner, newName, entityType, tracked.EntityId, pos.X, pos.Y, pos.Z);
                    continue;
                }

                // Normal position update
                var epos = entity.ServerPos;
                db.UpdatePosition(tracked.EntityId, epos.X, epos.Y, epos.Z);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private bool IsTrackedType(Entity entity)
        {
            if (entity?.Code?.Path == null) return false;
            string path = entity.Code.Path;

            foreach (var tracked in TrackedEntityTypes)
            {
                if (path.Contains(tracked, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private string GetOwnerUid(Entity entity)
        {
            var ownedBy = entity.WatchedAttributes.GetTreeAttribute("ownedby");
            return ownedBy?.GetString("uid") ?? "";
        }

        private string ResolvePlayerName(string uid)
        {
            var playerData = sapi.PlayerData.GetPlayerDataByUid(uid);
            return playerData?.LastKnownPlayername ?? "Unknown";
        }
    }
    