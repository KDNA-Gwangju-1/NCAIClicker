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
                var baseInterval = balance.Economy.SpawnIntervalSec;

                // 주입 전에는 기준값 그대로다.
                var spawnCount = mgr.GetRequiredSpawnCount();
                Assert(spawnCount == baseSpawnCount,
                       "1단계 기본 spawn_count 가 stages.csv 와 다릅니다. 현재: " + spawnCount);
                Assert(Mathf.Abs(mgr.GetSpawnIntervalSec() - baseInterval) < 0.01f,
                       "기본 재등장 대기시간이 economy.csv 와 다릅니다.");
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

                // 같은 업그레이드가 재등장 대기를 줄인다 (percent 효과, 음수).
                var shortened = mgr.GetSpawnIntervalSec();
                Assert(Mathf.Abs(shortened - upgrades.GetStat(StatId.SpawnIntervalSec, baseInterval)) < 0.01f,
                       "재등장 대기시간이 실효값과 다릅니다: " + shortened);
                Assert(shortened < baseInterval,
                       "업그레이드를 넣었는데 재등장 대기가 줄지 않았습니다: " + shortened);
                passedCount++;

                // 리스폰 타이머 처리 검증
                mgr.UpdateRespawnTimers(1.0f);
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

            // 12. 리스폰 회계 검증 (#141)
            //
            // 치운 개수와 예약 개수가 어긋나면 필드 개체 수가 목표치에서 벗어난다.
            // 특히 목록에 없는 대상의 파괴 이벤트가 들어오면 치운 것이 없는데도 예약만 쌓여
            // 목표치를 넘겨 스폰됐다. 밸런스 시뮬레이터는 슬롯 수가 고정이라고 가정한다.
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
                foreach (var field in new[] { "_targetNormalPrefab", "_targetAnchorPrefab",
                                              "_targetRunnerPrefab", "_targetTouristPrefab" })
                {
                    serialized.FindProperty(field).objectReferenceValue = stubPrefab;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();

                // 이벤트 구독은 OnEnable 쌍으로 걸린다. 에디터에서 만든 컴포넌트는 직접 부른다.
                InvokeLifecycle(mgr, "OnEnable");
                var timers = (System.Collections.Generic.List<float>)GetPrivateField(mgr, "_respawnTimers");

                var required = mgr.GetRequiredSpawnCount();
                Assert(required >= 2, "검증하려면 목표 동시 출현 수가 2 이상이어야 합니다: " + required);

                mgr.InitializeStage(1);
                Assert(mgr.ActiveCreatures.Count == required,
                       "초기 배치 수가 목표치와 다릅니다: " + mgr.ActiveCreatures.Count + " / " + required);
                passedCount++;

                // 정상 파괴: 치운 수만큼 예약된다. 필드 + 예약의 합이 항상 목표치여야 한다.
                for (var i = 0; i < 2; i++)
                {
                    var victimGo = mgr.ActiveCreatures[0];
                    var victim = victimGo.GetComponent<Target>();
                    Assert(victim.IsAlive, "스폰된 대상이 살아 있지 않습니다. 스텁 초기화를 확인하세요.");
                    victim.OnHit(new HitInfo(HitSource.Hover, victim.MaxHp, Vector3.zero));
                    Assert(victimGo == null, "파괴된 크리처 GameObject가 즉시 파괴되지 않고 씬에 남아 있습니다 (#161).");
                }
                Assert(mgr.ActiveCreatures.Count + timers.Count == required,
                       "파괴 후 필드+예약 합이 목표치와 다릅니다: " + mgr.ActiveCreatures.Count
                       + " + " + timers.Count + " / " + required);
                passedCount++;

                // 대기 시간이 지나면 목표치로 돌아온다. 기대값은 CSV 에서 읽은 대기 시간이다.
                mgr.UpdateRespawnTimers(mgr.GetSpawnIntervalSec() + 0.01f);
                Assert(mgr.ActiveCreatures.Count == required,
                       "대기 후 개체 수가 목표치로 돌아오지 않았습니다: "
                       + mgr.ActiveCreatures.Count + " / " + required);
                passedCount++;

                // 목록에 없는 대상의 파괴 이벤트(유령)는 예약을 만들지 않는다.
                GameEvents.PublishTargetBroken(new BreakInfo("ghost", 1m, 0f, Vector3.zero));
                GameEvents.PublishTargetBroken(new BreakInfo("ghost", 1m, 0f, Vector3.zero));
                Assert(timers.Count == 0,
                       "치운 것이 없는데 재등장이 예약됐습니다: " + timers.Count + " (#141)");
                passedCount++;

                // 설령 예약이 남아 있어도 목표치를 넘겨 스폰하지 않는다.
                timers.Add(0f);
                timers.Add(0f);
                mgr.UpdateRespawnTimers(0.01f);
                Assert(mgr.ActiveCreatures.Count == required,
                       "목표치를 넘겨 스폰했습니다: " + mgr.ActiveCreatures.Count + " / " + required + " (#141)");
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

                var popup = DamagePopup.Create(Vector3.zero, 1.5f);
                Assert(popup != null, "DamagePopup 생성 실패");
                UnityEngine.Object.DestroyImmediate(popup.gameObject);
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

        /// <summary>검증에서만 내부 상태를 들여다본다. 이 때문에 필드를 public 으로 열지 않는다.</summary>
        private static object GetPrivateField(Component component, string fieldName)
        {
            var field = component.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert(field != null, component.GetType().Name + " 의 " + fieldName + " 필드를 찾지 못했습니다.");
            return field.GetValue(component);
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
