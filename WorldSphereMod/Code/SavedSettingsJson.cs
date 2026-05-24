using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldSphereMod
{
    /// <summary>
    /// JSON deserialize + v1→v2 field migration for <see cref="SavedSettings"/>.
    /// Unity-free so unit tests can fuzz malformed on-disk payloads without the game host.
    /// </summary>
    public static class SavedSettingsJson
    {
        public static string ApplyTerrainSmoothingMigration(string raw)
        {
            if (!raw.Contains("\"TerrainSmoothing\"") || raw.Contains("\"MountainSlopeSmoothing\""))
            {
                return raw;
            }

            try
            {
                JObject obj = JObject.Parse(raw);
                if (obj["MountainSlopeSmoothing"] == null && obj["TerrainSmoothing"] != null)
                {
                    obj["MountainSlopeSmoothing"] = obj["TerrainSmoothing"]!.Value<bool>();
                }
                return obj.ToString();
            }
            catch
            {
                return raw;
            }
        }

        /// <summary>
        /// Parses settings JSON the same way <see cref="Core.LoadSettings"/> does before version handling.
        /// Never throws; returns false when the payload cannot be deserialized to a non-null object.
        /// </summary>
        public static bool TryDeserialize(string raw, out SavedSettings? settings)
        {
            settings = null;
            try
            {
                raw = ApplyTerrainSmoothingMigration(raw);
                settings = JsonConvert.DeserializeObject<SavedSettings>(raw);
                return settings != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
