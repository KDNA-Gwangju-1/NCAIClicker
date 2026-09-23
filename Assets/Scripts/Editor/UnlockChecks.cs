using System;
using NCAIClicker.Data;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 크리처 단계별 해금 계산을 검증한다 (#247). 기대값은 코드에 적지 않고 stage_spawns.csv 산출물에서 읽는다.
    /// </summary>
    public static class UnlockChecks
    {
        public static void RunBatch()
        {
            var checkCount = 0;
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            Assert(balance != null, "BalanceData 에셋을 찾지 못했습니다.");

            // 1. 해금 단계 = 비율이 0 보다 큰 첫 단계. 행이 없는 종류는 0 (등장하지 않음)
            foreach (var target in balance.Targets)
            {
                var expected = 0;
                foreach (var spawn in balance.StageSpawns)
                {
                    if (spawn.TargetId == target.Id && spawn.Ratio > 0f && (expected == 0 || spawn.Stage < expected))
                    {
                        expected = spawn.Stage;
                    }
                }
                Assert(balance.GetUnlockStage(target.Id) == expected,
                       target.Id + " 해금 단계가 " + balance.GetUnlockStage(target.Id) + " 입니다. " + expected + " 여야 합니다.");
            }
            checkCount++;

            // 2. 해금 목록은 단계마다 늘기만 하고, 그 단계의 스폰 종류를 모두 포함한다
            var previous = 0;
            for (var stage = 1; stage <= balance.Stages.Count; stage++)
            {
                var unlocked = balance.GetUnlockedTargets(stage);
                Assert(unlocked.Count >= previous, stage + "단계 해금 목록이 줄었습니다.");
                foreach (var spawn in balance.GetStageSpawns(stage))
                {
                    if (spawn.Ratio > 0f)
                    {
                        Assert(unlocked.Exists(t => t.Id == spawn.TargetId),
                               stage + "단계에 나오는 " + spawn.TargetId + " 가 해금 목록에 없습니다.");
                    }
                }
                for (var i = 1; i < unlocked.Count; i++)
                {
                    Assert(balance.GetUnlockStage(unlocked[i - 1].Id) <= balance.GetUnlockStage(unlocked[i].Id),
                           stage + "단계 해금 목록이 해금 순서가 아닙니다.");
                }
                previous = unlocked.Count;
            }
            checkCount++;

            // 3. 다음 해금 종류는 다음 단계들 중 가장 먼저 나오는 것, 마지막 단계에서는 없다
            for (var stage = 1; stage <= balance.Stages.Count; stage++)
            {
                var next = balance.GetNextUnlockTarget(stage);
                if (next != null)
                {
                    var nextStage = balance.GetUnlockStage(next.Id);
                    Assert(nextStage > stage, stage + "단계의 다음 해금 " + next.Id + " 가 이미 해금돼 있습니다.");
                    foreach (var target in balance.Targets)
                    {
                        var s = balance.GetUnlockStage(target.Id);
                        Assert(s <= stage || s >= nextStage, stage + "단계 다음 해금보다 먼저 오는 " + target.Id + " 가 있습니다.");
                    }
                }
            }
            Assert(balance.GetNextUnlockTarget(balance.Stages.Count) == null, "마지막 단계에 다음 해금이 남아 있습니다.");
            checkCount++;

            // 4. 해금되는 종류는 스포너(Managers)와 결과 화면 미리보기 양쪽에 같은 프리팹이 연결돼 있어야 한다
            var managers = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/Managers.prefab");
            var spawner = managers != null ? managers.GetComponentInChildren<NCAIClicker.Core.CreatureManager>(true) : null;
            var resultUi = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/UI/ResultUI.prefab");
            var preview = resultUi != null ? resultUi.GetComponentInChildren<NCAIClicker.UI.CreaturePreview>(true) : null;
            Assert(spawner != null, "Managers 프리팹에 CreatureManager 가 없습니다.");
            Assert(preview != null, "ResultUI 프리팹에 CreaturePreview 가 없습니다.");
            var spawnerList = new SerializedObject(spawner).FindProperty("_targetPrefabs");
            var previewList = new SerializedObject(preview).FindProperty("_prefabs");
            foreach (var target in balance.GetUnlockedTargets(balance.Stages.Count))
            {
                var fromSpawner = FindPrefab(spawnerList, target.Id);
                Assert(fromSpawner != null, target.Id + " 의 프리팹이 Managers 목록에 없습니다.");
                Assert(FindPrefab(previewList, target.Id) == fromSpawner,
                       target.Id + " 의 결과 화면 미리보기 프리팹이 Managers 목록과 다릅니다. " +
                       "NCAI/UI/결과 화면 프리팹 생성 또는 ResultUIPrefabCreator.AttachCreaturePreview 로 다시 맞추세요.");
            }
            checkCount++;

            Debug.Log("[UnlockChecks] PASS " + checkCount + " checks.");
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
