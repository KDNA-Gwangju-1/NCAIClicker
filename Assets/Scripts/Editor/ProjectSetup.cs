using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>초기 세팅 승인 범위의 PC 창과 도메인 리로드 설정만 적용한다.</summary>
    public static class ProjectSetup
    {
        [MenuItem("NCAI/PC 초기 설정 적용", false, MenuPriority.ProjectSetup)]
        public static void ApplyDefaults()
        {
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.allowFullscreenSwitch = false;
            EditorSettings.enterPlayModeOptionsEnabled = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] PC 1920x1080 window; domain and scene reload enabled.");
        }
    }
}
