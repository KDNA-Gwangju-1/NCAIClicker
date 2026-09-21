using System.Linq;
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

        private static IBillPersistence FindBillPersistence()
        {
            return Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OfType<IBillPersistence>()
                .FirstOrDefault();
        }
    }
}
