using UnityEngine;
using WorldSphereMod.NewCamera;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Weather
{
    public sealed class WeatherDriver : MonoBehaviour
    {
        const float kRainHeight = 14f;
        const float kRainSpeed = 24f;
        const float kSnowSpeed = 6f;
        const float kRainFallRate = 160f;
        const float kSnowFallRate = 30f;
        const float kEmitRadius = 10f;
        const float kRainStartSize = 0.05f;
        const float kSnowStartSize = 0.09f;
        const float kRainLife = 2.1f;
        const float kSnowLife = 6f;

        const float kLightningRangeMin = 5f;
        const float kLightningRangeMax = 15f;
        const float kLightningFlashIntensity = 4.5f;
        const float kLightningFlashDuration = 0.12f;
        const float kLightningBoltHeight = 9f;

        static WeatherDriver Instance;

        ParticleSystem _rainSystem;
        ParticleSystem _snowSystem;
        Mesh _voxelMesh;
        Material _particleMaterial;
        Material _boltMaterial;
        float _nextLightningAt;
        int _lastRainFrame = -1;

        public static void EnsureCreated()
        {
            if (_isTearingDown)
            {
                return;
            }

            if (!Core.IsWorld3D) return;
            if (Instance != null) return;
            if (!Core.savedSettings.WeatherRain && !Core.savedSettings.WeatherSnow && !Core.savedSettings.WeatherLightning) return;
            if (Mod.Object == null) return;
            Mod.Object.AddComponent<WeatherDriver>();
        }

        public static void Teardown()
        {
            if (Instance == null) return;
            Destroy(Instance);
            Instance = null;
        }

        static bool _isTearingDown;

        void Awake()
        {
            Instance = this;
            InitializeResources();
            _nextLightningAt = Time.time + Random.Range(kLightningRangeMin, kLightningRangeMax);
        }

        void OnDestroy()
        {
            _isTearingDown = true;
            if (_rainSystem != null) Destroy(_rainSystem.gameObject);
            if (_snowSystem != null) Destroy(_snowSystem.gameObject);
            if (_particleMaterial != null) Destroy(_particleMaterial);
            if (_boltMaterial != null) Destroy(_boltMaterial);
            _isTearingDown = false;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void Update()
        {
            if (!Core.IsWorld3D)
            {
                return;
            }

            var cam = CameraManager.MainCamera;
            if (cam == null)
            {
                return;
            }

            transform.position = cam.transform.position + Vector3.up * kRainHeight;

            if (Core.savedSettings.WeatherRain && _rainSystem != null)
            {
                SpawnRainFrame();
            }

            if (Core.savedSettings.WeatherSnow && _snowSystem != null)
            {
                SpawnSnowFrame();
            }

            if (Core.savedSettings.WeatherLightning)
            {
                SpawnLightningIfNeeded();
            }
        }

        void InitializeResources()
        {
            _voxelMesh = BuildVoxelMesh();

            var resolvedMaterial = VoxelRender.GetResolvedMaterial();
            if (resolvedMaterial == null)
            {
                // 60f1 strips Unlit/* — ResolveShader returns Standard, never null.
                Shader fallback = Shader.Find("Sprites/Default")
                    ?? Core.Sphere.ResolveShader("");

                resolvedMaterial = new Material(fallback);
                resolvedMaterial.color = Color.white;
            }

            _particleMaterial = new Material(resolvedMaterial);
            _boltMaterial = new Material(resolvedMaterial);

            _boltMaterial.color = new Color(0.9f, 0.96f, 1f, 0.9f);

            if (Core.savedSettings.WeatherRain)
            {
                _rainSystem = CreateWeatherSystem("WSM3D.Rain", kRainStartSize, kRainLife, new Color(0.35f, 0.55f, 0.95f, 1f));
            }

            if (Core.savedSettings.WeatherSnow)
            {
                _snowSystem = CreateWeatherSystem("WSM3D.Snow", kSnowStartSize, kSnowLife, Color.white);
            }
        }

        void SpawnRainFrame()
        {
            float target = Time.deltaTime * kRainFallRate;
            int count = Mathf.CeilToInt(Mathf.Max(1f, target));
            if (_lastRainFrame == Time.frameCount)
            {
                count = Mathf.Max(count, 1);
            }
            _lastRainFrame = Time.frameCount;

            for (int i = 0; i < count; i++)
            {
                Vector3 localOffset = new Vector3(
                    Random.Range(-kEmitRadius, kEmitRadius),
                    0f,
                    Random.Range(-kEmitRadius, kEmitRadius));

                var emit = BuildEmitParams(localOffset, kRainSpeed, kRainLife, new Color(0.3f, 0.56f, 1f, 1f), kRainStartSize);
                _rainSystem.Emit(emit, 1);
            }
        }

        void SpawnSnowFrame()
        {
            float target = Time.deltaTime * kSnowFallRate;
            int count = Mathf.CeilToInt(Mathf.Max(0.5f, target));

            for (int i = 0; i < count; i++)
            {
                Vector3 localOffset = new Vector3(
                    Random.Range(-kEmitRadius * 0.8f, kEmitRadius * 0.8f),
                    0f,
                    Random.Range(-kEmitRadius * 0.8f, kEmitRadius * 0.8f));

                var emit = BuildEmitParams(localOffset, kSnowSpeed, kSnowLife, Color.white, kSnowStartSize * 1.3f);
                _snowSystem.Emit(emit, 1);
            }
        }

        void SpawnLightningIfNeeded()
        {
            if (Time.time < _nextLightningAt)
            {
                return;
            }

            Vector3 tileCenter = RandomTileWorldPosition();
            if (tileCenter == Vector3.zero)
            {
                _nextLightningAt = Time.time + Random.Range(kLightningRangeMin, kLightningRangeMax);
                return;
            }

            SpawnLightningFlash(tileCenter);
            SpawnLightningBolt(tileCenter);
            _nextLightningAt = Time.time + Random.Range(kLightningRangeMin, kLightningRangeMax);
        }

        void SpawnLightningFlash(Vector3 tileCenter)
        {
            var lightning = new GameObject("WSM3D.LightningFlash");
            lightning.transform.SetParent(transform, false);
            lightning.transform.position = tileCenter + Vector3.up * 0.1f;
            lightning.transform.rotation = Quaternion.Euler(
                Random.Range(45f, 90f),
                Random.Range(0f, 360f),
                0f);

            var flash = lightning.AddComponent<Light>();
            flash.type = LightType.Directional;
            flash.color = new Color(0.78f, 0.87f, 1f, 1f);
            flash.intensity = kLightningFlashIntensity;
            flash.shadows = LightShadows.Hard;

            Destroy(lightning, kLightningFlashDuration);
        }

        void SpawnLightningBolt(Vector3 tileCenter)
        {
            var bolt = new GameObject("WSM3D.LightningBolt");
            bolt.transform.SetParent(transform, false);
            bolt.transform.position = tileCenter + Vector3.up * (kLightningBoltHeight * 0.5f);
            bolt.transform.localScale = new Vector3(0.2f, kLightningBoltHeight, 0.2f);

            var meshFilter = bolt.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = _voxelMesh;

            var meshRenderer = bolt.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _boltMaterial;

            Destroy(bolt, 0.25f);
        }

        ParticleSystem CreateWeatherSystem(string name, float startSize, float life, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.startLifetime = life;
            main.startSize = startSize;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4096;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = _voxelMesh;
            renderer.material = new Material(_particleMaterial)
            {
                color = color,
            };

            ps.Play();
            return ps;
        }

        static ParticleSystem.EmitParams BuildEmitParams(Vector3 localOffset, float speed, float life, Color color, float size)
        {
            return new ParticleSystem.EmitParams
            {
                position = localOffset,
                velocity = Vector3.down * speed,
                startLifetime = life,
                startColor = color,
                startSize = size,
            };
        }

        static Vector3 RandomTileWorldPosition()
        {
            if (MapBox.width <= 0 || MapBox.height <= 0 || !Core.IsWorld3D)
            {
                return Vector3.zero;
            }

            float x = Random.Range(0f, MapBox.width);
            float y = Random.Range(0f, MapBox.height);
            return Core.Sphere.SpherePos(x, y, 0f) + Vector3.up * 0.2f;
        }

        static Mesh BuildVoxelMesh()
        {
            const float h = 0.5f;
            var mesh = new Mesh { name = "WSM3D.WeatherVoxel" };

            Vector3[] vertices =
            {
                new Vector3(-h, -h, -h), new Vector3( h, -h, -h),
                new Vector3( h,  h, -h), new Vector3(-h,  h, -h),
                new Vector3(-h, -h,  h), new Vector3( h, -h,  h),
                new Vector3( h,  h,  h), new Vector3(-h,  h,  h),
            };

            int[] triangles =
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                3, 7, 6, 3, 6, 2,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
