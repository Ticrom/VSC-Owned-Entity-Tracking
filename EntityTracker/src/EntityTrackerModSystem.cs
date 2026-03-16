using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace EntityTracker
{
    public class EntityTrackerModSystem : ModSystem
    {
        private ICoreServerAPI sapi;
        private TrackerDatabase db;
        private long tickId;

        // Entity types we care about tracking
        private static readonly HashSet<string> TrackedEntityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sailboat", "boat", "raft",
            "wolf", "hyena", "aurochs", "moose", "bighorn",
            "sawtooth", "tameddeer", "elk", "deer"
        };

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            db = new TrackerDatabase(GamePaths.DataPath);

            api.Event.OnEntitySpawn += OnEntitySpawn;
            api.Event.OnEntityDespawn += OnEntityDespawn;
            api.Event.OnEntityDeath += OnEntityDeath;

            // Periodic position update every 60 seconds
            tickId = api.World.RegisterGameTickListener(OnPeriodicUpdate, 300000);

            RegisterCommands(api);

            api.Logger.Notification("[EntityTracker] Started.");
        }

        public override void Dispose()
        {
            if (sapi != null)
            {
                sapi.Event.OnEntitySpawn -= OnEntitySpawn;
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
                .RequiresPrivilege(Privilege.chat)
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
            string msg = $"[EntityTracker] Entities owned by '{results[0].OwnerName}' ({results.Count}):\n";
            foreach (var e in results)
            {
                int rx = (int)(e.X - spawn.X);
                int ry = (int)e.Y;
                int rz = (int)(e.Z - spawn.Z);
                string loc = e.Status == "active" ? $"({rx}, {ry}, {rz})" : $"last seen ({rx}, {ry}, {rz})";
                msg += $"  {e.EntityType} #{e.EntityId} - {e.Status} - {loc}\n";
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

        private void OnEntityDespawn(Entity entity, EntityDespawnData reason)
        {
            if (!IsTrackedType(entity)) return;
            db.UpdateStatus(entity.EntityId, "despawned");
        }

        private void OnEntityDeath(Entity entity, DamageSource damageSource)
        {
            if (!IsTrackedType(entity)) return;
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
                if (entity == null) continue;

                if (!entity.Alive)
                {
                    db.UpdateStatus(tracked.EntityId, "destroyed");
                    continue;
                }

                var pos = entity.ServerPos;
                db.UpdatePosition(tracked.EntityId, pos.X, pos.Y, pos.Z);
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
}
