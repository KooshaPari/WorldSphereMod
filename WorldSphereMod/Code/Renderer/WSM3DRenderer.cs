using UnityEngine;
using UnityEngine.Rendering;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Renderer
{
    public sealed class WSM3DRenderer : MonoBehaviour
    {
        private const string CommandBufferName = "WSM3D.Forward+";

        private CommandBuffer? _commandBuffer;
        private Camera? _camera;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            _camera = CameraManager.MainCamera;
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

        private void OnDisable()
        {
            if (_camera != null && _commandBuffer != null)
            {
                _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _commandBuffer);
            }
        }

        public void Execute()
        {
        }
    }
}
