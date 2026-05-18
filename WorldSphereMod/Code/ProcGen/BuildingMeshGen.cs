using UnityEngine;

namespace WorldSphereMod.ProcGen
{
    public static class BuildingMeshGen
    {
        public static Mesh Generate(BuildingAsset asset, BuildingRules rules)
        {
            // STUB until step 3 lands. Returns a unit cube so ProcGenCache plumbing can be validated.
            var m = new Mesh { name = $"procgen:stub:{asset?.id ?? "null"}" };

            Vector3[] verts =
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f),
                new Vector3(-0.5f,  0.5f,  0.5f),
            };

            int[] tris =
            {
                0, 2, 1, 0, 3, 2,
                1, 2, 6, 1, 6, 5,
                5, 6, 7, 5, 7, 4,
                4, 7, 3, 4, 3, 0,
                3, 7, 6, 3, 6, 2,
                4, 0, 1, 4, 1, 5,
            };

            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
