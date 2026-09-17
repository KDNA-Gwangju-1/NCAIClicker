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

        [MenuItem("NCAI/망치 스윙 타격 판정 검증")]
        public static void RunBatch()
        {
            var passedCount = 0;

            // 1. 레티클 반경 상수 동기화 검증
            Assert(Mathf.Abs(HammerSwingController.DefaultReticleRadius - 0.45f) < 0.001f, "DefaultReticleRadius 는 0.45f 여야 합니다.");
            passedCount++;

            // 2. HammerSwingController 인스턴스 기본 HitRadius 검증
            var controllerGo = new GameObject("TestHammerController");
            controllerGo.hideFlags = HideFlags.HideAndDontSave;
            GameObject instanceNear = null;
            GameObject instanceFar = null;

            try
            {
                var controller = controllerGo.AddComponent<HammerSwingController>();
                Assert(Mathf.Abs(controller.HitRadius - HammerSwingController.DefaultReticleRadius) < 0.001f, "기본 HitRadius는 DefaultReticleRadius 와 일치해야 합니다.");
                passedCount++;

                // 3. 반경 내 크리처와 반경 밖 크리처 OverlapSphere 판정 로직 검증
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NormalPrefabPath);
                Assert(prefab != null, "TargetNormal 프리팹을 찾지 못했습니다.");

                instanceNear = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instanceNear.hideFlags = HideFlags.HideAndDontSave;
                instanceNear.transform.position = new Vector3(0.3f, 0f, 0f);
                var targetNear = instanceNear.GetComponent<Target>();
                targetNear.Initialize();

                instanceFar = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instanceFar.hideFlags = HideFlags.HideAndDontSave;
                instanceFar.transform.position = new Vector3(1.5f, 0f, 0f);
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

                Assert(foundNear, "반경 0.45 안에 위치한 targetNear(거리 0.3)는 감지되어야 합니다.");
                Assert(!foundFar, "반경 0.45 밖에 위치한 targetFar(거리 1.5)는 감지되지 않아야 합니다.");
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

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"[HammerSwingChecks 검증 실패] {message}");
            }
        }
    }
}
