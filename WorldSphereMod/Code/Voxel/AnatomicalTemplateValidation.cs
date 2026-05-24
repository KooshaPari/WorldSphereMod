using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Rig;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Rejects degenerate template builds before they replace extrusion (spec §6).
    /// </summary>
    public static class AnatomicalTemplateValidation
    {
        public const int MinOccupiedVoxels = 8;

        public static bool TryValidate(in AnatomicalTemplate template, out string reason)
        {
            reason = null;
            if (template.RigType == RigType.None || template.RigType == RigType.Static)
            {
                reason = "rig type has no anatomical template";
                return false;
            }

            AnatomicalVoxel[] voxels = template.Voxels;
            if (voxels == null || voxels.Length < MinOccupiedVoxels)
            {
                reason = $"too few occupied voxels (need >= {MinOccupiedVoxels})";
                return false;
            }

            if (!HasConnectedMass(voxels))
            {
                reason = "disconnected body parts";
                return false;
            }

            return true;
        }

        static bool HasConnectedMass(AnatomicalVoxel[] voxels)
        {
            var occupied = new HashSet<Vector3Int>();
            for (int i = 0; i < voxels.Length; i++)
            {
                occupied.Add(voxels[i].Local);
            }

            if (occupied.Count == 0)
            {
                return false;
            }

            var queue = new Queue<Vector3Int>();
            var visited = new HashSet<Vector3Int>();
            using var enumerator = occupied.GetEnumerator();
            enumerator.MoveNext();
            Vector3Int start = enumerator.Current;
            queue.Enqueue(start);
            visited.Add(start);

            static IEnumerable<Vector3Int> Neighbors(Vector3Int p)
            {
                yield return p + Vector3Int.right;
                yield return p + Vector3Int.left;
                yield return p + Vector3Int.up;
                yield return p + Vector3Int.down;
                yield return p + Vector3Int.forward;
                yield return p + Vector3Int.back;
            }

            while (queue.Count > 0)
            {
                Vector3Int current = queue.Dequeue();
                foreach (Vector3Int next in Neighbors(current))
                {
                    if (!occupied.Contains(next) || visited.Contains(next))
                    {
                        continue;
                    }

                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            return visited.Count == occupied.Count;
        }
    }
}
