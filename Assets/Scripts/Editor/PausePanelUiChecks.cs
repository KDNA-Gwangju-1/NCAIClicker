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
            checkCount += RunResetDuringRunChecks();

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

        /// <summary>
        /// 런 도중 저장 초기화가 런을 접는지 본다 (이슈 #220).
        ///
        /// **씬 전환 자체는 여기서 확인할 수 없다** — SceneManager.LoadScene 은 Play Mode 전용이라
        /// 부르면 콘솔 오류가 난다. 그래서 그 직전까지(구독이 붙어 있는지, 초기화가 이벤트를
        /// 발행하는지, 넘기기 전에 timeScale 을 되돌리는지)를 본다. 씬이 실제로 넘어가는지는
        /// Play Mode 로 확인하고 기술 문서에 적는다.
        /// </summary>
        private static int RunResetDuringRunChecks()
        {
            var checkCount = 0;
            var host = new GameObject("ResetDuringRunCheckHost");
            var controller = host.AddComponent<PausePanelController>();

            var settingsHost = new GameObject("SettingsPanelHost", typeof(RectTransform));
            settingsHost.transform.SetParent(host.transform);
            var settingsCtrl = settingsHost.AddComponent<SettingsPanelController>();
            settingsHost.SetActive(false);

            try
            {
                AssertCondition(typeof(SettingsPanelController).GetEvent("ResetPerformed") != null,
                                "SettingsPanelController 에 ResetPerformed 이벤트가 없습니다.");
                checkCount++;

                // **초기화가 이벤트를 발행하는지.** 발행하지 않으면 런이 그대로 이어져
                // 지운 성장으로 계속 플레이하게 된다 — 이 카드가 막으려는 결함이 그것이다.
                var settingsSource = ReadSource("Assets/Scripts/Runtime/UI/SettingsPanelController.cs");
                var resetBody = ExtractMethodBody(settingsSource, "private void HandleResetConfirmed");
                AssertCondition(resetBody.Contains("ResetPerformed?.Invoke()"),
                                "HandleResetConfirmed 가 ResetPerformed 를 발행하지 않습니다. " +
                                "런이 그대로 이어져 지운 성장으로 계속 플레이하게 됩니다.");
                checkCount++;

                // 구독 쌍. 빠뜨리면 초기화해도 아무 일이 없거나, 두 번 붙어 씬이 두 번 넘어간다.
                controller.SetSettingsPanel(settingsCtrl);
                AssertCondition(CountSubscribers(settingsCtrl, "ResetPerformed") == 1,
                                "SetSettingsPanel 뒤 ResetPerformed 구독자가 1개가 아닙니다.");
                checkCount++;

                InvokePrivate(controller, "OnDisable");
                AssertCondition(CountSubscribers(settingsCtrl, "ResetPerformed") == 0,
                                "OnDisable 이 ResetPerformed 구독을 해제하지 않습니다. " +
                                "정적 이벤트가 아니어도 쌍을 맞추지 않으면 파괴된 패널이 계속 반응합니다.");
                InvokePrivate(controller, "OnEnable");
                AssertCondition(CountSubscribers(settingsCtrl, "ResetPerformed") == 1,
                                "OnEnable 이 ResetPerformed 를 다시 구독하지 않습니다.");
                checkCount++;

                // **timeScale 복원이 씬 전환보다 앞이어야 한다.** 일시정지 중이면 0 이라,
                // 되돌리지 않고 넘기면 메인 메뉴가 0배속으로 열려 멈춰 보인다.
                var pauseSource = ReadSource("Assets/Scripts/Runtime/UI/PausePanelController.cs");
                var handlerBody = ExtractMethodBody(pauseSource, "private void HandleSettingsReset");
                var restoreAt = handlerBody.IndexOf("Time.timeScale", StringComparison.Ordinal);
                var loadAt = handlerBody.IndexOf("LoadScene", StringComparison.Ordinal);
                AssertCondition(restoreAt >= 0 && loadAt >= 0 && restoreAt < loadAt,
                                "HandleSettingsReset 이 timeScale 을 되돌리기 전에 씬을 넘깁니다. " +
                                "메인 메뉴가 0배속으로 열려 멈춰 보입니다.");
                AssertCondition(handlerBody.Contains("MainMenuSceneName"),
                                "HandleSettingsReset 이 메인 메뉴로 보내지 않습니다.");
                checkCount++;

                // 되돌릴 수 없는 동작이므로 무슨 일이 일어나는지 먼저 말해야 한다.
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/SettingsPanel.prefab");
                AssertCondition(prefab != null, "SettingsPanel 프리팹을 찾지 못했습니다.");
                var warned = false;
                foreach (var label in prefab.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (label.text != null && label.text.Contains("초기화할까요") && label.text.Contains("메인 메뉴"))
                    {
                        warned = true;
                    }
                }
                AssertCondition(warned,
                                "초기화 확인창이 런이 끝난다는 사실을 알리지 않습니다. " +
                                "되돌릴 수 없는 동작이라 무엇이 일어나는지 먼저 말해야 합니다.");
                checkCount++;
            }
            finally
            {
                InvokePrivate(controller, "OnDisable");
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        private static string ReadSource(string path)
        {
            AssertCondition(System.IO.File.Exists(path), path + " 를 찾지 못했습니다.");
            return System.IO.File.ReadAllText(path);
        }

        /// <summary>
        /// 메서드 하나의 본문만 잘라낸다. 파일 전체에 Contains 를 걸면 다른 메서드의 호출이
        /// 대신 걸려 "어느 메서드에 두었는가" 가 검사에서 빠진다.
        /// </summary>
        private static string ExtractMethodBody(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            AssertCondition(start >= 0, signature + " 를 찾지 못했습니다. 이름이 바뀌었습니까?");

            var open = source.IndexOf('{', start);
            AssertCondition(open >= 0, signature + " 의 본문 시작을 찾지 못했습니다.");

            var depth = 0;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(open, i - open + 1);
                    }
                }
            }

            AssertCondition(false, signature + " 의 본문이 닫히지 않았습니다.");
            return string.Empty;
        }

        /// <summary>이벤트의 뒷단 필드를 읽어 구독자 수를 센다. 쌍이 맞는지 보려면 수가 필요하다.</summary>
        private static int CountSubscribers(object target, string eventName)
        {
            var field = target.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            AssertCondition(field != null, eventName + " 의 뒷단 필드를 찾지 못했습니다.");
            var handler = field.GetValue(target) as Delegate;
            return handler == null ? 0 : handler.GetInvocationList().Length;
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
