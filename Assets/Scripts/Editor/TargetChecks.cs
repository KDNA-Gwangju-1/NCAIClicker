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
    /// 그레이박스 타격 대상 4종 프리팹의 구조와 피격 동작을 검증한다.
    ///
    /// 한계: Edit Mode 에서는 Unity 가 Awake 를 부르지 않으므로 Initialize() 를 직접 부른다.
    /// 이 메서드는 스폰·풀 재사용에서도 쓰라고 공개해 둔 것이라 리플렉션이 필요 없다.
    /// </summary>
    public static class TargetChecks
    {
        private const string PrefabDir = "Assets/Prefabs/Targets/";

        /// <summary>그레이박스 통일 높이. 카메라 구도(#14)에 맞춰 정한 값이다.</summary>
        private const float GreyboxHeight = 0.4f;

        private static readonly Dictionary<string, string> _prefabs = new Dictionary<string, string>
        {
            { "TargetNormal", "normal" },
            { "TargetAnchor", "anchor" },
            { "TargetRunner", "runner" },
            { "TargetTourist", "tourist" },
        };

        [MenuItem("NCAI/타격 대상 프리팹 검증")]
        public static void RunBatch()
        {
            var checkCount = 0;
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            AssertCondition(balance != null, "BalanceData 에셋을 찾지 못했습니다.");

            var spawned = new List<GameObject>();
            var broken = new List<BreakInfo>();
            Action<BreakInfo> onBroken = info => broken.Add(info);

            try
            {
                GameEvents.OnTargetBroken += onBroken;

                foreach (var pair in _prefabs)
                {
                    var path = PrefabDir + pair.Key + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    AssertCondition(prefab != null, "프리팹이 없습니다: " + path);

                    // --- 구조: 로직은 루트, 메시는 Visual 자식 (AGENTS.md) ---
                    var target = prefab.GetComponent<Target>();
                    AssertCondition(target != null, pair.Key + ": 루트에 Target 이 없습니다.");
                    AssertCondition(target.TargetId == pair.Value,
                        pair.Key + ": _targetId 가 '" + target.TargetId + "' 입니다. '" + pair.Value + "' 여야 합니다.");
                    AssertCondition(prefab.GetComponent<SphereCollider>() != null,
                        pair.Key + ": 루트에 SphereCollider 가 없습니다.");

                    // ASSET_PIPELINE 1절: Visual 은 빈 GameObject, 그 아래 Mesh 자식.
                    // 에셋 교체(작업 6.6)가 Mesh 하나만 갈아끼우면 끝나야 한다.
                    var visual = prefab.transform.Find("Visual");
                    AssertCondition(visual != null, pair.Key + ": Visual 자식이 없습니다.");
                    AssertCondition(visual.GetComponent<Renderer>() == null,
                        pair.Key + ": Visual 이 직접 메시를 들고 있습니다. 비어 있어야 합니다.");
                    AssertCondition(visual.localScale == Vector3.one,
                        pair.Key + ": Visual 의 스케일이 1 이 아닙니다. 연출이 여기를 스케일합니다.");

                    var meshChild = visual.Find("Mesh");
                    AssertCondition(meshChild != null, pair.Key + ": Visual 아래 Mesh 자식이 없습니다.");
                    AssertCondition(meshChild.GetComponent<Renderer>() != null,
                        pair.Key + ": Mesh 에 Renderer 가 없습니다.");
                    AssertCondition(visual.GetComponentInChildren<Collider>() == null,
                        pair.Key + ": Visual 아래에 콜라이더가 남아 있습니다. 피격 판정은 루트가 단독으로 갖습니다.");
                    checkCount++;

                    // --- 인스턴스 동작 ---
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instance.hideFlags = HideFlags.HideAndDontSave;
                    spawned.Add(instance);

                    var live = instance.GetComponent<Target>();
                    live.Initialize();
                    AssertCondition(live.IsAlive, pair.Key + ": 초기화 후 살아 있지 않습니다.");

                    var def = balance.GetTarget(pair.Value);
                    AssertCondition(def != null, "targets.csv 에 '" + pair.Value + "' 가 없습니다.");

                    // 그레이박스 높이가 합의한 값인지 — 카메라 구도와 직결된다.
                    var meshRenderer = live.GetComponentInChildren<Renderer>();
                    var bounds = meshRenderer.bounds;
                    AssertCondition(Mathf.Abs(bounds.size.y - GreyboxHeight) < 0.01f,
                        pair.Key + ": 높이가 " + bounds.size.y.ToString("0.###") + " 입니다. " + GreyboxHeight + " 여야 합니다.");
                    AssertCondition(Mathf.Abs(bounds.min.y) < 0.01f,
                        pair.Key + ": 바닥이 y=" + bounds.min.y.ToString("0.###") + " 입니다. 0 에 닿아야 합니다.");
                    checkCount++;

                    // 피격 반경은 hit_radius_bonus 만큼 넓다 (GDD 4절).
                    // 그리고 **메시 크기를 바꿔도 변하지 않아야 한다** — 모델이 바뀌어도 판정은 그대로여야
                    // 밸런스가 유지된다 (ASSET_PIPELINE 1절). 작업 6.6 교체가 밸런스를 흔들지 않게 하는 가드다.
                    var hitCollider = live.GetComponent<SphereCollider>();
                    var radiusBefore = hitCollider.radius;
                    AssertCondition(radiusBefore > 0f, pair.Key + ": 피격 반경이 0 입니다.");

                    var liveMesh = live.transform.Find("Visual/Mesh");
                    liveMesh.localScale *= 2f;
                    live.Initialize();
                    AssertCondition(Mathf.Abs(hitCollider.radius - radiusBefore) < 0.0001f,
                        pair.Key + ": 메시를 키웠더니 판정 반경이 " + hitCollider.radius.ToString("0.####") +
                        " 로 따라 변했습니다. 모델과 무관해야 합니다.");
                    liveMesh.localScale *= 0.5f;
                    live.Initialize();
                    checkCount++;

                    // --- 내구도를 다 깎기 전에는 부서지지 않는다 ---
                    broken.Clear();
                    var hitsToKill = def.Hp;
                    for (var i = 0; i < hitsToKill - 1; i++)
                    {
                        live.OnHit(new HitInfo(HitSource.Hover, 1f, Vector3.zero));
                    }
                    AssertCondition(broken.Count == 0,
                        pair.Key + ": 내구도가 남았는데 부서졌습니다. " + (hitsToKill - 1) + "타 후 " + broken.Count + "건 발행.");
                    AssertCondition(live.IsAlive, pair.Key + ": 내구도가 남았는데 죽었습니다.");
                    checkCount++;

                    // --- 마지막 타격에 정확히 한 번 발행한다 (계약 2번) ---
                    live.OnHit(new HitInfo(HitSource.Hover, 1f, Vector3.zero));
                    AssertCondition(broken.Count == 1,
                        pair.Key + ": OnTargetBroken 이 " + broken.Count + "번 발행됐습니다. 1번이어야 합니다.");
                    AssertCondition(!live.IsAlive, pair.Key + ": 부서졌는데 살아 있다고 합니다.");

                    var info = broken[0];
                    AssertCondition(info.TargetId == pair.Value, pair.Key + ": BreakInfo.TargetId 가 다릅니다.");

                    // 원시 보상은 남은 내구도가 아니라 초기 최대 내구도로 계산한다.
                    var expectedCoin = def.Hp * (decimal)def.CoinMult + def.BreakBonus;
                    AssertCondition(info.RawCoin == expectedCoin,
                        pair.Key + ": RawCoin 이 " + info.RawCoin + " 입니다. " + expectedCoin + " 여야 합니다.");
                    AssertCondition(Mathf.Abs(info.StaminaRestore - def.StaminaRestore) < 0.001f,
                        pair.Key + ": StaminaRestore 가 다릅니다.");
                    checkCount++;

                    // --- 죽은 뒤 더 때려도 다시 발행하지 않는다 ---
                    live.OnHit(new HitInfo(HitSource.AutoHammer, 99f, Vector3.zero));
                    AssertCondition(broken.Count == 1,
                        pair.Key + ": 부서진 뒤 추가 타격으로 코인이 또 나왔습니다.");
                    checkCount++;
                }

                // 회복형만 스태미나를 돌려준다 — GDD 가 핵심 장치라고 부른 부분이다.
                var tourist = balance.GetTarget("tourist");
                AssertCondition(tourist.StaminaRestore > 0f, "회복형의 stamina_restore 가 0 입니다.");
                foreach (var id in new[] { "normal", "anchor", "runner" })
                {
                    AssertCondition(balance.GetTarget(id).StaminaRestore == 0f,
                        id + ": 회복형이 아닌데 stamina_restore 가 0 이 아닙니다.");
                }
                checkCount++;

                Debug.Log("[TargetChecks] PASS " + checkCount + " checks.");
            }
            finally
            {
                GameEvents.OnTargetBroken -= onBroken;
                foreach (var go in spawned)
                {
                    if (go != null)
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                    }
                }
            }
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
