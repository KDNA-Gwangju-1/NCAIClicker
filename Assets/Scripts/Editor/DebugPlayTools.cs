using System.Linq;
using NCAIClicker.Core;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// Play Mode 테스트를 위한 에디터 전용 디버그 도구.
    /// 마감일 스킵 및 즉시 런 종료를 에디터 메뉴에서 트리거한다.
    /// 빌드에 포함되지 않는 Editor 폴더 전용 도구다.
    /// </summary>
    public static class DebugPlayTools
    {
        [MenuItem("NCAI/디버그/마감 당일로 날짜 이동", false, MenuPriority.DebugAdvanceToDueDay)]
        public static void JumpToDueDay()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[디버그] Play Mode 에서만 실행할 수 있습니다.");
                return;
            }

            var manager = FindBillPersistence();
            if (manager == null)
            {
                Debug.LogWarning("[디버그] BillManager 를 찾을 수 없습니다.");
                return;
            }

            var activeBill = manager.ActiveBill;
            if (activeBill == null)
            {
                Debug.LogWarning("[디버그] 활성 고지서가 없습니다.");
                return;
            }

            manager.RestoreBillState(activeBill.DueDay, manager.CurrentBillIndex, activeBill,
                manager.CurrentLoan, manager.LastLoanRepaidDay, manager.OfferedPerkIds);
            var daysLeft = ((IBillService)manager).DaysLeft;
            GameEvents.PublishBillDueSoon(daysLeft);
            Debug.Log($"[디버그] 현재 날짜를 마감 당일({activeBill.DueDay}일차, 남은 일수 {daysLeft}일)로 이동했습니다.");
        }

        [MenuItem("NCAI/디버그/마감 당일로 날짜 이동", true)]
        private static bool ValidateJumpToDueDay() => Application.isPlaying;

        [MenuItem("NCAI/디버그/즉시 런 종료 (정산창 열기)", false, MenuPriority.DebugDepleteStamina)]
        public static void EndRunImmediately()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[디버그] Play Mode 에서만 실행할 수 있습니다.");
                return;
            }

            Debug.Log("[디버그] 스태미나 소진 이벤트를 발행하여 런을 즉시 종료합니다.");
            GameEvents.PublishStaminaDepleted();
        }

        [MenuItem("NCAI/디버그/즉시 런 종료 (정산창 열기)", true)]
        private static bool ValidateEndRunImmediately() => Application.isPlaying;

        [MenuItem("NCAI/디버그/날짜 1일 진행", false, MenuPriority.DebugAdvanceOneDay)]
        public static void AdvanceOneDay()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[디버그] Play Mode 에서만 실행할 수 있습니다.");
                return;
            }

            var manager = FindBillPersistence();
            if (manager == null)
            {
                Debug.LogWarning("[디버그] BillManager 를 찾을 수 없습니다.");
                return;
            }

            var current = manager.CurrentDay;
            manager.RestoreBillState(current + 1, manager.CurrentBillIndex, manager.ActiveBill,
                manager.CurrentLoan, manager.LastLoanRepaidDay, manager.OfferedPerkIds);
            var daysLeft = ((IBillService)manager).DaysLeft;
            GameEvents.PublishBillDueSoon(daysLeft);
            Debug.Log($"[디버그] 날짜를 1일 진행했습니다: {current}일차 -> {current + 1}일차 (남은 일수: {daysLeft}일)");
        }

        [MenuItem("NCAI/디버그/날짜 1일 진행", true)]
        private static bool ValidateAdvanceOneDay() => Application.isPlaying;

        /// <summary>
        /// 고지서를 내지 않고 단계를 하나 올린다 (#247 해금 확인용). 런 중이면 책상 위 저금통을 새 단계 구성으로
        /// 다시 깐다. 이미 발행된 고지서는 그대로다 — 새 단계 고지서는 다음 납부 뒤에 나온다.
        /// </summary>
        [MenuItem("NCAI/디버그/단계 +1", false, MenuPriority.DebugAdvanceStage)]
        public static void AdvanceStage()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[디버그] Play Mode 에서만 실행할 수 있습니다.");
                return;
            }

            var stageManager = Object.FindFirstObjectByType<StageGoalManager>(FindObjectsInactive.Include);
            if (stageManager == null)
            {
                Debug.LogWarning("[디버그] StageGoalManager 를 찾을 수 없습니다.");
                return;
            }

            if (!stageManager.AdvanceStage())
            {
                Debug.LogWarning($"[디버그] 이미 마지막 단계({stageManager.CurrentStageNumber}단계)입니다.");
                return;
            }

            var stage = stageManager.CurrentStageNumber;
            var creatureManager = CreatureManager.Instance;
            if (creatureManager != null && creatureManager.ActiveCreatures.Count > 0)
            {
                creatureManager.InitializeStage(stage);
            }
            Debug.Log($"[디버그] {stage}단계로 올렸습니다. 이 상태로 저장되면 세이브에도 남습니다.");
        }

        [MenuItem("NCAI/디버그/단계 +1", true)]
        private static bool ValidateAdvanceStage() => Application.isPlaying;

        /// <summary>
        /// 회차 누적 수입을 다음 크리처 해금 기준액 바로 아래로 옮긴다 (#301 확인용). 다음 런을 조금만 벌고
        /// 끝내면 그 정산에서 해금된다 — 해금 "순간"(이벤트·정산창 연출)을 실제 흐름 그대로 볼 수 있다.
        /// </summary>
        [MenuItem("NCAI/디버그/다음 크리처 해금 직전으로", false, MenuPriority.DebugNextUnlock)]
        public static void JumpToNextUnlock()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[디버그] Play Mode 에서만 실행할 수 있습니다.");
                return;
            }

            var unlock = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OfType<IUnlockPersistence>()
                .FirstOrDefault();
            var balance = AssetDatabase.LoadAssetAtPath<NCAIClicker.Data.BalanceData>("Assets/GameData/Generated/BalanceData.asset");
            if (unlock == null || balance == null)
            {
                Debug.LogWarning("[디버그] IUnlockPersistence 또는 BalanceData 를 찾을 수 없습니다.");
                return;
            }

            var next = balance.GetNextUnlockTarget(unlock.EarnedTotal);
            if (next == null)
            {
                Debug.LogWarning("[디버그] 이미 모든 크리처가 해금됐습니다.");
                return;
            }

            unlock.RestoreEarnedTotal(next.UnlockEarned - 1L);
            Debug.Log($"[디버그] 누적 수입을 ${next.UnlockEarned - 1L:N0} 로 옮겼습니다. 이번 런을 $1 이상 벌고 끝내면 {next.DisplayName} 이(가) 해금됩니다. 저장되면 세이브에도 남습니다.");
        }

        [MenuItem("NCAI/디버그/다음 크리처 해금 직전으로", true)]
        private static bool ValidateJumpToNextUnlock() => Application.isPlaying;

        private static IBillPersistence FindBillPersistence()
        {
            return Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OfType<IBillPersistence>()
                .FirstOrDefault();
        }
    }
}
