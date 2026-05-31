using CompoundSpheres;
using CompoundSpheres.Gpu;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using WorldSphereMod.NewCamera;
using Debug = UnityEngine.Debug;
using static UnityEngine.UI.CanvasScaler;
using static WorldSphereMod.Constants;
namespace WorldSphereMod
{
    public delegate float Gate(float a, float b, float c);
    public struct PhaseGate
    {
        public Gate GetDist;
        public Gate GetChange;
    }
    public static class CompoundSphereScripts
    {
        public static readonly PhaseGate DefaultGate = new()
        {
            GetDist = (a, b, c) => a - b,
            GetChange = (a, b, c) => a + b
        };
        public static readonly PhaseGate WrappedGate = new()
        {
            GetDist = (a, b, c) => Tools.MathStuff.WrappedDist(a, b, c),
            GetChange = (a, b, c) => Tools.MathStuff.WrappedChange(a, b, c)
        };
        public static int SphereTileTexture(SphereTile Tile)
        {
            return Core.Sphere.WorldTileTexture(Tile.SphereToWorld());
        }
        public static float SphereTileHeight(SphereTile Tile)
        {
            WorldTile tile = Tile.SphereToWorld();
            float Height = Tools.TrueHeight(tile.GetHeight(), tile.main_type.render_z);
            if (Core.Sphere.PerlinNoise)
            {
                Height *= Tools.PerlinNose(Tile.X, Tile.Y, Tile.Manager.Rows, Tile.Manager.Cols, 20);
            }
            return Height;
        }
        public static Vector3 SphereTileScaleFlat(SphereTile Tile)
        {
            float Height = SphereTileHeight(Tile);
            return new Vector3(1, 1, Height * Core.Sphere.HeightMult);
        }
        public static Vector3 SphereTileScaleCube(SphereTile Tile)
        {
            float Height = SphereTileHeight(Tile);
            return new Vector3(1, 1, Height * Core.Sphere.HeightMult);
        }
        public static Vector3 SphereTileScaleCylindrical(SphereTile Tile)
        {
            float Height = SphereTileHeight(Tile);
            return new Vector3(1, 1 + (Height * YConst), Height * Core.Sphere.HeightMult);
        }
        public static Vector3 SphereTileAddedColor(SphereTile Tile)
        {
            Color32 color = Core.Sphere.GetAddedColor(Tile.Index());
            return new Vector3(color.r, color.g, color.b) / 255;
        }
        public static Quaternion CylindricalRotation(SphereTile tile)
        {
            return CylindricalRotation(tile.Position);
        }
        public static Quaternion CylindricalRotation(Vector2 pos)
        {
            return Quaternion.AngleAxis(Tools.MathStuff.Angle(pos.y, pos.x), Vector3.forward) * ConstRot;
        }
        public static Quaternion FlatRotation(SphereTile _)
        {
            return ConstRot * ToUpright;
        }
        public static Quaternion CubeRotation(Vector2 _)
        {
            return ConstRot * ToUpright;
        }
        public static Quaternion FlatRotation(Vector2 _)
        {
            return ConstRot * ToUpright;
        }
        public static Color32 SphereTileColor(SphereTile SphereTile)
        {
            return Core.Sphere.GetColor(SphereTile.Index());
        }

        // -----------------------------------------------------------------
        // GPU adapters (#199 Phase 2/3): the GPU manager's delegate surface is
        // typed against GpuSphereTile / GpuSphereManager, not the CPU SphereTile.
        // These mirror the CPU bodies above EXACTLY by re-deriving the same
        // WorldBox lookups from the tile's grid coordinates (X,Y) — keeping a
        // single source of truth for per-tile color/texture/scale/overlay.
        // -----------------------------------------------------------------
        private static WorldTile GpuWorldTile(GpuSphereTile t) => World.world.GetTileSimple(t.X, t.Y);

        public static int GpuTileTexture(GpuSphereTile t)
        {
            WorldTile wt = GpuWorldTile(t);
            return wt == null ? 0 : Core.Sphere.WorldTileTexture(wt);
        }
        public static float GpuTileHeight(GpuSphereTile t)
        {
            WorldTile tile = GpuWorldTile(t);
            if (tile == null) return 0f;
            float Height = Tools.TrueHeight(tile.GetHeight(), tile.main_type.render_z);
            if (Core.Sphere.PerlinNoise)
                Height *= Tools.PerlinNose(t.X, t.Y, t.Manager.Rows, t.Manager.Cols, 20);
            return Height;
        }
        public static Vector3 GpuTileScaleFlat(GpuSphereTile t)
        {
            float Height = GpuTileHeight(t);
            return new Vector3(1, 1, Height * Core.Sphere.HeightMult);
        }
        public static Vector3 GpuTileScaleCube(GpuSphereTile t)
        {
            float Height = GpuTileHeight(t);
            return new Vector3(1, 1, Height * Core.Sphere.HeightMult);
        }
        public static Vector3 GpuTileScaleCylindrical(GpuSphereTile t)
        {
            float Height = GpuTileHeight(t);
            return new Vector3(1, 1 + (Height * YConst), Height * Core.Sphere.HeightMult);
        }
        public static Vector3 GpuTileScaleForCurrentShape(GpuSphereTile t)
        {
            switch (t.Manager.Shape)
            {
                case TileShape.Flat: return GpuTileScaleFlat(t);
                case TileShape.Cube: return GpuTileScaleCube(t);
                default: return GpuTileScaleCylindrical(t);
            }
        }
        public static Color32 GpuTileColor(GpuSphereTile t)
        {
            WorldTile wt = GpuWorldTile(t);
            return wt == null ? new Color32(128, 128, 128, 255) : Core.Sphere.GetColor(wt.data.tile_id);
        }
        // GPU custom-buffer samplers are index-based (GetCustomData<T>(int Index)
        // where Index is the buffer slot = X*Cols+Y), unlike the CPU
        // CustomBufferData which receives the tile. The CPU SphereTileAddedColor
        // keys GetAddedColor on Tile.Index() (== the tile's tile_id), so to mirror
        // EXACTLY we resolve the same tile_id from the slot's grid coordinates.
        public static Vector3 GpuTileAddedColor(int slot)
        {
            int cols = Core.Sphere.Height; // == Manager.Cols (shared grid dimension)
            int x = cols > 0 ? slot / cols : 0;
            int y = cols > 0 ? slot % cols : 0;
            WorldTile wt = World.world.GetTileSimple(x, y);
            if (wt == null) return Vector3.zero;
            Color32 color = Core.Sphere.GetAddedColor(wt.data.tile_id);
            return new Vector3(color.r, color.g, color.b) / 255;
        }
        public static CompoundSpheres.Gpu.DisplayMode GpuDisplayMode()
        {
            return World.world.quality_changer.isLowRes()
                ? CompoundSpheres.Gpu.DisplayMode.ColorOnly
                : CompoundSpheres.Gpu.DisplayMode.TextureOnly;
        }
        // Adapts the active CPU shape's RenderRange (out int Min,Max applied to
        // rows) into the GPU camera-range delegate (out Range Rows, out Range Cols).
        // Cols spans the full grid width; the GPU culler narrows columns per row.
        public static void GpuCameraRange(GpuSphereManager mgr, out CompoundSpheres.Gpu.Range Rows, out CompoundSpheres.Gpu.Range Cols)
        {
            Core.Sphere.GetCamerRange(out int Min, out int Max);
            Rows = new CompoundSpheres.Gpu.Range(Min, Max);
            Cols = new CompoundSpheres.Gpu.Range(0, mgr.Cols);
        }
        public static Vector3 CartesianToFlat(SphereManager manager, float X, float Y, float Height = 0)
        {
            return new Vector3(X, Height, Y + ZDisplacement);
        }
        public static Vector3 FlatToCartesian(SphereManager manager, float x, float y, float z)
        {
            return new Vector3(x, z - ZDisplacement, y);
        }
        public static Vector2 FlatToCartesianFast(SphereManager manager, float x, float y, float z)
        {
            return new Vector2(x, z - ZDisplacement);
        }
        public static Vector2 GetMovementVectorSpherical(float Speed, bool Vertical)
        {
            Vector3 vector;
            float yRotation = RotateCamera.Rotation.y;

            float rad = yRotation * Mathf.Deg2Rad;

            bool Inversed = Core.savedSettings.InvertedCameraMovement;
            if (Core.savedSettings.CameraRotatesWithWorld)
            {
                Inversed = !Inversed;
            }

            if (Inversed ? !Vertical : Vertical)
            {
                vector = new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad)).normalized;
                if (Core.savedSettings.CameraRotatesWithWorld)
                {
                    vector.z *= RotateCamera.InvertMult;
                }
            }
            else
            {
                vector = new Vector3(Mathf.Cos(rad), 0, -Mathf.Sin(rad)).normalized;
                vector.x *= RotateCamera.InvertMult;
                if (Core.savedSettings.CameraRotatesWithWorld)
                {
                    vector *= -1;
                }
                else
                {
                    vector.z *= RotateCamera.InvertMult;
                }
            }
            return new Vector2(vector.x * Speed, vector.z * Speed * RotateCamera.InvertMult);
        }
        public static Vector2 GetMovementVectorFlat(float Speed, bool Vertical)
        {
            Vector3 vector = GetMovementVectorSpherical(Speed, Vertical);
            if (!Core.savedSettings.CameraRotatesWithWorld)
            {
                vector.x *= RotateCamera.InvertMult;
            }
            return vector;
        }
        public static Vector2 GetMovementVectorCube(float Speed, bool Vertical)
        {
            Vector3 vector = GetMovementVectorSpherical(Speed, Vertical);
            if (!Core.savedSettings.CameraRotatesWithWorld)
            {
                vector.x *= RotateCamera.InvertMult;
            }
            return vector;
        }
        public static Vector3 CartesianToCylindrical(SphereManager manager, float X, float Y, float Height = 0)
        {
            Vector2 xy = Tools.MathStuff.PointOnCircle(-X, manager.Radius, Height);
            float z = Y + ZDisplacement;
            return new Vector3(xy.x, xy.y, z);
        }
        public static Vector3 CylindricalToCartesian(SphereManager manager, float x, float y, float z)
        {
            float X = manager.Clamp(Tools.MathStuff.Flip(Mathf.Atan2(y, x) / (2f * Mathf.PI) * manager.Rows, manager.Rows), 0);
            float Y = z - ZDisplacement;
            float Height = Mathf.Sqrt((x * x) + (y * y)) - manager.Radius;
            return new Vector3(X, Y, Height);
        }
        //doesnt calculate height
        public static Vector2 CylindricalToCartesianFast(SphereManager manager, float x, float y, float z)
        {
            float X = manager.Clamp(Tools.MathStuff.Flip(Mathf.Atan2(y, x) / (2f * Mathf.PI) * manager.Rows, manager.Rows), 0);
            float Y = z - ZDisplacement;
            return new Vector2Int((int)X, (int)Y);
        }
        public static void CylindricalInitiation(SphereManager Manager)
        {
            var sw = Stopwatch.StartNew();
            GameObject Cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Cylinder.transform.SetPositionAndRotation(new Vector3(0, 0, (Manager.Cols / 2) + ZDisplacement), Quaternion.Euler(-90, 0, 0));
            Cylinder.transform.localScale = new Vector3(Manager.Diameter, Manager.Cols / 2, Manager.Diameter);
            Object.Destroy(Cylinder.GetComponent<CapsuleCollider>());
            Object.Destroy(Cylinder.GetComponent<MeshRenderer>());
            Cylinder.AddComponent<MeshCollider>();
            Cylinder.transform.parent = Manager.transform;
            Debug.Log($"[WSM3D][PERF] CompoundSphereScripts.CylindricalInitiation={sw.Elapsed.TotalMilliseconds:F3}ms");
        }
        public static void FlatInitiation(SphereManager Manager)
        {
            var sw = Stopwatch.StartNew();
            GameObject Quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Quad.transform.SetPositionAndRotation(new Vector3((Manager.Rows / 2) - 0.5f, 0, (Manager.Cols / 2) - 0.5f + ZDisplacement), Quaternion.Euler(90, 0, 0));
            Quad.transform.localScale = new Vector3(Manager.Rows, Manager.Cols, 1);
            Object.Destroy(Quad.GetComponent<MeshRenderer>());
            Quad.GetComponent<MeshCollider>().convex = true; //why the fuck?
            Quad.transform.parent = Manager.transform;
            Debug.Log($"[WSM3D][PERF] CompoundSphereScripts.FlatInitiation={sw.Elapsed.TotalMilliseconds:F3}ms");
        }
        public static CompoundSpheres.DisplayMode getdisplaymode(SphereManager _)
        {
            return World.world.quality_changer.isLowRes()
                ? CompoundSpheres.DisplayMode.ColorOnly
                : CompoundSpheres.DisplayMode.TextureOnly;
        }
        static float RangeMult => Core.savedSettings.RowRange;
        static float BaseRange => 4 - (1 / RangeMult);
        public static void RenderRange(SphereManager SphereManager, out int Min, out int Max)
        {
            float Devide = BaseRange + (CameraManager.Manager.orthographic_size_max / CameraManager.Height / RangeMult);
            float Rows = SphereManager.Rows;
            Min = (int)-(Rows / Devide);
            Max = (int)(Rows / Devide);
        }
        public static void RenderRangeFlat(SphereManager SphereManager, out int Min, out int Max)
        {
            float Devide = (BaseRange + (CameraManager.Manager.orthographic_size_max / CameraManager.Height / RangeMult)) / 4;

            float Rows = SphereManager.Rows;
            Min = Mathf.Max((int)-(Rows / Devide), -(int)CameraManager.Position.x);
            Max = Mathf.Min((int)(Rows / Devide), Core.Sphere.Width - (int)CameraManager.Position.x);
        }
        public static void RenderRangeCube(SphereManager SphereManager, out int Min, out int Max)
        {
            float Devide = (BaseRange + (CameraManager.Manager.orthographic_size_max / CameraManager.Height / RangeMult)) / 4;

            float Rows = SphereManager.Rows;
            Min = Mathf.Max((int)-(Rows / Devide), -(int)CameraManager.Position.x);
            Max = Mathf.Min((int)(Rows / Devide), Core.Sphere.Width - (int)CameraManager.Position.x);
        }
        public static Vector3 CubeToCartesian(SphereManager manager, float x, float y, float z)
        {
            return Tools.Cube.ToWorld(new Vector2(x, y), z);
        }
        public static Vector3 CartesianToCube(SphereManager manager, float x, float y, float z)
        {
            return Tools.Cube.To2D(new Vector3(x, y, z));
        }
        public static Vector2 CartesianToCubeFast(SphereManager manager, float x, float y, float z)
        {
            return Tools.Cube.To2D(new Vector3(x, y, z));
        }
        public static Quaternion CubeRotation(SphereTile tile)
        {
            return Tools.Cube.GetRegion(new Vector2(tile.X, tile.Y)).Direction;
        }
        public static void CubeInitiation(SphereManager Manager)
        {
            GameObject Cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Cube.transform.position = new Vector3(0, 0, ZDisplacement);
            Cube.transform.localScale = new Vector3(Tools.Cube.Size, Tools.Cube.Size, Tools.Cube.Size);
            Object.Destroy(Cube.GetComponent<MeshRenderer>());
        }

        public static void GpuCylindricalInitiation(GpuSphereManager manager)
        {
            var sw = Stopwatch.StartNew();
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.transform.SetPositionAndRotation(new Vector3(0, 0, (manager.Cols / 2) + ZDisplacement), Quaternion.Euler(-90, 0, 0));
            cylinder.transform.localScale = new Vector3(manager.Diameter, manager.Cols / 2, manager.Diameter);
            Object.Destroy(cylinder.GetComponent<CapsuleCollider>());
            Object.Destroy(cylinder.GetComponent<MeshRenderer>());
            cylinder.AddComponent<MeshCollider>();
            cylinder.transform.parent = manager.transform;
            Debug.Log($"[WSM3D][PERF] CompoundSphereScripts.GpuCylindricalInitiation={sw.Elapsed.TotalMilliseconds:F3}ms");
        }
        public static void GpuFlatInitiation(GpuSphereManager manager)
        {
            var sw = Stopwatch.StartNew();
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetPositionAndRotation(new Vector3((manager.Rows / 2) - 0.5f, 0, (manager.Cols / 2) - 0.5f + ZDisplacement), Quaternion.Euler(90, 0, 0));
            quad.transform.localScale = new Vector3(manager.Rows, manager.Cols, 1);
            Object.Destroy(quad.GetComponent<MeshRenderer>());
            quad.GetComponent<MeshCollider>().convex = true;
            quad.transform.parent = manager.transform;
            Debug.Log($"[WSM3D][PERF] CompoundSphereScripts.GpuFlatInitiation={sw.Elapsed.TotalMilliseconds:F3}ms");
        }
        public static void GpuCubeInitiation(GpuSphereManager manager)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(0, 0, ZDisplacement);
            cube.transform.localScale = new Vector3(Tools.Cube.Size, Tools.Cube.Size, Tools.Cube.Size);
            Object.Destroy(cube.GetComponent<MeshRenderer>());
            cube.transform.parent = manager.transform;
        }
        public static GpuCubeRegion[] BuildGpuCubeRegions()
        {
            var src = Tools.Cube.AllRegions;
            var dst = new GpuCubeRegion[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                Tools.Cube.Region r = src[i];
                dst[i] = new GpuCubeRegion
                {
                    RectPos = r.Rect.position,
                    RectSize = r.Rect.size,
                    Normal = r.Normal,
                    Right = r.Right,
                    Up = r.Up,
                    Start = r.Start,
                };
            }
            return dst;
        }
    }
}