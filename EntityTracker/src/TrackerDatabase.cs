using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace EntityTracker
{
    public class TrackedEntity
    {
        public string OwnerUid;
        public string OwnerName;
        public string EntityType;
        public long EntityId;
        public double X, Y, Z;
        public string Status;
        public string LastSeen;
        public string FirstTracked;
    }

    public class TrackerDatabase : IDisposable
    {
        private SqliteConnection conn;

        public TrackerDatabase(string dataPath)
        {
            string dir = Path.Combine(dataPath, "ModData", "entitytracker");
            Directory.CreateDirectory(dir);
            string dbPath = Path.Combine(dir, "entitytracker.db");

            conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            CreateSchema();
        }

        private void CreateSchema()
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS tracked_entities (
                    entity_id     INTEGER PRIMARY KEY,
                    owner_uid     TEXT NOT NULL,
                    owner_name    TEXT NOT NULL,
                    entity_type   TEXT NOT NULL,
                    x             REAL NOT NULL DEFAULT 0,
                    y             REAL NOT NULL DEFAULT 0,
                    z             REAL NOT NULL DEFAULT 0,
                    status        TEXT NOT NULL DEFAULT 'active',
                    last_seen     TEXT NOT NULL,
                    first_tracked TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_owner_uid ON tracked_entities(owner_uid);
                CREATE INDEX IF NOT EXISTS idx_owner_name ON tracked_entities(owner_name COLLATE NOCASE);
            ";
            cmd.ExecuteNonQuery();
        }

        public void Upsert(string ownerUid, string ownerName, string entityType, long entityId, double x, double y, double z)
        {
            string now = DateTime.UtcNow.ToString("o");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tracked_entities (entity_id, owner_uid, owner_name, entity_type, x, y, z, status, last_seen, first_tracked)
                VALUES ($eid, $uid, $name, $type, $x, $y, $z, 'active', $now, $now)
                ON CONFLICT(entity_id) DO UPDATE SET
                    owner_uid = $uid,
                    owner_name = $name,
                    x = $x, y = $y, z = $z,
                    status = 'active',
                    last_seen = $now;
            ";
            cmd.Parameters.AddWithValue("$eid", entityId);
            cmd.Parameters.AddWithValue("$uid", ownerUid);
            cmd.Parameters.AddWithValue("$name", ownerName);
            cmd.Parameters.AddWithValue("$type", entityType);
            cmd.Parameters.AddWithValue("$x", x);
            cmd.Parameters.AddWithValue("$y", y);
            cmd.Parameters.AddWithValue("$z", z);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }

        public void UpdatePosition(long entityId, double x, double y, double z)
        {
            string now = DateTime.UtcNow.ToString("o");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE tracked_entities SET x=$x, y=$y, z=$z, last_seen=$now WHERE entity_id=$eid";
            cmd.Parameters.AddWithValue("$eid", entityId);
            cmd.Parameters.AddWithValue("$x", x);
            cmd.Parameters.AddWithValue("$y", y);
            cmd.Parameters.AddWithValue("$z", z);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }

        public void UpdateStatus(long entityId, string status)
        {
            string now = DateTime.UtcNow.ToString("o");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE tracked_entities SET status=$status, last_seen=$now WHERE entity_id=$eid";
            cmd.Parameters.AddWithValue("$eid", entityId);
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }

        public List<TrackedEntity> FindByOwnerName(string name)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM tracked_entities WHERE owner_name LIKE $name COLLATE NOCASE";
            cmd.Parameters.AddWithValue("$name", "%" + name + "%");
            return ReadEntities(cmd);
        }

        public List<TrackedEntity> FindByOwnerUid(string uid)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM tracked_entities WHERE owner_uid = $uid";
            cmd.Parameters.AddWithValue("$uid", uid);
            return ReadEntities(cmd);
        }

        public List<TrackedEntity> GetAllActive()
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM tracked_entities WHERE status = 'active'";
            return ReadEntities(cmd);
        }

        private List<TrackedEntity> ReadEntities(SqliteCommand cmd)
        {
            var list = new List<TrackedEntity>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new TrackedEntity
                {
                    EntityId = reader.GetInt64(reader.GetOrdinal("entity_id")),
                    OwnerUid = reader.GetString(reader.GetOrdinal("owner_uid")),
                    OwnerName = reader.GetString(reader.GetOrdinal("owner_name")),
                    EntityType = reader.GetString(reader.GetOrdinal("entity_type")),
                    X = reader.GetDouble(reader.GetOrdinal("x")),
                    Y = reader.GetDouble(reader.GetOrdinal("y")),
                    Z = reader.GetDouble(reader.GetOrdinal("z")),
                    Status = reader.GetString(reader.GetOrdinal("status")),
                    LastSeen = reader.GetString(reader.GetOrdinal("last_seen")),
                    FirstTracked = reader.GetString(reader.GetOrdinal("first_tracked"))
                });
            }
            return list;
        }

        public void Dispose()
        {
            conn?.Close();
            conn?.Dispose();
        }
    }
}
