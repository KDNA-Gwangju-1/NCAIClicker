using System;
using System.Collections.Generic;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Targets;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 화난 저금통의 분노·돌진 규칙을 검증한다 (#297).
    /// 실제 수치는 #247 에서 정하므로, 산출물을 복제해 한 종류에만 돌진 값을 넣고 규칙만 본다.
    /// </summary>
    public static class ChargeChecks
    {
        private const string PrefabDir = "Assets/Prefabs/Targets/";
        private const float TestChargeSpeed = 5f;
        private const float TestChargeRatio = 0.7f;

        public static void RunBatch()
        {
            var checkCount = 0;
            var source = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            Assert(source != null, "BalanceData 에셋을 찾지 못했습니다.");
            Assert(Enum.IsDefined(typeof(HitSource), HitSource.Charge), "HitSource.Charge 정의 누락");
            Assert(Enum.IsDefined(typeof(CreatureState), CreatureState.Charging), "CreatureState.Charging 정의 누락");
            checkCount++;

            var balance = UnityEngine.Object.Instantiate(source);
            var angryDef = balance.GetTarget("anchor");
            Assert(angryDef != null, "targets.csv 에 anchor 가 없습니다.");
            angryDef.ChargeSpeed = TestChargeSpeed;
            angryDef.ChargeDamageRatio = TestChargeRatio;

            var spawned = new List<GameObject>();
            var swingEvents = 0;
            Action<HitSource, bool> onSwing = (s, hit) => swingEvents++;
            try
            {
                GameEvents.OnSwingResolved += onSwing;
                var bounds = new Bounds(new Vector3(0f, 0f, 2f), new Vector3(6f, 1f, 6f));

                var angry = Spawn("TargetAnchor", balance, new Vector3(-2f, 0f, 2f), bounds, spawned);
                var victim = Spawn("TargetNormal", balance, new Vector3(2f, 0f, 2f), bounds, spawned);
                var angryMove = angry.GetComponent<CreatureMovement>();
                var victimTarget = victim.GetComponent<Target>();

                // 1. 돌진에 맞아서는 분노하지 않는다 (연쇄 방지)
                angry.GetComponent<Target>().OnHit(new HitInfo(HitSource.Charge, 0.1f, Vector3.zero));
                Assert(!angryMove.IsAngry, "Charge 타격으로 분노하면 안 됩니다.");
                checkCount++;

                // 2. 호버 타격으로 분노하고, 돌진 피해는 그 피해 × 비율이다
                const float hoverDamage = 2f;
                angry.GetComponent<Target>().OnHit(new HitInfo(HitSource.Hover, hoverDamage, angry.transform.position));
                Assert(angryMove.IsAngry, "호버 타격 후 분노 상태여야 합니다.");
                Assert(Mathf.Abs(angryMove.ChargeDamage - hoverDamage * TestChargeRatio) < 0.0001f,
                       "돌진 피해가 " + angryMove.ChargeDamage + " 입니다. " + hoverDamage * TestChargeRatio + " 여야 합니다.");
                checkCount++;

                // 3. 경직이 끝나면 다른 저금통으로 돌진하고, 부딪히면 Charge 피해를 준다
                var received = new List<HitInfo>();
                Action<HitInfo> onVictimHit = info => received.Add(info);
                victimTarget.HitReceived += onVictimHit;
                try
                {
                    angryMove.UpdateFSM(0.5f);
                    Assert(angryMove.CurrentState == CreatureState.Charging,
                           "경직 후 Charging 이어야 합니다: " + angryMove.CurrentState);
                    Assert(angryMove.ChargeTarget == victimTarget, "가장 가까운 다른 저금통을 노려야 합니다.");
                    for (var i = 0; i < 200 && received.Count == 0; i++)
                    {
                        angryMove.UpdateFSM(0.02f);
                    }
                }
                finally
                {
                    victimTarget.HitReceived -= onVictimHit;
                }
                Assert(received.Count == 1, "돌진 충돌 피해가 " + received.Count + "회 들어왔습니다. 1회여야 합니다.");
                Assert(received[0].Source == HitSource.Charge, "돌진 피해의 발신원이 Charge 가 아닙니다.");
                Assert(Mathf.Abs(received[0].Damage - hoverDamage * TestChargeRatio) < 0.0001f,
                       "돌진 피해량이 " + received[0].Damage + " 입니다.");
                checkCount++;

                // 4. 돌진은 호버 전용 집계(피버·정확도·자동 망치 발동)가 듣는 스윙 이벤트를 내지 않는다
                Assert(swingEvents == 0, "돌진 중 OnSwingResolved 가 " + swingEvents + "회 발행됐습니다.");
                checkCount++;

                // 5. 돌진하지 않는 종류는 맞아도 분노하지 않는다
                var normalMove = victim.GetComponent<CreatureMovement>();
                victimTarget.OnHit(new HitInfo(HitSource.Hover, 0.1f, victim.transform.position));
                Assert(!normalMove.IsAngry, "charge_speed 0 인 종류가 분노했습니다.");
                checkCount++;

                // 6. 분노한 저금통끼리 부딪히면 서로 피해를 준다
                var angry2 = Spawn("TargetAnchor", balance, angry.transform.position + new Vector3(0.3f, 0f, 0f), bounds, spawned);
                angry2.GetComponent<Target>().OnHit(new HitInfo(HitSource.Hover, hoverDamage, angry2.transform.position));
                var selfHits = new List<HitInfo>();
                Action<HitInfo> onSelfHit = info => selfHits.Add(info);
                var angryTarget = angry.GetComponent<Target>();
                angryTarget.HitReceived += onSelfHit;
                try
                {
                    angryMove.ChangeState(CreatureState.BeingHit);
                    for (var i = 0; i < 200 && selfHits.Count == 0; i++)
                    {
                        angryMove.UpdateFSM(0.02f);
                    }
                }
                finally
                {
                    angryTarget.HitReceived -= onSelfHit;
                }
                Assert(selfHits.Count >= 1 && selfHits[0].Source == HitSource.Charge,
                       "분노한 상대와 부딪혔는데 반격 피해를 받지 않았습니다.");
                checkCount++;
            }
            finally
            {
                GameEvents.OnSwingResolved -= onSwing;
                foreach (var go in spawned)
                {
                    if (go != null)
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                    }
                }
                UnityEngine.Object.DestroyImmediate(balance);
            }

            Debug.Log("[ChargeChecks] PASS " + checkCount + " checks.");
        }

        private static GameObject Spawn(string prefabName, BalanceData balance, Vector3 position, Bounds bounds,
                                        List<GameObject> spawned)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + prefabName + ".prefab");
            Assert(prefab != null, "프리팹이 없습니다: " + prefabName);
            var go = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            go.hideFlags = HideFlags.HideAndDontSave;
            spawned.Add(go);

            var target = go.GetComponent<Target>();
            var serialized = new SerializedObject(target);
            serialized.FindProperty("_balanceData").objectReferenceValue = balance;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            target.Initialize();

            var movement = go.GetComponent<CreatureMovement>();
            if (movement == null)
            {
                movement = go.AddComponent<CreatureMovement>();
            }
            // 에디터에서 만든 컴포넌트는 Awake·OnEnable 이 돌지 않아 직접 부른다 (구독 쌍 포함).
            InvokeLifecycle(movement, "Awake");
            InvokeLifecycle(movement, "OnEnable");
            movement.Initialize(balance, target.TargetId, bounds, others: spawned);
            return go;
        }

        private static void InvokeLifecycle(Component component, string methodName)
        {
            var method = component.GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert(method != null, component.GetType().Name + " 의 " + methodName + " 을 찾지 못했습니다.");
            method.Invoke(component, null);
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
