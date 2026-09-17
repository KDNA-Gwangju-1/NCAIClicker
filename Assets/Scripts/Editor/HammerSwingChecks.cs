using System;
using NCAIClicker.Core;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using NCAIClicker.Targets;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 망치 스윙 판정 반경 및 타격 로직을 검증한다.
    /// GitHub 이슈 #109 완료 기준 검증.
    /// </summary>
    public static class HammerSwingChecks
    {
        private const string NormalPrefabPath = "Assets/Prefabs/Targets/TargetNormal.prefab";
        private const string BalanceAssetPath = "Assets/GameData/Generated/BalanceData.asset";

        [MenuItem("NCAI/망치 스윙 타격 판정 검증")]
        public static void RunBatch()
        {
            var passedCount = 0;

            // 1. 조준 반경의 원본이 CSV 인지 검증 (코드 상수로 돌아가지 않았는지)
            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>(BalanceAssetPath);
            Assert(balance != null, $"{BalanceAssetPath} 를 찾지 못했습니다. 밸런스 CSV 임포트를 실행하세요.");
            Assert(balance.Economy.ReticleRadius > 0f, "economy.csv 의 reticle_radius 가 BalanceData 에 실려 있어야 합니다.");
            passedCount++;

            // 2. 컨트롤러가 CSV 값을 그대로 판정 반경으로 쓰는지 검증
            var controllerGo = new GameObject("TestHammerController");
            controllerGo.hideFlags = HideFlags.HideAndDontSave;
            GameObject instanceNear = null;
            GameObject instanceFar = null;

            try
            {
                var controller = controllerGo.AddComponent<HammerSwingController>();
                SetPrivateField(controller, "_balanceData", balance);
                InvokePrivate(controller, "Awake");
                Assert(Mathf.Abs(controller.HitRadius - balance.Economy.ReticleRadius) < 0.001f,
                    "HitRadius 는 economy.csv 의 reticle_radius 와 같아야 합니다.");
                passedCount++;

                // 3. 반경 내 크리처와 반경 밖 크리처 OverlapSphere 판정 로직 검증
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NormalPrefabPath);
                Assert(prefab != null, "TargetNormal 프리팹을 찾지 못했습니다.");

                instanceNear = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instanceNear.hideFlags = HideFlags.HideAndDontSave;
                // 반경의 3분의 2 지점 — CSV 값이 바뀌어도 "안"에 남는다.
                var nearDistance = balance.Economy.ReticleRadius * 0.67f;
                instanceNear.transform.position = new Vector3(nearDistance, 0f, 0f);
                var targetNear = instanceNear.GetComponent<Target>();
                targetNear.Initialize();

                instanceFar = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instanceFar.hideFlags = HideFlags.HideAndDontSave;
                // 반경의 3배 지점 — 대상 콜라이더 확대(hit_radius_bonus)를 감안해도 밖이다.
                var farDistance = balance.Economy.ReticleRadius * 3f;
                instanceFar.transform.position = new Vector3(farDistance, 0f, 0f);
                var targetFar = instanceFar.GetComponent<Target>();
                targetFar.Initialize();

                // Physics 시뮬레이션 동기화
                Physics.SyncTransforms();

                // 원점 (0, 0, 0) 기준 OverlapSphere 수행
                var hitColliders = Physics.OverlapSphere(Vector3.zero, controller.HitRadius);
                var foundNear = false;
                var foundFar = false;

                for (var i = 0; i < hitColliders.Length; i++)
                {
                    var hittable = hitColliders[i].GetComponentInParent<IHittable>();
                    if (hittable == (IHittable)targetNear)
                    {
                        foundNear = true;
                    }
                    if (hittable == (IHittable)targetFar)
                    {
                        foundFar = true;
                    }
                }

                Assert(foundNear, $"반경 {controller.HitRadius} 안에 위치한 targetNear(거리 {nearDistance})는 감지되어야 합니다.");
                Assert(!foundFar, $"반경 {controller.HitRadius} 밖에 위치한 targetFar(거리 {farDistance})는 감지되지 않아야 합니다.");
                passedCount++;

                // 4. 최근접 타깃 선택 및 피격 검증
                var initialHp = targetNear.CurrentHp;
                var col = instanceNear.GetComponent<SphereCollider>();
                var closestPoint = col != null ? col.ClosestPoint(Vector3.zero) : instanceNear.transform.position;
                targetNear.OnHit(new HitInfo(HitSource.Hover, 1.0f, closestPoint));
                Assert(targetNear.CurrentHp < initialHp, "targetNear 피격 시 HP 가 감소해야 합니다.");
                passedCount++;

                Debug.Log($"[HammerSwingChecks] 총 {passedCount}건의 검증을 모두 통과했습니다.");
            }
            finally
            {
                if (controllerGo != null)
                {
                    UnityEngine.Object.DestroyImmediate(controllerGo);
                }
                if (instanceNear != null)
                {
                    UnityEngine.Object.DestroyImmediate(instanceNear);
                }
                if (instanceFar != null)
                {
                    UnityEngine.Object.DestroyImmediate(instanceFar);
                }
            }
        }

        private static void SetPrivateField(Component target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert(field != null, $"{target.GetType().Name} 에 {fieldName} 필드가 없습니다.");
            field.SetValue(target, value);
        }

        /// <summary>AddComponent 로 만든 컴포넌트는 Awake 가 바로 돌지 않으므로 직접 부른다.</summary>
        private static void InvokePrivate(Component target, string methodName)
        {
            var method = target.GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert(method != null, $"{target.GetType().Name} 에 {methodName} 메서드가 없습니다.");
            method.Invoke(target, null);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"[HammerSwingChecks 검증 실패] {message}");
            }
        }
    }
}
