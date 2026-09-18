using System;
using System.Reflection;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 결과 화면 2종(하루 정산 및 파산) UI 컨트롤러 검증 (이슈 #34).
    /// </summary>
    public static class ResultUIChecks
    {
        public static void RunBatch()
        {
            var checkCount = 0;
            checkCount += RunAccuracyChecks();
            checkCount += RunViewBranchChecks();
            checkCount += RunResultPrefabChecks();

            Debug.Log("[ResultUIChecks] PASS " + checkCount + " checks.");
        }

        private static int RunAccuracyChecks()
        {
            var checkCount = 0;
            var host = new GameObject("ResultUIControllerHost");
            var controller = host.AddComponent<ResultUIController>();

            try
            {
                AssertCondition(Mathf.Approximately(controller.Accuracy, 0f), "스윙 기록이 없을 때 정확도는 0이어야 합니다.");
                checkCount++;

                controller.HandleSwingResolved(HitSource.Hover, true);
                AssertCondition(controller.TotalHoverSwings == 1, "호버 스윙 총 횟수가 증가해야 합니다.");
                AssertCondition(controller.HitHoverSwings == 1, "호버 적중 횟수가 증가해야 합니다.");
                AssertCondition(Mathf.Approximately(controller.Accuracy, 100f), "1회 시도 1회 적중 시 100%여야 합니다.");
                checkCount++;

                controller.HandleSwingResolved(HitSource.Hover, false);
                AssertCondition(controller.TotalHoverSwings == 2, "호버 스윙 총 횟수가 2여야 합니다.");
                AssertCondition(controller.HitHoverSwings == 1, "적중 횟수는 1이어야 합니다.");
                AssertCondition(Mathf.Approximately(controller.Accuracy, 50f), "2회 시도 1회 적중 시 50%여야 합니다.");
                checkCount++;

                controller.HandleSwingResolved(HitSource.AutoHammer, true);
                controller.HandleSwingResolved(HitSource.AutoHammer, false);
                AssertCondition(controller.TotalHoverSwings == 2, "자동 망치는 호버 정확도 분모에 포함되지 않아야 합니다.");
                AssertCondition(controller.HitHoverSwings == 1, "자동 망치는 호버 정확도 분자에 포함되지 않아야 합니다.");
                checkCount++;

                controller.ResetRunStats();
                AssertCondition(controller.TotalHoverSwings == 0, "리셋 후 총 스윙 수는 0이어야 합니다.");
                AssertCondition(controller.HitHoverSwings == 0, "리셋 후 적중 수는 0이어야 합니다.");
                AssertCondition(Mathf.Approximately(controller.Accuracy, 0f), "리셋 후 정확도는 0이어야 합니다.");
                checkCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        private static int RunViewBranchChecks()
        {
            var checkCount = 0;
            var host = new GameObject("ResultUIControllerBranchHost");
            var panelRoot = new GameObject("PanelRoot");
            var settlement = new GameObject("SettlementContainer");
            var bankruptcy = new GameObject("BankruptcyContainer");

            panelRoot.transform.SetParent(host.transform);
            settlement.transform.SetParent(panelRoot.transform);
            bankruptcy.transform.SetParent(panelRoot.transform);

            var controller = host.AddComponent<ResultUIController>();

            var type = typeof(ResultUIController);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            type.GetField("_panelRoot", flags)?.SetValue(controller, panelRoot);
            type.GetField("_settlementContainer", flags)?.SetValue(controller, settlement);
            type.GetField("_bankruptcyContainer", flags)?.SetValue(controller, bankruptcy);

            try
            {
                controller.HideAll();
                AssertCondition(!controller.IsPanelActive, "HideAll 호출 후 루트 패널이 비활성화되어야 합니다.");
                AssertCondition(!controller.IsSettlementActive, "HideAll 호출 후 정산 패널이 비활성화되어야 합니다.");
                AssertCondition(!controller.IsBankruptcyActive, "HideAll 호출 후 파산 패널이 비활성화되어야 합니다.");
                checkCount++;

                controller.ShowSettlement();
                AssertCondition(controller.IsPanelActive, "ShowSettlement 호출 후 루트 패널이 활성화되어야 합니다.");
                AssertCondition(controller.IsSettlementActive, "ShowSettlement 호출 후 정산 패널이 활성화되어야 합니다.");
                AssertCondition(!controller.IsBankruptcyActive, "ShowSettlement 호출 시 파산 패널은 꺼져 있어야 합니다.");
                checkCount++;

                controller.ShowBankruptcy();
                AssertCondition(controller.IsPanelActive, "ShowBankruptcy 호출 후 루트 패널이 활성화되어야 합니다.");
                AssertCondition(!controller.IsSettlementActive, "ShowBankruptcy 호출 시 정산 패널은 꺼져 있어야 합니다.");
                AssertCondition(controller.IsBankruptcyActive, "ShowBankruptcy 호출 후 파산 패널이 활성화되어야 합니다.");
                checkCount++;

                controller.ResetRunStats();
                AssertCondition(!controller.IsPanelActive, "ResetRunStats 후 패널이 닫혀야 합니다.");
                checkCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            return checkCount;
        }

        /// <summary>
        /// 생성된 프리팹 자체를 본다. 직렬화 참조는 이름만 어긋나도 조용히 null 이 되고,
        /// 그러면 화면에 칸이 통째로 비는데 에러는 한 줄도 안 난다.
        /// </summary>
        private static int RunResultPrefabChecks()
        {
            const string prefabPath = "Assets/Prefabs/Resources/UI/ResultUI.prefab";
            var checkCount = 0;

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            AssertCondition(prefab != null, $"{prefabPath} 가 없습니다. NCAI > UI > 결과 화면 프리팹 생성 을 실행하세요.");
            checkCount++;

            // 같은 프리팹이 두 벌 있으면 인스펙터에서 고친 쪽과 런타임이 읽는 쪽이 갈라진다.
            var duplicate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/ResultUI.prefab");
            AssertCondition(duplicate == null, "Assets/Prefabs/UI/ResultUI.prefab 사본이 남아 있습니다. Resources 아래 한 벌만 둡니다.");
            checkCount++;

            var controller = prefab.GetComponent<ResultUIController>();
            AssertCondition(controller != null, "프리팹 루트에 ResultUIController 가 없습니다.");
            checkCount++;

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var fields = typeof(ResultUIController).GetFields(flags);
            for (var i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (!field.IsDefined(typeof(SerializeField), true))
                {
                    continue;
                }

                var value = field.GetValue(controller);
                if (value is Array array)
                {
                    AssertCondition(array.Length > 0, $"{field.Name} 배열이 비어 있습니다.");
                    for (var j = 0; j < array.Length; j++)
                    {
                        AssertCondition(array.GetValue(j) as UnityEngine.Object != null, $"{field.Name}[{j}] 참조가 비어 있습니다.");
                    }
                    continue;
                }

                AssertCondition(value as UnityEngine.Object != null, $"{field.Name} 참조가 프리팹에서 비어 있습니다.");
            }
            checkCount++;

            // 동작이 없는 버튼은 눌리면 안 된다. 눌리면 아무 일도 안 일어나고 플레이어는 고장으로 읽는다.
            var payButton = FindButton(prefab, "PayButton");
            AssertCondition(payButton != null, "납부 버튼을 프리팹에서 찾지 못했습니다.");
            AssertCondition(!payButton.interactable, "PayButton 은 배선 전까지 interactable = false 여야 합니다.");
            checkCount++;

            // 도박(더블 오어 낫싱)은 MVP 밖이다 — REFERENCE_ANALYSIS.md 가 "추가 목표, 우선순위 낮음"
            // 으로 분류했다. 기획에 없는 기능이 화면에 남아 있으면 구현된 줄 알고 눌러 본다.
            AssertCondition(FindButton(prefab, "GambleButton") == null, "GambleButton 은 MVP 범위 밖이라 프리팹에 없어야 합니다.");
            checkCount++;

            return checkCount;
        }

        private static Button FindButton(GameObject root, string name)
        {
            var buttons = root.GetComponentsInChildren<Button>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].name == name)
                {
                    return buttons[i];
                }
            }
            return null;
        }

        private static void AssertCondition(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("[ResultUIChecks] " + message);
            }
        }
    }
}
