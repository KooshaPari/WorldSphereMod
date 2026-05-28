using UnityEngine;
using UnityEngine.Rendering;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Renderer
{
    /// <summary>
    /// Tier 5 Forward+ hand-roll renderer scaffold. See docs/specs/forward-plus-renderer-spec.md.
    /// </summary>
    public sealed class WSM3DRenderer : MonoBehaviour
    {
        public const string CommandBufferName = "WSM3D.Forward+";

        /// <summary>Screen-space tile size for Forward+ light culling (spec §2).</summary>
        public const int TileSizePx = 16;

        /// <summary>Reference resolution for tile-grid sizing (1920×1080 → 120×68 tiles).</summary>
        public const int ReferenceWidthPx = 1920;
        public const int ReferenceHeightPx = 1080;
        public const int ReferenceTileCountX = 120;
        public const int ReferenceTileCountY = 68;
        public const int ReferenceTileCount = 8160;

        /// <summary>Per-tile light cap and global dynamic-light budget (spec §2, performance table).</summary>
        public const int MaxLightsPerTile = 32;
        public const int MaxDynamicLights = 256;

        static WSM3DRenderer? _instance;
        static bool _executeStubLogged;

        CommandBuffer? _commandBuffer;
        Camera? _camera;
        Camera? _attachedCamera;

        static readonly int DepthRtId = Shader.PropertyToID("_WSM3D_DepthRT");
        static readonly int ColorRtId = Shader.PropertyToID("_WSM3D_ColorRT");
        static readonly int AoRtId = Shader.PropertyToID("_WSM3D_AORT");

        public static int TileCountX(int screenWidthPx) =>
            (screenWidthPx + TileSizePx - 1) / TileSizePx;

        public static int TileCountY(int screenHeightPx) =>
            (screenHeightPx + TileSizePx - 1) / TileSizePx;

        public static void EnsureCreated()
        {
            if (_instance != null)
            {
                return;
            }

            if (!Core.IsWorld3D)
            {
                return;
            }

            if (Core.savedSettings == null || !Core.savedSettings.ForwardPlusRenderer)
            {
                return;
            }

            if (Mod.Object == null)
            {
                return;
            }

            Mod.Object.AddComponent<WSM3DRenderer>();
        }

        void Awake()
        {
            if (_instance != null)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable()
        {
            RebindCamera(CameraManager.MainCamera ?? Camera.main);
        }

        void OnDisable()
        {
            DetachCommandBuffer();

            _commandBuffer?.Release();
            _commandBuffer = null;
            _camera = null;
        }

        void OnDestroy()
        {
            DetachCommandBuffer();
            _commandBuffer?.Release();
            _commandBuffer = null;

            if (_instance == this)
            {
                _instance = null;
            }
        }

        void RebindCamera(Camera? camera)
        {
            _camera = camera;

            if (_commandBuffer == null)
            {
                if (_attachedCamera != null)
                {
                    DetachCommandBuffer();
                }

                return;
            }

            if (_attachedCamera != null && _attachedCamera != camera)
            {
                _attachedCamera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _commandBuffer);
                _attachedCamera = null;
            }

            if (camera == null)
            {
                return;
            }

            camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _commandBuffer);
            camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, _commandBuffer);
            _attachedCamera = camera;
        }

        void DetachCommandBuffer()
        {
            if (_commandBuffer == null || _attachedCamera == null)
            {
                _attachedCamera = null;
                return;
            }

            _attachedCamera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _commandBuffer);
            _attachedCamera = null;
        }

        void EnsureCameraBinding()
        {
            var currentCamera = CameraManager.MainCamera ?? Camera.main;
            if (currentCamera != _camera || currentCamera != _attachedCamera)
            {
                RebindCamera(currentCamera);
            }
        }

        void ConfigureCommandBuffer()
        {
            _commandBuffer ??= new CommandBuffer
            {
                name = CommandBufferName
            };

            RebindCamera(CameraManager.MainCamera ?? Camera.main);
        }

        void LateUpdate()
        {
            Execute();
        }

        public void Execute()
        {
            if (Core.savedSettings == null || !Core.savedSettings.ForwardPlusRenderer)
            {
                return;
            }

            ConfigureCommandBuffer();
            EnsureCameraBinding();

            if (_commandBuffer == null || _camera == null)
            {
                return;
            }

            if (!_executeStubLogged)
            {
                Debug.Log("[WSM3D][Forward+] Execute scaffold active (depth/color passes not wired yet).");
                _executeStubLogged = true;
            }

            _commandBuffer.Clear();

            try
            {
                AllocateTargets();
                DepthPrepass();
                // TileLightCull / ColorPass / PostFXChain / Composite — deferred.
            }
            finally
            {
                ReleaseTargets();
            }
        }

        void AllocateTargets()
        {
            if (_commandBuffer == null || _camera == null)
            {
                return;
            }

            int w = Mathf.Max(1, _camera.pixelWidth);
            int h = Mathf.Max(1, _camera.pixelHeight);

            // Stub: reserve full-screen RTs for depth prepass, color pass, and SSAO (spec §1, §3, §5).
            _commandBuffer.GetTemporaryRT(DepthRtId, w, h, 0, FilterMode.Point, RenderTextureFormat.RFloat);
            _commandBuffer.GetTemporaryRT(ColorRtId, w, h, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
            _commandBuffer.GetTemporaryRT(AoRtId, w, h, 0, FilterMode.Bilinear, RenderTextureFormat.R8);
        }

        void ReleaseTargets()
        {
            if (_commandBuffer == null)
            {
                return;
            }

            _commandBuffer.ReleaseTemporaryRT(AoRtId);
            _commandBuffer.ReleaseTemporaryRT(ColorRtId);
            _commandBuffer.ReleaseTemporaryRT(DepthRtId);
        }

        void DepthPrepass()
        {
            if (_commandBuffer == null || _camera == null)
            {
                return;
            }

            // Stub: depth-only pass into DepthRtId (spec §3). Mesh submit + early-Z wiring deferred.
        }
    }
}
