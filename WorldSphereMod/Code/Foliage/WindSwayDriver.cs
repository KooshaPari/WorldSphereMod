using UnityEngine;

namespace WorldSphereMod.Foliage
{
    public sealed class WindSwayDriver : MonoBehaviour
    {
        static readonly int _windTime = Shader.PropertyToID("_WindTime");
        static readonly int _windDir = Shader.PropertyToID("_WindDir");
        static readonly int _windSpeed = Shader.PropertyToID("_WindSpeed");

        Vector2 _dir = new Vector2(1f, 0f);
        float _dirAngle;

        void LateUpdate()
        {
            // Slowly rotate the wind direction over time — gives the foliage a believable
            // ambient drift without requiring per-instance phase data.
            _dirAngle += Time.deltaTime * 0.05f;
            _dir.x = Mathf.Cos(_dirAngle);
            _dir.y = Mathf.Sin(_dirAngle);

            Shader.SetGlobalFloat(_windTime, Time.time);
            Shader.SetGlobalVector(_windDir, new Vector4(_dir.x, _dir.y, 0f, 0f));
            Shader.SetGlobalFloat(_windSpeed, 1.5f);
        }
    }
}
