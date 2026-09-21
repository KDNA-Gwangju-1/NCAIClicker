using System;
using System.Reflection;
using NCAIClicker.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 일시정지 패널 UI 및 동작 검증 (이슈 #192, 작업 6.13).
    /// SettingsPanel (#196) 조립 및 상호작용 흐름을 함께 검증합니다.
    /// </summary>
    public static class PausePanelUiChecks
    {
        private const string PrefabPath = "Assets/Prefabs/Resources/UI/PausePanel.prefab";

        public static void RunBatch()
        {
            var checkCount = 0;
            checkCount += RunPrefabWiringChecks();
            checkCount += RunPauseTimeScaleChecks();
            checkCount += RunConfirmDialogFlowChecks();
            checkCount += RunSettingsPanelFlowChecks();

            Debug.Log("[PausePanelUiChecks] PASS " + checkCount + " checks.");
        }

        private static int RunPrefabWiringChecks()
        {
            var checkCount = 0;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            AssertCondition(prefab != null, "PausePanel 프리팹을 찾을 수 없습니다: " + PrefabPath);
            checkCount++;

            var controller = prefab.GetComponent<PausePanelController>();
            AssertCondition(controller != null, "PausePanel 에 PausePanelController 가 없습니다.");
            checkCount++;

            AssertCondition(GetPrivate<GameObject>(controller, "_panelRoot") != null, "_panelRoot 가 비어 있습니다.");
            AssertCondition(GetPrivate<GameObject>(controller, "_mainPanel") != null, "_mainPanel 이 비어 있습니다.");
            AssertCondition(GetPrivate<TextMeshProUGUI>(controller, "_titleText") != null, "_titleText 가 비어 있습니다.");
            AssertCondition(GetPrivate<TextMeshProUGUI>(controller, "_subtitleText") != null, "_subtitleText 가 비어 있습니다.");

            AssertCondition(GetPrivate<Button>(controller, "_resumeButton") != null, "_resumeButton 이 비어 있습니다.");
            AssertCondition(GetPrivate<Button>(controller, "_settingsButton") != null, "_settingsButton 이 비어 있습니다.");
            AssertCondition(GetPrivate<Button>(controller, "_mainMenuButton") != null, "_mainMenuButton 이 비어 있습니다.");
            AssertCondition(GetPrivate<Button>(controller, "_quitButton") != null, "_quitButton 이 비어 있습니다.");

            AssertCondition(GetPrivate<SettingsPanelController>(controller, "_settingsPanel") != null, "_settingsPanel 조립이 비어 있습니다 (#196).");
            checkCount++;

            AssertCondition(GetPrivate<GameObject>(controller, "_confirmDialogRoot") != null, "_confirmDialogRoot 가 비어 있습니다.");
            AssertCondition(GetPrivate<TextMeshProUGUI>(controller, "_confirmTitleText") != null, "_confirmTitleText 가 비어 있습니다.");
            AssertCondition(GetPrivate<TextMeshProUGUI>(controller, "_confirmMessageText") != null, "_confirmMessageText 가 비어 있습니다.");
            AssertCondition(GetPrivate<Button>(controller, "_confirmOkButton") != null, "_confirmOkButton 이 비어 있습니다.");
            AssertCondition(GetPrivate<Button>(controller, "_confirmCancelButton") != null, "_confirmCancelButton 이 비어 있습니다.");
            checkCount++;

            var fallback = GetPrivate<GameObject>(controller, "_fallbackEventSystem");
            AssertCondition(fallback != null, "예비 EventSystem 이 설정되지 않았습니다.");
            AssertCondition(!fallback.activeSelf, "예비 EventSystem 은 기본적으로 비활성화 상태여야 합니다.");
            AssertCondition(fallback.GetComponent<EventSystem>() != null, "예비 EventSystem 에 EventSystem 컴포넌트가 없습니다.");
            checkCount++;

            return checkCount;
        }

        private static int RunPauseTimeScaleChecks()
        {
            var checkCount = 0;
            var host = new GameObject("PauseCheckHost");
            var controller = host.AddComponent<PausePanelController>();

            var originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 1.5f;

                controller.PauseGame();
                AssertCondition(controller.IsPaused, "PauseGame 호출 후 IsPaused 는 true 여야 합니다.");
                AssertCondition(Mathf.Approximately(Time.timeScale, 0f), "일시정지 중 Time.timeScale 은 0 이어야 합니다.");
                AssertCondition(Mathf.Approximately(controller.TimeScaleBeforePause, 1.5f), "일시정지 직전 Time.timeScale(1.5) 이 보존되어야 합니다.");
                checkCount++;

                controller.ResumeGame();
                AssertCondition(!controller.IsPaused, "ResumeGame 호출 후 IsPaused 는 false 여야 합니다.");
                AssertCondition(Mathf.Approximately(Time.timeScale, 1.5f), "재개 후 Time.timeScale 은 이전 값(1.5) 으로 정확히 복원되어야 합니다 (1f 로 덮어쓰지 않음).");
                checkCount++;
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        private static int RunConfirmDialogFlowChecks()
        {
            var checkCount = 0;
            var host = new GameObject("ConfirmCheckHost");
            var controller = host.AddComponent<PausePanelController>();

            var panelRoot = new GameObject("PanelRoot");
            panelRoot.transform.SetParent(host.transform);
            var confirmRoot = new GameObject("ConfirmRoot");
            confirmRoot.transform.SetParent(host.transform);
            confirmRoot.SetActive(false);

            SetPrivate(controller, "_panelRoot", panelRoot);
            SetPrivate(controller, "_confirmDialogRoot", confirmRoot);

            try
            {
                controller.PauseGame();
                AssertCondition(controller.IsPaused, "일시정지 상태여야 합니다.");

                // 메인 메뉴 클릭 핸들러 호출
                InvokePrivate(controller, "HandleMainMenuClicked");
                AssertCondition(controller.IsConfirmDialogOpen, "메인 메뉴 클릭 시 확인 대화상자가 열려야 합니다.");
                checkCount++;

                // 취소 클릭 핸들러 호출
                InvokePrivate(controller, "HandleConfirmCancelClicked");
                AssertCondition(!controller.IsConfirmDialogOpen, "취소 클릭 시 확인 대화상자가 닫혀야 합니다.");
                AssertCondition(controller.IsPaused, "확인창 취소 후에도 일시정지 상태는 유지되어야 합니다.");
                checkCount++;

                // 종료 클릭 핸들러 호출
                InvokePrivate(controller, "HandleQuitClicked");
                AssertCondition(controller.IsConfirmDialogOpen, "종료 클릭 시 확인 대화상자가 열려야 합니다.");
                checkCount++;

                // 확인창 열린 상태에서 ESC 키 처리
                controller.HandleEscapePressed();
                AssertCondition(!controller.IsConfirmDialogOpen, "확인창이 열린 상태에서 ESC 입력 시 확인창이 닫혀야 합니다.");
                AssertCondition(controller.IsPaused, "확인창이 닫힌 뒤에도 일시정지 상태는 유지되어야 합니다.");
                checkCount++;

                // 일시정지 상태에서 ESC 키 처리 -> 재개
                controller.HandleEscapePressed();
                AssertCondition(!controller.IsPaused, "일시정지 상태에서 ESC 입력 시 게임이 재개되어야 합니다.");
                checkCount++;
            }
            finally
            {
                controller.ResumeGame();
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        private static int RunSettingsPanelFlowChecks()
        {
            var checkCount = 0;
            var host = new GameObject("SettingsFlowCheckHost");
            var controller = host.AddComponent<PausePanelController>();

            var mainPanel = new GameObject("MainPanel");
            mainPanel.transform.SetParent(host.transform);

            var settingsHost = new GameObject("SettingsPanelHost", typeof(RectTransform));
            settingsHost.transform.SetParent(host.transform);
            var settingsCtrl = settingsHost.AddComponent<SettingsPanelController>();
            settingsHost.SetActive(false);

            SetPrivate(controller, "_mainPanel", mainPanel);
            controller.SetSettingsPanel(settingsCtrl);

            try
            {
                controller.PauseGame();
                AssertCondition(controller.IsPaused, "일시정지 상태여야 합니다.");
                AssertCondition(mainPanel.activeSelf, "일시정지 시 메인 패널이 켜져 있어야 합니다.");

                // 설정 버튼 클릭 핸들러 호출
                InvokePrivate(controller, "HandleSettingsClicked");
                AssertCondition(settingsHost.activeSelf, "설정 버튼 클릭 시 설정 패널이 켜져야 합니다.");
                AssertCondition(!mainPanel.activeSelf, "설정 패널이 켜지면 일시정지 메인 상자는 숨겨져야 합니다.");
                checkCount++;

                // 설정 패널이 켜진 상태에서 ESC 입력 -> 설정 패널 닫기 및 메인 상자 복귀
                controller.HandleEscapePressed();
                AssertCondition(!settingsHost.activeSelf, "설정 패널이 켜진 상태에서 ESC 입력 시 설정 패널이 닫혀야 합니다.");
                AssertCondition(mainPanel.activeSelf, "설정 패널 닫힘 후 일시정지 메인 상자가 다시 보여야 합니다.");
                AssertCondition(controller.IsPaused, "설정 패널을 닫아도 일시정지는 유지되어야 합니다.");
                checkCount++;

                // 설정 패널을 켠 뒤 Close() 메서드로 닫힘 -> 메인 상자 복귀
                InvokePrivate(controller, "HandleSettingsClicked");
                AssertCondition(settingsHost.activeSelf, "설정 패널이 다시 켜져야 합니다.");
                settingsCtrl.Close();
                AssertCondition(!settingsHost.activeSelf, "Close() 호출 시 설정 패널이 닫혀야 합니다.");
                AssertCondition(mainPanel.activeSelf, "Closed 이벤트에 의해 메인 상자가 다시 켜져야 합니다.");
                checkCount++;
            }
            finally
            {
                controller.ResumeGame();
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        private static T GetPrivate<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? (T)field.GetValue(target) : default;
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(target, null);
        }

        private static void AssertCondition(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("[PausePanelUiChecks] FAIL — " + message);
            }
        }
    }
}
