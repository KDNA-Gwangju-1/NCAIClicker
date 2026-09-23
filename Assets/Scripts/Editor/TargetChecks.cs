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

        /// <summary>
        /// 타격 대상 통일 높이 (ASSET_PIPELINE 2절). 카메라 구도와 조준 원(지름 0.9유닛) 대비 크기로 정했다.
        /// 그레이박스 시절 0.4 에서 광물 크리처 교체(6.6) 때 0.8 로 올렸다 (#287 에서 검증도 맞춤).
        /// 6.26 씬 배치(#239)에서 이동 공간이 좁다는 피드백으로 0.65 로 다시 낮췄다.
        /// </summary>
        private const float TargetHeight = 0.65f;

        private static readonly Dictionary<string, string> _prefabs = new Dictionary<string, string>
        {
            { "TargetNormal", "normal" },
            { "TargetAnchor", "anchor" },
            { "TargetRunner", "runner" },
            { "TargetTourist", "tourist" },
            { "TargetPinata", "pinata" },
            { "TargetAngry", "angry" },
        };

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

                    // ASSET_PIPELINE 1절: Visual 은 빈 GameObject, 그 아래 Renderer 를 가진 모델 자식.
                    // 자식 이름은 모델명이라 고정하지 않는다 (#287) — 에셋 교체가 자식 하나만 갈아끼우면 끝나야 한다.
                    var visual = prefab.transform.Find("Visual");
                    AssertCondition(visual != null, pair.Key + ": Visual 자식이 없습니다.");
                    AssertCondition(visual.GetComponent<Renderer>() == null,
                        pair.Key + ": Visual 이 직접 메시를 들고 있습니다. 비어 있어야 합니다.");
                    AssertCondition(visual.localScale == Vector3.one,
                        pair.Key + ": Visual 의 스케일이 1 이 아닙니다. 연출이 여기를 스케일합니다.");

                    AssertCondition(visual.childCount > 0, pair.Key + ": Visual 아래 모델 자식이 없습니다.");
                    AssertCondition(visual.GetComponentInChildren<Renderer>() != null,
                        pair.Key + ": Visual 아래에 Renderer 를 가진 자식이 없습니다.");
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

                    // 높이가 합의한 값인지 — 카메라 구도와 직결된다.
                    var meshRenderer = live.GetComponentInChildren<Renderer>();
                    var bounds = meshRenderer.bounds;
                    AssertCondition(Mathf.Abs(bounds.size.y - TargetHeight) < 0.01f,
                        pair.Key + ": 높이가 " + bounds.size.y.ToString("0.###") + " 입니다. " + TargetHeight + " 여야 합니다.");
                    AssertCondition(Mathf.Abs(bounds.min.y) < 0.01f,
                        pair.Key + ": 바닥이 y=" + bounds.min.y.ToString("0.###") + " 입니다. 0 에 닿아야 합니다.");
                    checkCount++;

                    // 피격 반경은 hit_radius_bonus 만큼 넓다 (GDD 4절).
                    // 그리고 **메시 크기를 바꿔도 변하지 않아야 한다** — 모델이 바뀌어도 판정은 그대로여야
                    // 밸런스가 유지된다 (ASSET_PIPELINE 1절). 작업 6.6 교체가 밸런스를 흔들지 않게 하는 가드다.
                    var hitCollider = live.GetComponent<SphereCollider>();
                    var radiusBefore = hitCollider.radius;
                    AssertCondition(radiusBefore > 0f, pair.Key + ": 피격 반경이 0 입니다.");

                    var liveMesh = live.transform.Find("Visual").GetChild(0);
                    liveMesh.localScale *= 2f;
                    live.Initialize();
                    AssertCondition(Mathf.Abs(hitCollider.radius - radiusBefore) < 0.0001f,
                        pair.Key + ": 메시를 키웠더니 판정 반경이 " + hitCollider.radius.ToString("0.####") +
                        " 로 따라 변했습니다. 모델과 무관해야 합니다.");
                    liveMesh.localScale *= 0.5f;
                    live.Initialize();
                    checkCount++;

                    // --- 내구도를 다 깎기 전에는 부서지지 않는다 및 HitReceived 이벤트 검증 (#148) ---
                    // 즉시 파괴(#293)는 확률이라 이 구간에서만 메모리 값을 0 으로 둔다. 에셋에는 저장하지 않는다.
                    var savedInstantBreak = def.InstantBreakChance;
                    def.InstantBreakChance = 0f;

                    // 최대 액면(#330)이 있는 종류는 그보다 큰 액면의 가중치를 메모리에서만 크게 올린다 — Target 이
                    // max 를 넘기지 않으면 거의 확실히 그 액면이 나와 아래 검사에 걸린다. 에셋에는 저장하지 않는다.
                    var maxDenom = string.IsNullOrEmpty(def.MaxDenomId) ? null : balance.GetCoin(def.MaxDenomId);
                    var savedWeights = new Dictionary<CoinDef, int>();
                    if (maxDenom != null)
                    {
                        foreach (var coin in balance.Coins)
                        {
                            if (coin.Value > maxDenom.Value)
                            {
                                savedWeights[coin] = coin.Weight;
                                coin.Weight = 1000000;
                            }
                        }
                    }
                    try
                    {
                        broken.Clear();
                        var hitsToKill = def.Hp;
                        var hitReceivedCount = 0;
                        Action<HitInfo> onHitReceived = hit => hitReceivedCount++;
                        live.HitReceived += onHitReceived;

                        for (var i = 0; i < hitsToKill - 1; i++)
                        {
                            live.OnHit(new HitInfo(HitSource.Hover, 1f, Vector3.zero));
                        }
                        AssertCondition(hitReceivedCount == hitsToKill - 1,
                            pair.Key + ": HitReceived 가 타격 횟수만큼 발행되지 않았습니다: " + hitReceivedCount);
                        AssertCondition(broken.Count == 0,
                            pair.Key + ": 내구도가 남았는데 부서졌습니다. " + (hitsToKill - 1) + "타 후 " + broken.Count + "건 발행.");
                        AssertCondition(live.IsAlive, pair.Key + ": 내구도가 남았는데 죽었습니다.");
                        checkCount++;

                        // --- 마지막 타격에 정확히 한 번 발행한다 (계약 2번) ---
                        live.OnHit(new HitInfo(HitSource.Hover, 1f, Vector3.zero));
                        AssertCondition(hitReceivedCount == hitsToKill,
                            pair.Key + ": 마지막 타격에 HitReceived 가 발행되지 않았습니다.");
                        live.HitReceived -= onHitReceived;
                        AssertCondition(broken.Count == 1,
                            pair.Key + ": OnTargetBroken 이 " + broken.Count + "번 발행됐습니다. 1번이어야 합니다.");
                        AssertCondition(!live.IsAlive, pair.Key + ": 부서졌는데 살아 있다고 합니다.");
                    }
                    finally
                    {
                        def.InstantBreakChance = savedInstantBreak;
                        foreach (var saved in savedWeights)
                        {
                            saved.Key.Weight = saved.Value;
                        }
                    }

                    var info = broken[0];
                    AssertCondition(info.TargetId == pair.Value, pair.Key + ": BreakInfo.TargetId 가 다릅니다.");

                    // 코인 액면은 파괴 시 추첨한다 (이슈 #178) — 더 이상 hp*coin_mult+break_bonus 로
                    // 결정되는 고정값이 아니다. 대신 Coins 내역의 합이 RawCoin과 일치하는지,
                    // 개수·최소 액면 계약을 지키는지를 본다.
                    AssertCondition(info.Coins != null, pair.Key + ": BreakInfo.Coins 가 null 입니다. 빈 목록이어야 합니다.");
                    var minDenom = balance.GetCoin(def.MinDenomId);
                    var drawnCount = 0;
                    var sum = 0m;
                    foreach (var drop in info.Coins)
                    {
                        var coinDef = balance.GetCoin(drop.DenomId);
                        AssertCondition(coinDef != null, pair.Key + ": coins.csv 에 없는 액면 '" + drop.DenomId + "' 이 나왔습니다.");
                        AssertCondition(minDenom == null || coinDef.Value >= minDenom.Value,
                            pair.Key + ": min_denom_id(" + def.MinDenomId + ") 미만 액면 '" + drop.DenomId + "' 이 나왔습니다.");
                        AssertCondition(maxDenom == null || coinDef.Value <= maxDenom.Value,
                            pair.Key + ": max_denom_id(" + def.MaxDenomId + ") 를 넘는 액면 '" + drop.DenomId + "' 이 나왔습니다 (#330).");
                        AssertCondition(drop.Count > 0, pair.Key + ": CoinDrop 개수가 0 이하입니다.");
                        drawnCount += drop.Count;
                        sum += (decimal)coinDef.Value * drop.Count;
                    }
                    AssertCondition(drawnCount == def.CoinCount,
                        pair.Key + ": 뽑힌 코인 개수가 " + drawnCount + " 입니다. coin_count(" + def.CoinCount + ") 여야 합니다.");
                    AssertCondition(info.RawCoin == sum,
                        pair.Key + ": RawCoin(" + info.RawCoin + ") 이 액면 합(" + sum + ") 과 다릅니다.");
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
                foreach (var id in new[] { "normal", "anchor", "runner", "pinata", "angry" })
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
