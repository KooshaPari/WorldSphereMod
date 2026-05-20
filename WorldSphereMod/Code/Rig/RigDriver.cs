using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Rig
{
    /// <summary>
    /// Per-actor rig submission. Humanoid actors are rigid-skinned on the CPU from
    /// <see cref="AnimationFrameData"/> into a per-actor proxy mesh; other rig types
    /// currently fall back to the static voxel mesh path.
    /// </summary>
    public static class RigDriver
    {
        sealed class ActorProxyMesh
        {
            public long SourceKey;
            public Mesh Mesh;
            public Vector3[] SourceVertices = System.Array.Empty<Vector3>();
            public Vector3[] SourceNormals = System.Array.Empty<Vector3>();
            public Vector3[] Vertices = System.Array.Empty<Vector3>();
            public Vector3[] Normals = System.Array.Empty<Vector3>();
        }

        static readonly Dictionary<long, ActorProxyMesh> _actorMeshes = new Dictionary<long, ActorProxyMesh>();

        public static bool SubmitSkinnedActor(
            Actor a, Vector3 pos, Quaternion rot, Vector3 scl, Color tint,
            RigType rigType)
        {
            if (a == null || a.asset == null)
            {
                return false;
            }

            Sprite? sp = a.calculateMainSprite();
            if (sp == null)
            {
                return false;
            }

            SkinnedVoxelMesh svm = RigCache.GetOrBuild(sp, rigType);
            if (svm.BaseMesh == null || svm.BaseMesh.vertexCount == 0)
            {
                return false;
            }

            Mesh meshToSubmit = svm.BaseMesh;
            if (rigType == RigType.Humanoid && svm.BoneIndices != null && svm.BoneIndices.Length > 0)
            {
                meshToSubmit = SkinHumanoid(a, sp, svm);
                if (meshToSubmit == null)
                {
                    return false;
                }
            }

            Matrix4x4 trs = Matrix4x4.TRS(pos, rot, scl);
            return VoxelRender.Submit(meshToSubmit, trs, tint);
        }

        static Mesh SkinHumanoid(Actor actor, Sprite sprite, SkinnedVoxelMesh svm)
        {
            long actorKey = actor.GetHashCode();
            long sourceKey = ((long)(uint)sprite.GetInstanceID() << 8) | (byte)svm.RigType;
            ActorProxyMesh proxy = GetOrCreateProxy(actorKey, sourceKey, svm);
            if (proxy.Mesh == null)
            {
                return null;
            }

            Matrix4x4[] boneMatrices = HumanoidRig.Evaluate(actor.getAnimationFrameData(), 1f);
            SkinProxy(proxy, svm, boneMatrices);
            return proxy.Mesh;
        }

        static ActorProxyMesh GetOrCreateProxy(long actorKey, long sourceKey, SkinnedVoxelMesh svm)
        {
            if (_actorMeshes.TryGetValue(actorKey, out ActorProxyMesh proxy) && proxy.SourceKey == sourceKey && proxy.Mesh != null)
            {
                return proxy;
            }

            if (proxy != null && proxy.Mesh != null)
            {
                Object.Destroy(proxy.Mesh);
            }

            var mesh = new Mesh { name = $"{svm.BaseMesh.name}:skinned:{actorKey}" };
            if (svm.BaseMesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(svm.BaseMesh.vertices);
            var colors = svm.BaseMesh.colors32;
            if (colors != null && colors.Length == svm.BaseMesh.vertexCount)
            {
                mesh.SetColors(colors);
            }
            var normals = svm.BaseMesh.normals;
            if (normals != null && normals.Length == svm.BaseMesh.vertexCount)
            {
                mesh.SetNormals(normals);
            }
            mesh.SetTriangles(svm.BaseMesh.triangles, 0);
            mesh.RecalculateBounds();
            mesh.MarkDynamic();

            proxy = new ActorProxyMesh
            {
                SourceKey = sourceKey,
                Mesh = mesh,
                SourceVertices = svm.BaseMesh.vertices,
                SourceNormals = svm.BaseMesh.normals,
                Vertices = new Vector3[svm.BaseMesh.vertexCount],
                Normals = normals != null && normals.Length == svm.BaseMesh.vertexCount
                    ? new Vector3[svm.BaseMesh.vertexCount]
                    : System.Array.Empty<Vector3>(),
            };
            _actorMeshes[actorKey] = proxy;
            return proxy;
        }

        static void SkinProxy(ActorProxyMesh proxy, SkinnedVoxelMesh svm, Matrix4x4[] boneMatrices)
        {
            Vector3[] srcVerts = proxy.SourceVertices;
            Vector3[] srcNormals = proxy.SourceNormals;
            byte[] boneIndices = svm.BoneIndices ?? System.Array.Empty<byte>();

            if (proxy.Vertices.Length != srcVerts.Length)
            {
                proxy.Vertices = new Vector3[srcVerts.Length];
            }

            bool hasNormals = srcNormals != null && srcNormals.Length == srcVerts.Length;
            if (hasNormals && proxy.Normals.Length != srcVerts.Length)
            {
                proxy.Normals = new Vector3[srcVerts.Length];
            }

            for (int i = 0; i < srcVerts.Length; i++)
            {
                int boneIndex = i < boneIndices.Length ? boneIndices[i] : 0;
                if (boneIndex < 0 || boneIndex >= boneMatrices.Length)
                {
                    boneIndex = 0;
                }

                Matrix4x4 m = boneMatrices[boneIndex];
                proxy.Vertices[i] = m.MultiplyPoint3x4(srcVerts[i]);
                if (hasNormals)
                {
                    proxy.Normals[i] = m.MultiplyVector(srcNormals[i]).normalized;
                }
            }

            proxy.Mesh.SetVertices(proxy.Vertices);
            if (hasNormals)
            {
                proxy.Mesh.SetNormals(proxy.Normals);
            }
            proxy.Mesh.RecalculateBounds();
        }

        public static void Clear()
        {
            foreach (var proxy in _actorMeshes.Values)
            {
                if (proxy.Mesh != null)
                {
                    Object.Destroy(proxy.Mesh);
                }
            }
            _actorMeshes.Clear();
        }
    }
}
