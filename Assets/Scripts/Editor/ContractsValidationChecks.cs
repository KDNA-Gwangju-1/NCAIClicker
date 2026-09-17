using System;
using UnityEngine;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// 공용 계약(인터페이스, 이벤트 버스, DTO) 정합성을 검증한다.
    /// </summary>
    public static class ContractsValidationChecks
    {
        public static void RunBatch()
        {
            VerifyGameEvents();
            VerifyDataStructures();
            Debug.Log("[ContractsValidationChecks] All contract checks passed successfully.");
        }

        private static void VerifyGameEvents()
        {
            var coinEarnedReceived = 0L;
            GameEvents.OnCoinEarned += val => coinEarnedReceived = val;
            GameEvents.PublishCoinEarned(500L);
            if (coinEarnedReceived != 500L)
            {
                throw new InvalidOperationException("OnCoinEarned event failed");
            }

            var targetBrokenInvoked = false;
            var testBreak = new BreakInfo("target_1", 100m, 5f, new Vector3(1f, 2f, 3f));
            GameEvents.OnTargetBroken += info =>
            {
                if (info.TargetId == "target_1" && info.RawCoin == 100m && info.StaminaRestore == 5f)
                {
                    targetBrokenInvoked = true;
                }
            };
            GameEvents.PublishTargetBroken(testBreak);
            if (!targetBrokenInvoked)
            {
                throw new InvalidOperationException("OnTargetBroken event failed");
            }

            var coinAfterReset = 0L;
            var brokenAfterReset = false;
            GameEvents.OnCoinEarned += val => coinAfterReset = val;
            GameEvents.OnTargetBroken += _ => brokenAfterReset = true;
            GameEvents.ResetAll();
            GameEvents.PublishCoinEarned(999L);
            GameEvents.PublishTargetBroken(testBreak);
            if (coinAfterReset != 0L || brokenAfterReset)
            {
                throw new InvalidOperationException("ResetAll failed to clear delegates");
            }
        }

        private static void VerifyDataStructures()
        {
            var hit = new HitInfo(HitSource.Hover, 10f, Vector3.zero);
            if (hit.Source != HitSource.Hover || hit.Damage != 10f)
            {
                throw new InvalidOperationException("HitInfo failed");
            }

            var save = new SaveData();
            if (save.Version != 2 || save.CurrentDay != 1 || save.LastLoanRepaidDay != -1)
            {
                throw new InvalidOperationException("SaveData default values mismatch");
            }
        }
    }
}