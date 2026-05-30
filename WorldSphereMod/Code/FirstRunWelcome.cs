using UnityEngine;
using NeoModLoader.General;
using System.Collections;
using WorldSphereMod.UI;

namespace WorldSphereMod
{
    sealed class FirstRunWelcome : MonoBehaviour
    {
        bool _fired;

        void Start()
        {
            if (_fired) return;
            _fired = true;
            StartCoroutine(ShowWelcome());
        }

        IEnumerator ShowWelcome()
        {
            // Wait for the game world to finish loading so the UI layer is ready.
            while (World.world == null || !World.world.isActiveAndEnabled)
            {
                yield return null;
            }
            // Extra frame to let NML finish its post-init layout pass.
            yield return null;
            yield return null;

            if (Core.savedSettings.HasSeenWelcome)
            {
                Destroy(this);
                yield break;
            }

            Core.savedSettings.HasSeenWelcome = true;
            Core.SaveSettings();

            // Open the 3D Phases window so phase toggles are immediately visible.
            try
            {
                if (WindowManager.windows.ContainsKey("3D Phases"))
                {
                    WindowManager.OpenWindow("3D Phases");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WSM3D] FirstRunWelcome: failed to open Phases window: " + ex.Message);
            }

            // Display a brief in-game tooltip as a welcome message.
            try
            {
                string welcomeText = LM.Get("wsm3d_welcome_message");
                if (string.IsNullOrEmpty(welcomeText) || welcomeText == "wsm3d_welcome_message")
                {
                    welcomeText = "WorldSphere 3D is installed! Voxel actors are enabled by default. Open the 3D Phases panel to enable more features.";
                }
                WorldTip.instance.show(welcomeText, false, "top", 8f);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WSM3D] FirstRunWelcome: failed to show welcome tip: " + ex.Message);
            }

            Debug.Log("[WSM3D] First-run welcome shown. VoxelEntities=" + Core.savedSettings.VoxelEntities);
            Destroy(this);
        }
    }
}
