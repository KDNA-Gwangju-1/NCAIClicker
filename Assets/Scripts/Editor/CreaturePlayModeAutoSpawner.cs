#if UNITY_EDITOR
using NCAIClicker.Core;
using NCAIClicker.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// Game 씬 파일을 수정하지 않고도 Play Mode 에서 크리처들을 테스트할 수 있도록
    /// 씬 로드 직후 CreatureManager 를 메모리에 자동 주입한다.
    /// 씬 파일(.unity)을 건드리지 않으므로 병합 충돌 위험이 전혀 없다.
    /// </summary>
    public static class CreaturePlayModeAutoSpawner
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnSceneLoadedAutoInject()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.name != "Game")
            {
                return;
            }

            if (CreatureManager.Instance != null)
            {
                return;
            }

            var go = new GameObject("CreatureManager (AutoTest)");
            var mgr = go.AddComponent<CreatureManager>();

            var balance = AssetDatabase.LoadAssetAtPath<BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            var pNormal = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Targets/TargetNormal.prefab");
            var pAnchor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Targets/TargetAnchor.prefab");
            var pRunner = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Targets/TargetRunner.prefab");
            var pTourist = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Targets/TargetTourist.prefab");

            var so = new SerializedObject(mgr);
            so.FindProperty("_balanceData").objectReferenceValue = balance;
            so.FindProperty("_targetNormalPrefab").objectReferenceValue = pNormal;
            so.FindProperty("_targetAnchorPrefab").objectReferenceValue = pAnchor;
            so.FindProperty("_targetRunnerPrefab").objectReferenceValue = pRunner;
            so.FindProperty("_targetTouristPrefab").objectReferenceValue = pTourist;
            so.ApplyModifiedPropertiesWithoutUndo();

            mgr.InitializeStage(1);
            Debug.Log("[CreatureAutoSpawner] Game 씬 파일 수정 없이 테스트용 크리처 6마리를 자동 스폰했습니다.");
        }
    }
}
#endif
