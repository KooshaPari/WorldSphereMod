using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using NeoModLoader.constants;
using SQLite;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WorldSphereMod.Voxel
{
    [Table("voxel_cache")]
    internal sealed class VoxelCacheRow
    {
        [PrimaryKey, Column("sprite_name")]
        public string SpriteName { get; set; }

        [Column("vertex_data")]
        public byte[] VertexData { get; set; }

        [Column("index_data")]
        public byte[] IndexData { get; set; }

        [Column("color_data")]
        public byte[] ColorData { get; set; }

        [Column("normal_data")]
        public byte[] NormalData { get; set; }

        [Column("vertex_count")]
        public int VertexCount { get; set; }

        [Column("index_count")]
        public int IndexCount { get; set; }

        [Column("depth")]
        public int Depth { get; set; }

        [Column("style")]
        public string Style { get; set; }

        [Column("sprite_hash")]
        public string SpriteHash { get; set; }

        [Column("created_at")]
        public string CreatedAt { get; set; }
    }

    public static class VoxelDiskCache
    {
        struct PendingWrite
        {
            public string SpriteName;
            public byte[] VertexData;
            public byte[] IndexData;
            public byte[] ColorData;
            public byte[] NormalData;
            public int VertexCount;
            public int IndexCount;
            public int Depth;
            public string Style;
            public string SpriteHash;
        }

        static readonly ConcurrentQueue<PendingWrite> _writeQueue = new ConcurrentQueue<PendingWrite>();
        static int _flushCounter;
        const int FlushIntervalFrames = 60;
        static bool _initFailed;
        static string _dbPath;

        static string DbPath
        {
            get
            {
                if (_dbPath == null)
                {
                    _dbPath = Path.Combine(Paths.ModsConfigPath, "wsm3d-voxel-cache.db");
                }
                return _dbPath;
            }
        }

        static bool IsEnabled => Core.savedSettings != null && Core.savedSettings.VoxelDiskCache;

        static SQLiteConnection OpenConnection(SQLiteOpenFlags flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create)
        {
            var conn = new SQLiteConnection(DbPath, flags);
            conn.EnableWriteAheadLogging();
            conn.BusyTimeout = TimeSpan.FromSeconds(3);
            return conn;
        }

        static void EnsureSchema(SQLiteConnection conn)
        {
            conn.CreateTable<VoxelCacheRow>();
        }

        public static int WarmFromDisk(
            Dictionary<int, Mesh> insertCache,
            Dictionary<string, int> nameToSpriteId,
            Func<string, Sprite> spriteResolver)
        {
            if (!IsEnabled) return 0;
            if (_initFailed) return 0;

            int warmHits = 0;
            int stale = 0;
            int missing = 0;

            try
            {
                using (var conn = OpenConnection(SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create))
                {
                    EnsureSchema(conn);

                    var rows = conn.Table<VoxelCacheRow>().ToList();
                    var deleteNames = new List<string>();

                    string currentStyle = ResolveCurrentStyle();

                    foreach (var row in rows)
                    {
                        Sprite sprite = spriteResolver(row.SpriteName);
                        if (sprite == null)
                        {
                            missing++;
                            continue;
                        }

                        if (!string.Equals(row.Style, currentStyle, StringComparison.OrdinalIgnoreCase))
                        {
                            stale++;
                            deleteNames.Add(row.SpriteName);
                            continue;
                        }

                        string liveHash = ComputeSpriteHash(sprite);
                        if (!string.Equals(row.SpriteHash, liveHash, StringComparison.Ordinal))
                        {
                            stale++;
                            deleteNames.Add(row.SpriteName);
                            continue;
                        }

                        Mesh mesh = DeserializeMesh(row);
                        if (mesh == null)
                        {
                            stale++;
                            deleteNames.Add(row.SpriteName);
                            continue;
                        }

                        int key = sprite.GetInstanceID();
                        insertCache[key] = mesh;
                        nameToSpriteId[sprite.name] = key;
                        warmHits++;
                    }

                    if (deleteNames.Count > 0)
                    {
                        conn.RunInTransaction(() =>
                        {
                            foreach (string name in deleteNames)
                            {
                                conn.Execute("DELETE FROM voxel_cache WHERE sprite_name = ?", name);
                            }
                        });
                    }
                }

                Debug.Log($"[WSM3D] Disk cache: {warmHits} warm hits, {stale} stale, {missing} not cached (db={DbPath})");
            }
            catch (Exception ex)
            {
                _initFailed = true;
                Debug.LogWarning($"[WSM3D] Disk cache warm failed, falling back to async builds: {ex.Message}");
            }

            return warmHits;
        }

        public static void EnqueueSave(string spriteName, Mesh mesh, int depth, string style, string spriteHash)
        {
            if (!IsEnabled || _initFailed) return;
            if (mesh == null || string.IsNullOrEmpty(spriteName)) return;

            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            Color32[] colors = mesh.colors32;
            Vector3[] normals = mesh.normals;

            var write = new PendingWrite
            {
                SpriteName = spriteName,
                VertexData = SerializeVectors(verts),
                IndexData = SerializeInts(tris),
                ColorData = SerializeColors(colors),
                NormalData = normals != null && normals.Length > 0 ? SerializeVectors(normals) : null,
                VertexCount = verts.Length,
                IndexCount = tris.Length,
                Depth = depth,
                Style = style,
                SpriteHash = spriteHash,
            };
            _writeQueue.Enqueue(write);
        }

        public static void TickFlush()
        {
            if (!IsEnabled || _initFailed) return;
            if (_writeQueue.IsEmpty) return;

            _flushCounter++;
            if (_flushCounter < FlushIntervalFrames) return;
            _flushCounter = 0;

            FlushPendingWrites();
        }

        public static void FlushPendingWrites()
        {
            if (_writeQueue.IsEmpty) return;

            var batch = new List<PendingWrite>();
            while (_writeQueue.TryDequeue(out PendingWrite w))
            {
                batch.Add(w);
            }
            if (batch.Count == 0) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var conn = OpenConnection())
                    {
                        EnsureSchema(conn);

                        int maxSizeMB = Core.savedSettings != null ? Core.savedSettings.VoxelDiskCacheMaxSizeMB : 50;

                        conn.RunInTransaction(() =>
                        {
                            foreach (var w in batch)
                            {
                                var row = new VoxelCacheRow
                                {
                                    SpriteName = w.SpriteName,
                                    VertexData = w.VertexData,
                                    IndexData = w.IndexData,
                                    ColorData = w.ColorData,
                                    NormalData = w.NormalData,
                                    VertexCount = w.VertexCount,
                                    IndexCount = w.IndexCount,
                                    Depth = w.Depth,
                                    Style = w.Style,
                                    SpriteHash = w.SpriteHash,
                                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                                };
                                conn.InsertOrReplace(row);
                            }
                        });

                        EnforceSizeLimit(conn, maxSizeMB);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WSM3D] Disk cache flush failed: {ex.Message}");
                }
            });
        }

        public static void Purge()
        {
            try
            {
                if (File.Exists(DbPath))
                {
                    File.Delete(DbPath);
                    Debug.Log($"[WSM3D] Disk cache purged: {DbPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WSM3D] Disk cache purge failed: {ex.Message}");
            }
            _initFailed = false;
        }

        public static string ComputeSpriteHash(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return string.Empty;

            try
            {
                Color32[] tex = SpriteVoxelizer.GetPixelsCached(sprite.texture);
                Rect rect = sprite.textureRect;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(rect.x));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(rect.y));
                int w = Mathf.Max(1, Mathf.FloorToInt(rect.width));
                int h = Mathf.Max(1, Mathf.FloorToInt(rect.height));
                int texW = sprite.texture.width;

                byte[] rgba = new byte[w * h * 4];
                int dst = 0;
                for (int y = 0; y < h; y++)
                {
                    int row = (y0 + y) * texW + x0;
                    for (int x = 0; x < w; x++)
                    {
                        Color32 c = tex[row + x];
                        rgba[dst++] = c.r;
                        rgba[dst++] = c.g;
                        rgba[dst++] = c.b;
                        rgba[dst++] = c.a;
                    }
                }

                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(rgba);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool TryGetFromDisk(Sprite sprite, out Mesh mesh)
        {
            mesh = null;
            if (!IsEnabled || _initFailed) return false;
            if (sprite == null || string.IsNullOrEmpty(sprite.name)) return false;

            try
            {
                using (var conn = OpenConnection(SQLiteOpenFlags.ReadOnly))
                {
                    var row = conn.Find<VoxelCacheRow>(sprite.name);
                    if (row == null) return false;

                    string currentStyle = ResolveCurrentStyle();
                    if (!string.Equals(row.Style, currentStyle, StringComparison.OrdinalIgnoreCase))
                        return false;

                    string liveHash = ComputeSpriteHash(sprite);
                    if (!string.Equals(row.SpriteHash, liveHash, StringComparison.Ordinal))
                        return false;

                    mesh = DeserializeMesh(row);
                    return mesh != null;
                }
            }
            catch
            {
                return false;
            }
        }

        static Mesh DeserializeMesh(VoxelCacheRow row)
        {
            if (row.VertexData == null || row.IndexData == null || row.ColorData == null)
                return null;
            if (row.VertexCount <= 0 || row.IndexCount <= 0)
                return null;

            Vector3[] verts = DeserializeVectors(row.VertexData, row.VertexCount);
            int[] tris = DeserializeInts(row.IndexData, row.IndexCount);
            Color32[] colors = DeserializeColors(row.ColorData, row.VertexCount);

            if (verts == null || tris == null || colors == null)
                return null;

            var mesh = new Mesh { name = $"WSM3D.DiskCache.{row.SpriteName}" };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.colors32 = colors;

            if (row.NormalData != null && row.NormalData.Length >= row.VertexCount * 12)
            {
                Vector3[] normals = DeserializeVectors(row.NormalData, row.VertexCount);
                if (normals != null) mesh.normals = normals;
            }
            else
            {
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        static string ResolveCurrentStyle()
        {
            string raw = Core.savedSettings != null ? Core.savedSettings.VoxelInflationStyle : null;
            if (string.IsNullOrWhiteSpace(raw)) return "pertexel";
            return raw.Trim().ToLowerInvariant();
        }

        static void EnforceSizeLimit(SQLiteConnection conn, int maxSizeMB)
        {
            try
            {
                long sizeBytes = new FileInfo(DbPath).Length;
                long maxBytes = (long)maxSizeMB * 1024 * 1024;
                if (sizeBytes <= maxBytes) return;

                conn.Execute("DELETE FROM voxel_cache WHERE sprite_name IN (SELECT sprite_name FROM voxel_cache ORDER BY created_at ASC LIMIT 50)");
                Debug.Log($"[WSM3D] Disk cache pruned oldest 50 rows (size={sizeBytes / 1024}KB, limit={maxSizeMB}MB)");
            }
            catch { }
        }

        static byte[] SerializeVectors(Vector3[] vecs)
        {
            byte[] data = new byte[vecs.Length * 12];
            for (int i = 0; i < vecs.Length; i++)
            {
                int off = i * 12;
                Buffer.BlockCopy(BitConverter.GetBytes(vecs[i].x), 0, data, off, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(vecs[i].y), 0, data, off + 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(vecs[i].z), 0, data, off + 8, 4);
            }
            return data;
        }

        static Vector3[] DeserializeVectors(byte[] data, int count)
        {
            if (data.Length < count * 12) return null;
            var vecs = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int off = i * 12;
                vecs[i] = new Vector3(
                    BitConverter.ToSingle(data, off),
                    BitConverter.ToSingle(data, off + 4),
                    BitConverter.ToSingle(data, off + 8));
            }
            return vecs;
        }

        static byte[] SerializeInts(int[] vals)
        {
            byte[] data = new byte[vals.Length * 4];
            Buffer.BlockCopy(vals, 0, data, 0, data.Length);
            return data;
        }

        static int[] DeserializeInts(byte[] data, int count)
        {
            if (data.Length < count * 4) return null;
            var vals = new int[count];
            Buffer.BlockCopy(data, 0, vals, 0, count * 4);
            return vals;
        }

        static byte[] SerializeColors(Color32[] colors)
        {
            byte[] data = new byte[colors.Length * 4];
            for (int i = 0; i < colors.Length; i++)
            {
                int off = i * 4;
                data[off] = colors[i].r;
                data[off + 1] = colors[i].g;
                data[off + 2] = colors[i].b;
                data[off + 3] = colors[i].a;
            }
            return data;
        }

        static Color32[] DeserializeColors(byte[] data, int count)
        {
            if (data.Length < count * 4) return null;
            var colors = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                int off = i * 4;
                colors[i] = new Color32(data[off], data[off + 1], data[off + 2], data[off + 3]);
            }
            return colors;
        }
    }
}
