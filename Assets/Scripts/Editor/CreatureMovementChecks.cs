using System;
using NCAIClicker.Data;
using NCAIClicker.Economy;
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

                Assert(movement.CurrentState == CreatureState.Idle, "초기화 후 자연스러운 시작을 위해 Idle 이어야 합니다.");
                movement.UpdateFSM(1.5f);
                Assert(movement.CurrentState == CreatureState.Moving, "대기 시간 경과 후 Moving 으로 전이되어야 합니다.");
                // 기대값은 코드에 적지 않고 targets.csv 산출물에서 읽는다 (AGENTS.md 데이터 절).
                var normalDef = balance.GetTarget("normal");
                Assert(normalDef != null, "targets.csv 에 normal 이 없습니다.");
                Assert(Mathf.Abs(movement.MoveSpeed - normalDef.MoveSpeed) < 0.01f,
                       "normal 의 move_speed 가 targets.csv 와 다릅니다: " + movement.MoveSpeed);
                Assert(Mathf.Abs(movement.TurnIntervalSec - normalDef.TurnIntervalSec) < 0.01f,
                       "normal 의 turn_interval_sec 가 targets.csv 와 다릅니다: " + movement.TurnIntervalSec);
                passedCount++;

                // 3. FSM 피격 전이 검증 (BeingHit). 도망 여부는 확률이라 검증에서는 고정한다 (#267)
                movement.SetFleeAfterHit(true);
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

                // 5-1. 도망치지 않는 피격 — 경직 후 Moving 으로 바로 복귀 (#267)
                movement.SetFleeAfterHit(false);
                movement.ChangeState(CreatureState.BeingHit);
                movement.UpdateFSM(0.25f);
                Assert(movement.CurrentState == CreatureState.Moving,
                       "도망치지 않는 피격은 경직 후 Moving 으로 복귀해야 합니다: " + movement.CurrentState);
                passedCount++;

                // 6. 경계 클램프 및 반사 검증
                var outOfBoundsPos = new Vector3(5f, 0f, 2f);
                movement.SetDirection(new Vector3(1f, 0f, 0f));
                var clampedPos = movement.ClampAndBounce(outOfBoundsPos);

                Assert(clampedPos.x <= testBounds.max.x, "경계 X 최대값을 초과하지 않아야 합니다.");
                Assert(movement.CurrentDirection.x < 0f, "경계 도달 시 반대 방향으로 반사되어야 합니다.");
                passedCount++;

                // 6-1. 바운스·회전 연출 검증 (#267) — 루트 y 는 고정, Visual 로컬 y 만 튄다
                var visualGo = new GameObject("Visual");
                visualGo.transform.SetParent(go.transform, false);
                var targetSerialized = new SerializedObject(target);
                targetSerialized.FindProperty("_visual").objectReferenceValue = visualGo.transform;
                targetSerialized.ApplyModifiedPropertiesWithoutUndo();

                var rootYBefore = go.transform.position.y;
                movement.SetDirection(new Vector3(1f, 0f, 0f));
                movement.ChangeState(CreatureState.Moving);
                movement.UpdateVisual(0.2f); // 한 주기 안의 중간쯤 → 정점 부근
                Assert(visualGo.transform.localPosition.y > 0f,
                       "Moving 중에는 Visual 이 위로 떠야 합니다: " + visualGo.transform.localPosition.y);
                Assert(visualGo.transform.localPosition.y <= movement.HopHeight + 0.001f,
                       "바운스 높이가 _hopHeight 를 넘으면 안 됩니다.");
                Assert(Mathf.Abs(go.transform.position.y - rootYBefore) < 0.0001f,
                       "바운스는 루트 y 를 건드리면 안 됩니다 (타격 판정 불변).");

                for (var i = 0; i < 20; i++)
                {
                    movement.UpdateVisual(0.1f);
                }
                Assert(Quaternion.Angle(go.transform.rotation, movement.GetTargetRotation()) < 1f,
                       "충분한 시간이 지나면 루트가 이동 방향(+X)을 바라봐야 합니다.");

                movement.ChangeState(CreatureState.Idle);
                movement.UpdateVisual(0.1f);
                Assert(Mathf.Abs(visualGo.transform.localPosition.y) < 0.0001f,
                       "Idle 이면 Visual 이 원위치로 내려와야 합니다.");
                passedCount++;

                // 7. 4종 크리처 속도 파싱 검증
                // 속도 숫자를 여기 복제하지 않는다 — CSV 사본끼리 비교하면 늘 통과한다.
                // 대신 파싱이 됐는지와 **설계상의 대소 관계**를 본다 (GDD 4절: 거치형이 가장 느리고
                // 고속형이 가장 빠르다). 값을 조정해도 이 관계가 깨지면 그건 진짜 문제다.
                var types = new[] { "normal", "anchor", "runner", "tourist" };
                foreach (var id in types)
                {
                    var def = balance.GetTarget(id);
                    Assert(def != null, id + " 정의가 targets.csv 에 없습니다.");
                    Assert(def.MoveSpeed > 0f, id + " 의 move_speed 를 읽지 못했습니다: " + def.MoveSpeed);
                }
                var anchorSpeed = balance.GetTarget("anchor").MoveSpeed;
                var runnerSpeed = balance.GetTarget("runner").MoveSpeed;
                Assert(anchorSpeed < balance.GetTarget("normal").MoveSpeed,
                       "거치형이 일반형보다 느려야 합니다.");
                Assert(runnerSpeed > balance.GetTarget("normal").MoveSpeed,
                       "고속형이 일반형보다 빨라야 합니다.");
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

                // 기대값은 코드에 적지 않고 생성된 BalanceData 에서 읽는다 (AGENTS.md, #131).
                var stage1 = balance.GetStage(1);
                Assert(stage1 != null, "stages.csv 에 1단계가 없습니다.");
                var baseSpawnCount = stage1.SpawnCount;
                var baseChance = balance.Economy.ExtraSpawnChanceOnDestroy;

                // 주입 전에는 기준값 그대로다.
                var spawnCount = mgr.GetRequiredSpawnCount();
                Assert(spawnCount == baseSpawnCount,
                       "1단계 기본 spawn_count 가 stages.csv 와 다릅니다. 현재: " + spawnCount);
                Assert(Mathf.Abs(mgr.GetExtraSpawnChancePercent() - baseChance) < 0.01f,
                       "기본 추가 생성 확률이 economy.csv 와 다릅니다.");
                passedCount++;

                // 업그레이드를 주입하면 실효값으로 바뀐다 (#131). 어느 업그레이드가 어느 stat 을
                // 건드리는지도 CSV 에서 찾으므로 종류가 바뀌어도 검증이 따라간다.
                var upgrades = CreateUpgradeStats(balance, StatId.SpawnCount, 2);
                mgr.SetUpgradeStats(upgrades);

                var raisedCount = mgr.GetRequiredSpawnCount();
                Assert(raisedCount == Mathf.RoundToInt(upgrades.GetStat(StatId.SpawnCount, baseSpawnCount)),
                       "업그레이드 반영 후 spawn_count 가 실효값과 다릅니다: " + raisedCount);
                Assert(raisedCount > baseSpawnCount,
                       "업그레이드를 넣었는데 동시 출현 수가 늘지 않았습니다: " + raisedCount);
                passedCount++;

                // 같은 업그레이드(저금통 수집벽)가 추가 생성 확률도 올린다 (add 효과).
                var raisedChance = mgr.GetExtraSpawnChancePercent();
                Assert(Mathf.Abs(raisedChance - upgrades.GetStat(StatId.ExtraSpawnChance, baseChance)) < 0.01f,
                       "추가 생성 확률이 실효값과 다릅니다: " + raisedChance);
                Assert(raisedChance > baseChance,
                       "업그레이드를 넣었는데 추가 생성 확률이 늘지 않았습니다: " + raisedChance);
                passedCount++;

                // 9. stageDef 가 없는 경로에서 비율 폴백 하드코딩 없이 안전하게 null 반환 검증 (#148)
                mgr.InitializeStage(9999);
                Assert(mgr.GetRequiredSpawnCount() == 0,
                       "없는 단계에서는 spawn_count 가 0 이어야 합니다.");
                var spawnedInvalid = mgr.SpawnRandomCreature();
                Assert(spawnedInvalid == null,
                       "stageDef 가 없을 때는 크리처를 스폰하지 않아야 합니다 (#148).");
                passedCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mgrGo);
            }

            // 12. 스폰 회계 검증 (#141, #156 B안)
            //
            // 부서진 자리는 자동으로 채워지지 않는다. 기본 확률(0%)에서는 일부만 부숴도
            // 그 자리가 빈 채로 남아야 하고, 목록에 없는 대상의 파괴 이벤트(유령)는 여전히
            // 아무 것도 하면 안 된다 (#141). 전부 부쉈을 때만(0마리) 1개가 즉시 채워진다.
            //
            // 실물 프리팹 대신 스텁을 쓴다. Instantiate 는 클론을 **지금 열려 있는 씬에** 만들어
            // 남의 씬을 더럽히므로, HideAndDontSave 스텁을 복제해 흔적을 남기지 않는다.
            var respawnGo = new GameObject("RespawnCountCheck");
            respawnGo.hideFlags = HideFlags.HideAndDontSave;
            var stubPrefab = new GameObject("RespawnCheckStub");
            stubPrefab.hideFlags = HideFlags.HideAndDontSave;
            var stubTarget = stubPrefab.AddComponent<Target>();
            try
            {
                // 스텁도 targets.csv 를 읽어야 내구도가 잡힌다. id 는 코드에 박지 않고 CSV 에서 고른다.
                Assert(balance.Targets.Count > 0, "targets.csv 에서 읽은 대상이 없습니다.");
                var stubSerialized = new SerializedObject(stubTarget);
                stubSerialized.FindProperty("_targetId").stringValue = balance.Targets[0].Id;
                stubSerialized.FindProperty("_balanceData").objectReferenceValue = balance;
                stubSerialized.ApplyModifiedPropertiesWithoutUndo();

                var mgr = respawnGo.AddComponent<CreatureManager>();
                var serialized = new SerializedObject(mgr);
                serialized.FindProperty("_balanceData").objectReferenceValue = balance;
                // 모든 종류를 같은 스텁 프리팹으로 연결한다 (#293 target_id 키 목록).
                var entries = serialized.FindProperty("_targetPrefabs");
                entries.arraySize = balance.Targets.Count;
                for (var i = 0; i < balance.Targets.Count; i++)
                {
                    var entry = entries.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("_targetId").stringValue = balance.Targets[i].Id;
                    entry.FindPropertyRelative("_prefab").objectReferenceValue = stubPrefab;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();

                // 이벤트 구독은 OnEnable 쌍으로 걸린다. 에디터에서 만든 컴포넌트는 직접 부른다.
                InvokeLifecycle(mgr, "OnEnable");

                var required = mgr.GetRequiredSpawnCount();
                Assert(required >= 2, "검증하려면 목표 동시 출현 수가 2 이상이어야 합니다: " + required);
                Assert(Mathf.Approximately(mgr.GetExtraSpawnChancePercent(), 0f),
                       "이 검증은 추가 생성 확률 0% 를 전제합니다 — economy.csv 기본값을 확인하세요.");

                mgr.InitializeStage(1);
                Assert(mgr.ActiveCreatures.Count == required,
                       "초기 배치 수가 목표치와 다릅니다: " + mgr.ActiveCreatures.Count + " / " + required);
                passedCount++;

                // 확률 0% 에서는 하나를 부숴도 그 자리가 빈 채로 남는다 — 자동으로 채워지지 않는다.
                var victimGo = mgr.ActiveCreatures[0];
                var victim = victimGo.GetComponent<Target>();
                Assert(victim.IsAlive, "스폰된 대상이 살아 있지 않습니다. 스텁 초기화를 확인하세요.");
                victim.OnHit(new HitInfo(HitSource.Hover, victim.MaxHp, Vector3.zero));
                Assert(victimGo == null, "파괴된 크리처 GameObject가 즉시 파괴되지 않고 씬에 남아 있습니다 (#161).");
                Assert(mgr.ActiveCreatures.Count == required - 1,
                       "확률 0% 인데 부순 자리가 자동으로 채워졌습니다: " + mgr.ActiveCreatures.Count);
                passedCount++;

                // 목록에 없는 대상의 파괴 이벤트(유령)는 아무 것도 하지 않는다.
                var beforeGhost = mgr.ActiveCreatures.Count;
                GameEvents.PublishTargetBroken(new BreakInfo("ghost", 1m, Array.Empty<CoinDrop>(), 0f, Vector3.zero));
                GameEvents.PublishTargetBroken(new BreakInfo("ghost", 1m, Array.Empty<CoinDrop>(), 0f, Vector3.zero));
                Assert(mgr.ActiveCreatures.Count == beforeGhost,
                       "치운 것이 없는데 필드 개체 수가 바뀌었습니다: " + mgr.ActiveCreatures.Count + " (#141)");
                passedCount++;

                // 남은 것을 전부 부수면(0마리) 그때만 1개가 즉시 채워진다.
                // 마지막 한 마리를 부수는 순간 필드가 다시 1로 차오르므로, 반복 횟수는
                // "지금 남은 수"로 미리 고정한다 — count > 0 로 돌리면 방금 채워진 1마리를
                // 또 부수는 무한 루프가 된다.
                var remaining = mgr.ActiveCreatures.Count;
                for (var i = 0; i < remaining; i++)
                {
                    var lastGo = mgr.ActiveCreatures[0];
                    var last = lastGo.GetComponent<Target>();
                    last.OnHit(new HitInfo(HitSource.Hover, last.MaxHp, Vector3.zero));
                }
                Assert(mgr.ActiveCreatures.Count == 1,
                       "전멸 후에는 정확히 1마리만 즉시 채워져야 합니다: " + mgr.ActiveCreatures.Count);
                passedCount++;

                InvokeLifecycle(mgr, "OnDisable");
                foreach (var creature in new System.Collections.Generic.List<GameObject>(mgr.ActiveCreatures))
                {
                    if (creature != null)
                    {
                        UnityEngine.Object.DestroyImmediate(creature);
                    }
                }
            }
            finally
            {
                // 스텁 클론은 hideFlags 를 물려받아 HideAndDontSave 가 된다. DontSave 는
                // **씬을 새로 로드해도 파괴되지 않는다** — 검증을 돌릴 때마다 클론이 쌓이고,
                // 플레이 모드로 들어가도 그대로 살아남아 화면에 보라색(머티리얼 없음) 사각형으로
                // 그려졌다. 하이어라키에는 Hide 라 안 보이니 원인을 찾기도 어렵다.
                // ActiveCreatures 목록에만 기대지 말고 이름으로 훑어 확실히 지운다.
                foreach (var leftover in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (leftover != null && leftover.name.StartsWith("RespawnCheckStub"))
                    {
                        UnityEngine.Object.DestroyImmediate(leftover);
                    }
                }
                UnityEngine.Object.DestroyImmediate(respawnGo);
            }

            // 13. 크리처 HP 표시 및 데미지 팝업 생성 검증
            var testTargetGo = new GameObject("HpDisplayTestTarget");
            try
            {
                var target = testTargetGo.AddComponent<Target>();
                var hpDisplay = testTargetGo.AddComponent<CreatureHpDisplay>();
                Assert(hpDisplay != null, "CreatureHpDisplay 컴포넌트 생성 실패");
                passedCount++;

                var popup = DamagePopup.Spawn(Vector3.zero, 1.5f);
                Assert(popup != null, "DamagePopup 생성 실패");
                popup.Despawn();
                passedCount++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(testTargetGo);
            }

            Debug.Log("[CreatureMovementChecks] 전체 " + passedCount + "개 검증 통과 완료.");
        }

        /// <summary>에디터에서 만든 컴포넌트는 OnEnable/OnDisable 이 자동으로 불리지 않아 직접 부른다.</summary>
        private static void InvokeLifecycle(Component component, string methodName)
        {
            var method = component.GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert(method != null, component.GetType().Name + " 의 " + methodName + " 을 찾지 못했습니다.");
            method.Invoke(component, null);
        }

        /// <summary>
        /// 해당 stat 을 올리는 업그레이드를 CSV 에서 찾아 레벨을 올린 조회 통로를 만든다.
        /// UpgradeState 는 계산부라 IUpgradeStats 를 구현하지 않으므로 여기서 감싼다.
        /// </summary>
        private static IUpgradeStats CreateUpgradeStats(BalanceData balance, StatId stat, int wantedLevel)
        {
            var state = new UpgradeState(balance);
            var levels = new int[balance.Upgrades.Count];
            var ownerIndex = -1;
            for (var i = 0; i < balance.Upgrades.Count && ownerIndex < 0; i++)
            {
                foreach (var effect in balance.Upgrades[i].Effects)
                {
                    if (effect.Stat == stat)
                    {
                        ownerIndex = i;
                        break;
                    }
                }
            }
            Assert(ownerIndex >= 0, "upgrade_effects.csv 에 " + stat + " 를 올리는 업그레이드가 없습니다.");

            levels[ownerIndex] = Mathf.Min(wantedLevel, balance.Upgrades[ownerIndex].MaxLevel);
            Assert(levels[ownerIndex] >= 1, balance.Upgrades[ownerIndex].Id + " 의 max_level 이 0 입니다.");
            state.RestoreLevels(levels);
            return new UpgradeStatsAdapter(state);
        }

        private class UpgradeStatsAdapter : IUpgradeStats
        {
            private readonly UpgradeState _state;

            public UpgradeStatsAdapter(UpgradeState state)
            {
                _state = state;
            }

            public float GetStat(StatId stat, float baseValue)
            {
                return _state.GetStat(stat, baseValue);
            }
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
