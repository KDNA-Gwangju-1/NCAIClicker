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
        private const string BillPrefabResourcePath = "UI/BillPanel";
        private const string PausePrefabResourcePath = "UI/PausePanel";

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

            var managersGo = GameObject.Find("Managers");
            var billService = managersGo != null ? managersGo.GetComponentInChildren<IBillService>(true) : null;

            // 고지서 패널을 먼저 띄운다. 정산창이 납부 버튼에서 이 패널을 열기 때문이다.
            BillPanelController billPanel = null;
            var billPrefab = Resources.Load<GameObject>(BillPrefabResourcePath);
            if (billPrefab != null)
            {
                var billInstance = Object.Instantiate(billPrefab, canvas.transform, false);
                billInstance.name = "BillPanel (Runtime Fallback)";
                billPanel = billInstance.GetComponent<BillPanelController>();
                if (billPanel != null)
                {
                    billPanel.SetServices(billService);
                }
            }

            var prefab = Resources.Load<GameObject>(ResultPrefabResourcePath);
            if (prefab != null)
            {
                var instance = Object.Instantiate(prefab, canvas.transform, false);
                instance.name = "ResultUI (Runtime Fallback)";

                var controller = instance.GetComponent<ResultUIController>();
                if (controller != null)
                {
                    if (managersGo != null)
                    {
                        controller.SetServices(
                            managersGo.GetComponentInChildren<IEconomyService>(true),
                            billService,
                            managersGo.GetComponentInChildren<IStageService>(true));
                    }
                    controller.SetBillPanel(billPanel);
                }
            }

            if (Object.FindFirstObjectByType<PausePanelController>() == null)
            {
                var pausePrefab = Resources.Load<GameObject>(PausePrefabResourcePath);
                if (pausePrefab != null)
                {
                    var pauseInstance = Object.Instantiate(pausePrefab);
                    pauseInstance.name = "PausePanel (Runtime Fallback)";
                }
            }
        }
    }
}
