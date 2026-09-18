using NCAIClicker.Interfaces;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// Game 씬에 결과창이 배치되기 전까지 런타임에 임시로 띄우는 로더.
    /// 씬에 ResultUIController 가 있으면 즉시 양보한다.
    ///
    /// 스태미나는 더 이상 여기서 만들지 않는다 — 정식 GameHud 가 Game 씬에 들어왔다 (#172).
    /// 결과창이 씬에 배치되면 이 파일 전체를 지운다.
    /// </summary>
    public static class InGameUIFallbackLoader
    {
        private const string GameSceneName = "Game";
        private const string ResultPrefabResourcePath = "UI/ResultUI";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            CheckAndSpawnUI(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CheckAndSpawnUI(scene);
        }

        private static void CheckAndSpawnUI(Scene scene)
        {
            if (scene.name != GameSceneName)
            {
                return;
            }

            if (Object.FindFirstObjectByType<ResultUIController>() != null)
            {
                return;
            }

            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("Runtime_Fallback_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
            }

            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                new GameObject("Runtime_Fallback_EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            }

            {
                var prefab = Resources.Load<GameObject>(ResultPrefabResourcePath);
                if (prefab != null)
                {
                    var instance = Object.Instantiate(prefab, canvas.transform, false);
                    instance.name = "ResultUI (Runtime Fallback)";

                    var controller = instance.GetComponent<ResultUIController>();
                    var managersGo = GameObject.Find("Managers");
                    if (controller != null && managersGo != null)
                    {
                        controller.SetServices(
                            managersGo.GetComponentInChildren<IEconomyService>(true),
                            managersGo.GetComponentInChildren<IBillService>(true),
                            managersGo.GetComponentInChildren<IStageService>(true));
                    }
                }
            }
        }
    }
}
