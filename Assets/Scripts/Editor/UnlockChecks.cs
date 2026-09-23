using System;
using System.Collections.Generic;
using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 크리처 해금을 검증한다 (#247 → #301 회차 누적 수입 기준). 기대값은 코드에 적지 않고 targets.csv 산출물에서 읽는다.
    /// </summary>
    public static class UnlockChecks
    {
        public static void RunBatch()
        {
            var checkCount = 0;
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            Assert(balance != null, "BalanceData 에셋을 찾지 못했습니다.");

            // 1. 해금 순서 = spawn_weight > 0 인 종류를 unlock_earned 오름차순. 첫 종류는 기준 0
            var order = balance.GetUnlockOrder();
            Assert(order.Count > 0 && order[0].UnlockEarned == 0, "처음부터 나오는 종류(unlock_earned 0)가 없습니다.");
            for (var i = 1; i < order.Count; i++)
            {
                Assert(order[i - 1].UnlockEarned <= order[i].UnlockEarned, "해금 순서가 기준액 순이 아닙니다.");
            }
            foreach (var target in balance.Targets)
            {
                Assert(order.Contains(target) == (target.SpawnWeight > 0f),
                       target.Id + " 는 spawn_weight " + target.SpawnWeight + " 인데 해금 목록 포함 여부가 맞지 않습니다.");
            }
            checkCount++;

            // 2. 누적액이 기준을 넘는 순간 해금되고, 다음 해금 종류가 한 칸씩 넘어간다
            for (var i = 0; i < order.Count; i++)
            {
                var threshold = order[i].UnlockEarned;
                Assert(balance.GetUnlockedTargets(threshold).Contains(order[i]), order[i].Id + " 가 기준액에서 해금되지 않습니다.");
                if (threshold > 0)
                {
                    Assert(!balance.GetUnlockedTargets(threshold - 1).Contains(order[i]),
                           order[i].Id + " 가 기준액보다 1 적을 때 이미 해금돼 있습니다.");
                    Assert(balance.GetNextUnlockTarget(threshold - 1) == order[i], order[i].Id + " 직전의 다음 해금이 다릅니다.");
                }
            }
            Assert(balance.GetNextUnlockTarget(order[order.Count - 1].UnlockEarned) == null, "모두 해금됐는데 다음 해금이 남았습니다.");
            checkCount++;

            checkCount += RunEconomyChecks(balance, order);

            // 4. 해금되는 종류는 스포너(Managers)와 결과 화면 미리보기 양쪽에 같은 프리팹이 연결돼 있어야 한다
            var managers = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/Managers.prefab");
            var spawner = managers != null ? managers.GetComponentInChildren<NCAIClicker.Core.CreatureManager>(true) : null;
            var resultUi = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/UI/ResultUI.prefab");
            var preview = resultUi != null ? resultUi.GetComponentInChildren<NCAIClicker.UI.CreaturePreview>(true) : null;
            Assert(spawner != null, "Managers 프리팹에 CreatureManager 가 없습니다.");
            Assert(preview != null, "ResultUI 프리팹에 CreaturePreview 가 없습니다.");
            var spawnerList = new SerializedObject(spawner).FindProperty("_targetPrefabs");
            var previewList = new SerializedObject(preview).FindProperty("_prefabs");
            foreach (var target in order)
            {
                var fromSpawner = FindPrefab(spawnerList, target.Id);
                Assert(fromSpawner != null, target.Id + " 의 프리팹이 Managers 목록에 없습니다.");
                Assert(FindPrefab(previewList, target.Id) == fromSpawner,
                       target.Id + " 의 결과 화면 미리보기 프리팹이 Managers 목록과 다릅니다. " +
                       "NCAI/UI/결과 화면 프리팹 생성 또는 ResultUIPrefabCreator.AttachCreaturePreview 로 다시 맞추세요.");
            }
            checkCount++;

            checkCount += RunCodexChecks(balance, order, spawnerList);

            Debug.Log("[UnlockChecks] PASS " + checkCount + " checks.");
        }

        /// <summary>
        /// 3. EconomyManager 가 정산(EndRun) 때 누적하고, 새로 넘은 종류마다 이벤트를 한 번 낸다.
        /// 복원(RestoreEarnedTotal)은 이벤트를 내지 않는다.
        /// </summary>
        private static int RunEconomyChecks(BalanceData balance, List<TargetDef> order)
        {
            Assert(order.Count >= 3, "이 검증은 해금 종류가 3개 이상이어야 합니다.");
            var go = new GameObject("UnlockChecksEconomy");
            go.hideFlags = HideFlags.HideAndDontSave;
            var unlocked = new List<string>();
            Action<string> onUnlocked = id => unlocked.Add(id);
            try
            {
                GameEvents.OnCreatureUnlocked += onUnlocked;
                var economy = go.AddComponent<EconomyManager>();
                var serialized = new SerializedObject(economy);
                serialized.FindProperty("_balanceData").objectReferenceValue = balance;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Invoke(economy, "Awake");

                // 복원은 해금 순간이 아니다
                economy.RestoreEarnedTotal(order[1].UnlockEarned);
                Assert(unlocked.Count == 0, "복원이 해금 이벤트를 냈습니다.");
                Assert(economy.EarnedTotal == order[1].UnlockEarned, "복원한 누적 수입이 다릅니다.");

                // 한 번의 정산으로 두 종류를 넘으면 두 번 발행한다
                economy.RestoreEarnedTotal(order[1].UnlockEarned - 1);
                economy.BeginRun();
                economy.AddCoin(order[2].UnlockEarned - order[1].UnlockEarned + 1);
                var runCoin = economy.RunCoin;
                economy.EndRun();
                Assert(economy.EarnedTotal == order[1].UnlockEarned - 1 + runCoin,
                       "정산 뒤 누적 수입이 " + economy.EarnedTotal + " 입니다.");
                Assert(unlocked.Count == 2 && unlocked[0] == order[1].Id && unlocked[1] == order[2].Id,
                       "해금 이벤트가 [" + string.Join(",", unlocked) + "] 입니다. [" + order[1].Id + "," + order[2].Id + "] 여야 합니다.");

                // 같은 종류를 다시 넘지 않으면 다시 발행하지 않는다
                unlocked.Clear();
                economy.BeginRun();
                economy.AddCoin(1);
                economy.EndRun();
                Assert(unlocked.Count == 0, "이미 해금된 종류를 다시 알렸습니다.");

                // 음수 복원은 0 으로
                economy.RestoreEarnedTotal(-5);
                Assert(economy.EarnedTotal == 0, "음수 누적 수입이 0 으로 보정되지 않았습니다.");
            }
            finally
            {
                GameEvents.OnCreatureUnlocked -= onUnlocked;
                UnityEngine.Object.DestroyImmediate(go);
            }
            return 1;
        }

        private static void Invoke(Component component, string methodName)
        {
            var method = component.GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(component, null);
            }
        }

        /// <summary>
        /// 5. 저금통 도감 (#299). 카드가 해금 순서와 같고, 미리보기 프리팹이 스포너와 같고, 모델을 두는 좌표가 겹치지 않는다.
        /// 기대 코인은 실제 추첨 평균과 맞아야 한다 — 도감 숫자가 게임과 다른 말을 하면 안 된다.
        /// </summary>
        private static int RunCodexChecks(BalanceData balance, List<TargetDef> order, SerializedProperty spawnerList)
        {
            var codex = AssetDatabase.LoadAssetAtPath<GameObject>(CreatureCodexPrefabCreator.PrefabPath);
            Assert(codex != null, "저금통 도감 프리팹이 없습니다. NCAI/UI/저금통 도감 프리팹 생성을 실행하세요.");
            var entries = codex.GetComponentsInChildren<NCAIClicker.UI.CreatureCodexEntry>(true);
            Assert(entries.Length == order.Count,
                   "도감 카드 " + entries.Length + "장이 해금 종류 " + order.Count + "종과 다릅니다. NCAI/UI/저금통 도감 프리팹 생성을 다시 실행하세요.");
            var origins = new HashSet<Vector3>();
            for (var i = 0; i < entries.Length; i++)
            {
                Assert(entries[i].TargetId == order[i].Id, "도감 " + (i + 1) + "번째 카드가 " + order[i].Id + " 가 아닙니다.");
                var preview = entries[i].GetComponentInChildren<NCAIClicker.UI.CreaturePreview>(true);
                Assert(preview != null, order[i].Id + " 도감 카드에 CreaturePreview 가 없습니다.");
                var serialized = new SerializedObject(preview);
                Assert(FindPrefab(serialized.FindProperty("_prefabs"), order[i].Id) == FindPrefab(spawnerList, order[i].Id),
                       order[i].Id + " 도감 미리보기 프리팹이 Managers 목록과 다릅니다.");
                Assert(origins.Add(serialized.FindProperty("_stageOrigin").vector3Value),
                       order[i].Id + " 도감 미리보기 좌표가 다른 카드와 겹칩니다 — 한 카메라에 두 모델이 찍힙니다.");
            }

            var random = new System.Random(299);
            foreach (var target in order)
            {
                const int draws = 100000;
                var sum = 0m;
                for (var i = 0; i < draws; i++)
                {
                    sum += CoinLottery.SumValue(
                        CoinLottery.Draw(balance, target.MinDenomId, target.CoinCount, random.NextDouble), balance);
                }
                var expected = CoinLottery.GetExpectedValue(balance, target.MinDenomId, target.CoinCount);
                var average = sum / draws;
                Assert(expected > 0m && System.Math.Abs(average - expected) <= expected * 0.05m,
                       target.Id + " 기대 코인 " + expected + " 이 추첨 평균 " + average + " 과 5% 넘게 다릅니다.");
                Assert(!string.IsNullOrEmpty(NCAIClicker.UI.CreatureCodexEntry.GetRoleText(target)), target.Id + " 역할 문구가 비었습니다.");
            }
            return 1;
        }

        private static UnityEngine.Object FindPrefab(SerializedProperty list, string targetId)
        {
            for (var i = 0; i < list.arraySize; i++)
            {
                var entry = list.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("_targetId").stringValue == targetId)
                {
                    return entry.FindPropertyRelative("_prefab").objectReferenceValue;
                }
            }
            return null;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("[검증 실패] " + message);
            }
        }
    }
}
