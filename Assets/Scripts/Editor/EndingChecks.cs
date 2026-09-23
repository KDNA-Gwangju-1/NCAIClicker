using System;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using NCAIClicker.UI;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 엔딩 패널을 검증한다 (이슈 #271).
    ///
    /// 핵심은 판정이다 — **마지막 직전 단계를 냈을 때 뜨면 안 된다.** DoD 문구대로
    /// `IsMaxStage` 로 판정하면 바로 그 버그가 난다 (EndingController 주석 참고).
    /// 두 번째는 시간이다: 퍽 컨트롤러가 시간을 되돌린 **뒤에** 멈춰야 닫을 때 게임이 굳지 않는다.
    ///
    /// 한계: Edit Mode 라 OnEnable·Update 를 리플렉션으로 직접 부르고, 이벤트는 GameEvents 로 직접 발행한다.
    /// </summary>
    public static class EndingChecks
    {
        private const string PanelPath = "Assets/Prefabs/UI/EndingPanel.prefab";
        private const string ManagersPath = "Assets/Prefabs/Resources/Managers.prefab";
        private const int PerkPanelSortingOrder = 100;

        public static void RunBatch()
        {
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");
            AssertCondition(balance.Stages.Count >= 2, "단계가 2개 미만이라 '마지막 직전' 을 검증할 수 없습니다.");

            var checkCount = RunPrefabChecks();
            checkCount += RunFlowChecks(balance);
            Debug.Log("[EndingChecks] PASS " + checkCount + " checks.");
        }

        private static int RunPrefabChecks()
        {
            var panel = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
            AssertCondition(panel != null, "엔딩 패널 프리팹을 찾지 못했습니다: " + PanelPath);
            var view = panel.GetComponent<EndingPanelView>();
            AssertCondition(view != null, "패널 루트에 EndingPanelView 가 없습니다.");
            foreach (var field in new[] { "_daysText", "_cycleText", "_totalPaidText", "_continueButton" })
            {
                AssertCondition(GetPrivate<UnityEngine.Object>(view, field) != null, "EndingPanelView 의 " + field + " 가 비어 있습니다.");
            }

            var canvas = panel.GetComponent<Canvas>();
            AssertCondition(canvas != null && canvas.sortingOrder > PerkPanelSortingOrder,
                            "엔딩 Canvas 가 퍽 선택(100) 보다 위가 아닙니다 — 새 고지서 모달에 가립니다.");

            var fallback = panel.GetComponentInChildren<UnityEngine.EventSystems.EventSystem>(true);
            AssertCondition(fallback != null && !fallback.gameObject.activeSelf,
                            "예비 EventSystem 이 없거나 켜진 채로 저장됐습니다. Game 씬에는 EventSystem 이 없습니다.");

            var managers = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPath);
            var controller = managers != null ? managers.GetComponent<EndingController>() : null;
            AssertCondition(controller != null, "Managers 프리팹에 EndingController 가 없습니다. 게임에서 엔딩이 뜨지 않습니다.");
            AssertCondition(GetPrivate<UnityEngine.Object>(controller, "_balanceData") != null, "EndingController 의 _balanceData 가 비어 있습니다.");
            AssertCondition(GetPrivate<UnityEngine.Object>(controller, "_panelPrefab") != null, "EndingController 의 _panelPrefab 이 비어 있습니다.");
            return 3;
        }

        private static int RunFlowChecks(BalanceData balance)
        {
            var checkCount = 0;
            var lastStage = balance.Stages.Count;
            var originalTimeScale = Time.timeScale;
            var originalInstance = BillManager.Instance;
            GameObject billHost = null;
            GameObject controllerHost = null;

            try
            {
                billHost = new GameObject("EndingBillHost") { hideFlags = HideFlags.HideAndDontSave };
                billHost.SetActive(false);
                var bills = billHost.AddComponent<BillManager>();
                SetPrivate(bills, "_balanceData", balance);
                billHost.SetActive(true);
                SetBillManagerInstance(bills);

                controllerHost = new GameObject("EndingControllerHost") { hideFlags = HideFlags.HideAndDontSave };
                controllerHost.SetActive(false);
                var controller = controllerHost.AddComponent<EndingController>();
                SetPrivate(controller, "_balanceData", balance);
                SetPrivate(controller, "_panelPrefab", AssetDatabase.LoadAssetAtPath<EndingPanelView>(PanelPath));
                controllerHost.SetActive(true);
                Invoke(controller, "HandleBankrupt"); // 앞선 검증이 남긴 정적 플래그를 지운다
                Invoke(controller, "OnEnable");

                // --- 마지막 직전 단계: 뜨면 안 된다 (IsMaxStage 판정이면 여기서 뜬다)
                GameEvents.PublishStageGoalReached(lastStage - 1);
                GameEvents.PublishPerkChosen("any");
                AssertCondition(!EndingController.IsRunCleared, (lastStage - 1) + "단계를 냈는데 엔딩으로 판정했습니다.");
                checkCount++;

                // --- 마지막 단계: 퍽 선택 시점에 즉시 클리어, 패널은 다음 프레임
                GameEvents.PublishStageGoalReached(lastStage);
                AssertCondition(!EndingController.IsRunCleared, "퍽을 고르기 전에 엔딩으로 넘어갔습니다 — 순서는 퍽 → 엔딩입니다.");
                Time.timeScale = 0f; // 퍽 패널이 멈춰 둔 상태
                GameEvents.PublishPerkChosen("any");
                AssertCondition(EndingController.IsRunCleared, "마지막 단계를 내고 퍽을 골랐는데 클리어가 아닙니다.");
                Time.timeScale = originalTimeScale; // 퍽 컨트롤러의 ResumeTime
                Invoke(controller, "Update");
                var panel = GetPrivate<EndingPanelView>(controller, "_panelInstance");
                AssertCondition(panel != null && panel.gameObject.activeSelf, "엔딩 패널이 뜨지 않았습니다.");
                AssertNear(Time.timeScale, 0f, "엔딩 패널이 떴는데 시간이 멈추지 않았습니다.");
                checkCount++;

                // --- 계속하기: 원래 시간으로 (0 을 기억했으면 여기서 게임이 굳는다)
                Invoke(controller, "HandleContinueClicked");
                AssertCondition(!panel.gameObject.activeSelf, "계속하기를 눌렀는데 패널이 닫히지 않았습니다.");
                AssertNear(Time.timeScale, originalTimeScale, "계속하기 뒤 시간이 돌아오지 않았습니다. timeScale=" + Time.timeScale);
                checkCount++;

                // --- 회차당 한 번: 마지막 단계 재발행 고지서를 또 내도 다시 뜨지 않는다
                GameEvents.PublishStageGoalReached(lastStage);
                GameEvents.PublishPerkChosen("any");
                Invoke(controller, "Update");
                AssertCondition(!panel.gameObject.activeSelf, "재발행 고지서를 냈는데 엔딩이 또 떴습니다.");
                checkCount++;

                // --- 파산하면 내린다
                Invoke(controller, "HandleBankrupt");
                AssertCondition(!EndingController.IsRunCleared, "파산했는데 클리어 플래그가 남았습니다.");
                checkCount++;
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                SetBillManagerInstance(originalInstance);
                if (controllerHost != null)
                {
                    var controller = controllerHost.GetComponent<EndingController>();
                    Invoke(controller, "OnDisable");
                    Invoke(controller, "HandleBankrupt");
                    UnityEngine.Object.DestroyImmediate(controllerHost);
                }
                if (billHost != null)
                {
                    UnityEngine.Object.DestroyImmediate(billHost);
                }
            }

            return checkCount;
        }

        private static void Invoke(Component component, string methodName)
        {
            var method = component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(method != null, methodName + " 을 찾지 못했습니다.");
            method.Invoke(component, null);
        }

        private static void SetBillManagerInstance(IBillService service)
        {
            typeof(BillManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).SetValue(null, service, null);
        }

        private static T GetPrivate<T>(object target, string fieldName) where T : class
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, fieldName + " 필드를 찾지 못했습니다.");
            return field.GetValue(target) as T;
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            AssertCondition(field != null, fieldName + " 필드를 찾지 못했습니다.");
            field.SetValue(target, value);
        }

        private static void AssertNear(float actual, float expected, string message)
        {
            AssertCondition(Mathf.Abs(actual - expected) <= 0.001f, message);
        }

        private static void AssertCondition(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
