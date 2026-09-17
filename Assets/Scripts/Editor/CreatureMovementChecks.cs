using System;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using NCAIClicker.Targets;
using NCAIClicker.Core;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 크리처 이동, FSM 상태 전이, 스폰 로직을 검증한다.
    /// </summary>
    public static class CreatureMovementChecks
    {
        [MenuItem("NCAI/크리처 이동·스폰 검증")]
        public static void RunBatch()
        {
            var passedCount = 0;
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            Assert(balance != null, "BalanceData 에셋을 로드하지 못했습니다.");

            // 1. FSM 상태 열거형 검증
            Assert(Enum.IsDefined(typeof(CreatureState), CreatureState.Idle), "CreatureState.Idle 정의 누락");
            Assert(Enum.IsDefined(typeof(CreatureState), CreatureState.Moving), "CreatureState.Moving 정의 누락");
            Assert(Enum.IsDefined(typeof(CreatureState), CreatureState.BeingHit), "CreatureState.BeingHit 정의 누락");
            Assert(Enum.IsDefined(typeof(CreatureState), CreatureState.Fleeing), "CreatureState.Fleeing 정의 누락");
            passedCount++;

            // 2. CreatureMovement 컴포넌트 생성 및 초기화 검증
            var go = new GameObject("TestCreature");
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var target = go.AddComponent<Target>();
                var movement = go.AddComponent<CreatureMovement>();

                var testBounds = new Bounds(new Vector3(0f, 0f, 2f), new Vector3(4f, 1f, 4f));
                movement.Initialize(balance, "normal", testBounds);

                Assert(movement.CurrentState == CreatureState.Moving, "초기화 후 기본 상태는 Moving 이어야 합니다.");
                Assert(Mathf.Abs(movement.MoveSpeed - 2.0f) < 0.01f, "normal 타입의 move_speed는 2.0 이어야 합니다.");
                Assert(Mathf.Abs(movement.TurnIntervalSec - 1.5f) < 0.01f, "normal 타입의 turn_interval_sec는 1.5 이어야 합니다.");
                passedCount++;

                // 3. FSM 피격 전이 검증 (BeingHit)
                movement.ChangeState(CreatureState.BeingHit);
                Assert(movement.CurrentState == CreatureState.BeingHit, "BeingHit 상태 전이 실패");
                passedCount++;

                // 4. 경직 시간 후 Fleeing 전이 검증
                movement.UpdateFSM(0.25f);
                Assert(movement.CurrentState == CreatureState.Fleeing, "경직 후 Fleeing 전이 실패");
                passedCount++;

                // 5. 도망 시간 후 Moving 복귀 검증
                movement.UpdateFSM(1.1f);
                Assert(movement.CurrentState == CreatureState.Moving, "도망 후 Moving 복귀 실패");
                passedCount++;

                // 6. 경계 클램프 및 반사 검증
                var outOfBoundsPos = new Vector3(5f, 0f, 2f);
                movement.SetDirection(new Vector3(1f, 0f, 0f));
                var clampedPos = movement.ClampAndBounce(outOfBoundsPos);

                Assert(clampedPos.x <= testBounds.max.x, "경계 X 최대값을 초과하지 않아야 합니다.");
                Assert(movement.CurrentDirection.x < 0f, "경계 도달 시 반대 방향으로 반사되어야 합니다.");
                passedCount++;

                // 7. 4종 크리처 속도 파싱 검증
                var types = new[] { "normal", "anchor", "runner", "tourist" };
                var expectedSpeeds = new[] { 2.0f, 0.2f, 5.0f, 0.8f };
                for (var i = 0; i < types.Length; i++)
                {
                    var def = balance.GetTarget(types[i]);
                    Assert(def != null, types[i] + " 정의가 targets.csv 에 없습니다.");
                    Assert(Mathf.Abs(def.MoveSpeed - expectedSpeeds[i]) < 0.01f, types[i] + " 속도 불일치");
                }
                passedCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            // 8. CreatureManager 계산 로직 검증 (하드코딩 방지)
            var mgrGo = new GameObject("TestCreatureManager");
            mgrGo.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var mgr = mgrGo.AddComponent<CreatureManager>();
                
                // 직렬화 필드 설정
                var serialized = new SerializedObject(mgr);
                serialized.FindProperty("_balanceData").objectReferenceValue = balance;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                // 1단계 기본 출현 수 6 검증
                var spawnCount = mgr.GetRequiredSpawnCount();
                Assert(spawnCount == 6, "1단계 spawn_count는 6 이어야 합니다. 현재: " + spawnCount);
                passedCount++;

                // 업그레이드 확장 검증 (desk_expand +2레벨 시)
                mgr.SetUpgradeOverrides(2, 0.88f);
                Assert(mgr.GetRequiredSpawnCount() == 8, "업그레이드 반영 시 spawn_count는 8 이어야 합니다.");
                passedCount++;

                // 재등장 대기시간 검증 (7.0초 기준 및 12% 단축)
                var interval = mgr.GetSpawnIntervalSec();
                Assert(Mathf.Abs(interval - (7.0f * 0.88f)) < 0.01f, "재등장 대기시간 업그레이드 계산 불일치");
                passedCount++;

                // 리스폰 타이머 처리 검증
                mgr.UpdateRespawnTimers(1.0f);
                passedCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mgrGo);
            }

            Debug.Log("[CreatureMovementChecks] 전체 " + passedCount + "개 검증 통과 완료.");
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
