using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Reflection-based kingdom color table for faction badges. Rebuilds when the
    /// upstream nation list size changes (no hard dependency on <c>Nations</c> types).
    /// </summary>
    public static class FactionBadgeAtlasBuilder
    {
        static Color[] _colors = Array.Empty<Color>();
        static int _cachedNationCount = -1;

        public static int NationCount => _colors.Length;

        public static void MaybeRebuild()
        {
            object? nations = ResolveNationsInstance();
            if (nations == null) return;

            int count = ResolveNationListCount(nations);
            if (count < 0 || count == _cachedNationCount) return;

            _cachedNationCount = count;
            _colors = new Color[Mathf.Max(1, count)];
            for (int i = 0; i < _colors.Length; i++)
            {
                _colors[i] = SampleNationColor(nations, i);
            }
        }

        public static bool TryGetBadgeColor(Actor actor, out Color color)
        {
            MaybeRebuild();
            object? kingdom = ResolveKingdom(actor);
            if (kingdom == null)
            {
                color = Color.clear;
                return false;
            }

            int index = ResolveKingdomIndex(kingdom);
            if (_colors.Length == 0)
            {
                color = ResolveKingdomColor(kingdom);
                return color.a > 0.01f;
            }

            index = Mathf.Abs(index) % _colors.Length;
            color = _colors[index];
            if (color.a < 0.01f) color = ResolveKingdomColor(kingdom);
            return color.a > 0.01f;
        }

        public static void Reset()
        {
            _colors = Array.Empty<Color>();
            _cachedNationCount = -1;
        }

        static object? ResolveNationsInstance()
        {
            foreach (var type in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? t = type.GetType("Nations", false);
                if (t == null) continue;
                PropertyInfo? inst = t.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (inst != null) return inst.GetValue(null);
                FieldInfo? field = t.GetField("instance", BindingFlags.Public | BindingFlags.Static);
                if (field != null) return field.GetValue(null);
            }
            return null;
        }

        static int ResolveNationListCount(object nations)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in new[] { "nations", "list", "kingdoms" })
            {
                FieldInfo? f = nations.GetType().GetField(name, flags);
                if (f?.GetValue(nations) is ICollection c) return c.Count;
                PropertyInfo? p = nations.GetType().GetProperty(name, flags);
                if (p?.GetValue(nations) is ICollection c2) return c2.Count;
            }
            return -1;
        }

        static Color SampleNationColor(object nations, int index)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object? entry = null;
            foreach (string name in new[] { "nations", "list", "kingdoms" })
            {
                FieldInfo? f = nations.GetType().GetField(name, flags);
                if (f?.GetValue(nations) is IList list && index >= 0 && index < list.Count)
                {
                    entry = list[index];
                    break;
                }
                PropertyInfo? p = nations.GetType().GetProperty(name, flags);
                if (p?.GetValue(nations) is IList list2 && index >= 0 && index < list2.Count)
                {
                    entry = list2[index];
                    break;
                }
            }

            if (entry == null) return Color.white;
            return ResolveKingdomColor(entry);
        }

        static object? ResolveKingdom(Actor actor)
        {
            if (actor == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type t = actor.GetType();
            foreach (string name in new[] { "kingdom", "nation", "kingdomObject" })
            {
                FieldInfo? f = t.GetField(name, flags);
                if (f != null)
                {
                    object? v = f.GetValue(actor);
                    if (v != null) return v;
                }
                PropertyInfo? p = t.GetProperty(name, flags);
                if (p != null && p.CanRead)
                {
                    object? v = p.GetValue(actor);
                    if (v != null) return v;
                }
            }
            MethodInfo? m = t.GetMethod("getKingdom", flags);
            if (m != null && m.GetParameters().Length == 0)
            {
                try { return m.Invoke(actor, null); } catch { }
            }
            return null;
        }

        static int ResolveKingdomIndex(object kingdom)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in new[] { "id", "kingdom_id", "index" })
            {
                FieldInfo? f = kingdom.GetType().GetField(name, flags);
                if (f != null)
                {
                    object? v = f.GetValue(kingdom);
                    if (v is int i) return i;
                }
                PropertyInfo? p = kingdom.GetType().GetProperty(name, flags);
                if (p != null && p.CanRead)
                {
                    object? v = p.GetValue(kingdom);
                    if (v is int i2) return i2;
                }
            }
            return kingdom.GetHashCode();
        }

        static Color ResolveKingdomColor(object kingdom)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in new[] { "kingdomColor", "color", "bannerColor", "mainColor" })
            {
                FieldInfo? f = kingdom.GetType().GetField(name, flags);
                if (f != null)
                {
                    object? v = f.GetValue(kingdom);
                    if (v is Color c) return c;
                }
                PropertyInfo? p = kingdom.GetType().GetProperty(name, flags);
                if (p != null && p.CanRead)
                {
                    object? v = p.GetValue(kingdom);
                    if (v is Color c2) return c2;
                }
            }
            return new Color(0.85f, 0.85f, 0.85f, 1f);
        }
    }
}
