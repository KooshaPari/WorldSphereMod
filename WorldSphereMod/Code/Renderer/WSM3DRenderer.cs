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

        static WSM3DRenderer? _instance;
        static bool _executeStubLogged;

        CommandBuffer? _commandBuffer;
        Camera? _camera;

        static readonly int DepthRtId = Shader.PropertyToID("_WSM3D_DepthRT");
        static readonly int ColorRtId = Shader.PropertyToID("_WSM3D_ColorRT");
        static readonly int AoRtId = Shader.PropertyToID("_WSM3D_AORT");

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

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        void OnEnable()
        {
            _camera = CameraManager.MainCamera ?? Camera.main;
            if (_camera == null)
            {
                return;
            }

            _commandBuffer ??= new CommandBuffer
            {
                name = CommandBufferName
            };

            _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _commandBuffer);
            _camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, _commandBuffer);
        }

        void OnDisable()
        {
            if (_camera != null && _commandBuffer != null)
            {
                _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _commandBuffer);
            }

            _commandBuffer?.Release();
            _commandBuffer = null;
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
            // Tier 2 scaffold: AllocateTargets / DepthPrepass / TileLightCull / ColorPass / PostFXChain / Composite — deferred.
        }
    }
}
