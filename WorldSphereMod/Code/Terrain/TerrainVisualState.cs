using System;
using System.Collections.Generic;

namespace WorldSphereMod.Terrain
{
    /// <summary>Persistent terrain inputs, deliberately excluding transient map-layer RGB/tints.</summary>
    public readonly struct TerrainVisualState : IEquatable<TerrainVisualState>
    {
        readonly string mainType, topType;
        readonly int texture, classification;
        readonly float snow, frozen, corruption, burnt;

        public TerrainVisualState(string mainType, string topType, int texture, int classification,
            float snow, float frozen, float corruption, float burnt)
        {
            this.mainType = mainType;
            this.topType = topType;
            this.texture = texture;
            this.classification = classification;
            this.snow = snow;
            this.frozen = frozen;
            this.corruption = corruption;
            this.burnt = burnt;
        }

        public bool Equals(TerrainVisualState other) =>
            mainType == other.mainType && topType == other.topType && texture == other.texture &&
            classification == other.classification && snow == other.snow && frozen == other.frozen &&
            corruption == other.corruption && burnt == other.burnt;
    }

    /// <summary>Only changed tiles are examined; reset between worlds and shape managers.</summary>
    public sealed class TerrainVisualStateTracker
    {
        readonly Dictionary<int, TerrainVisualState> states = new Dictionary<int, TerrainVisualState>();
        public bool Observe(int index, TerrainVisualState state)
        {
            if (states.TryGetValue(index, out var prior) && prior.Equals(state)) return false;
            states[index] = state;
            return true;
        }
        public void Clear() => states.Clear();
    }
}
