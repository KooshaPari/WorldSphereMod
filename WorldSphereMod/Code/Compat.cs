#if !NET5_0_OR_GREATER
namespace WorldSphereMod
{
    internal static class WSMath
    {
        public static float Clamp(float value, float min, float max)
            => value < min ? min : (value > max ? max : value);

        public static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        public static double Clamp(double value, double min, double max)
            => value < min ? min : (value > max ? max : value);
    }
}

namespace System.Collections.Generic
{
    internal static class DictionaryPolyfill
    {
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue value)
        {
            if (dict.ContainsKey(key)) return false;
            dict[key] = value;
            return true;
        }
    }
}
#endif
