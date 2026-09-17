using System;
using NCAIClicker.Data;
using UnityEngine;

namespace NCAIClicker.Events
{
    /// <summary>
    /// 전역 정적 이벤트 버스
    /// </summary>
    public static class GameEvents
    {
        public static event Action<long> OnCoinEarned;
        public static event Action<long> OnBalanceChanged;
        public static event Action<long> OnRunCoinChanged;
        public static event Action<Bill> OnBillIssued;
        public static event Action<Bill> OnBillPaid;
        public static event Action<int> OnDayEnded;
        public static event Action<int> OnBillDueSoon;
        public static event Action OnBankrupt;
        public static event Action<BreakInfo> OnTargetBroken;
        public static event Action<HitSource, bool> OnSwingResolved;
        public static event Action<float, float> OnStaminaChanged;
        public static event Action<float> OnStaminaRestored;
        public static event Action OnStaminaDepleted;
        public static event Action<float, float> OnFeverGaugeChanged;
        public static event Action OnFeverStart;
        public static event Action OnFeverEnd;
        public static event Action<int> OnStageGoalReached;

        public static void PublishCoinEarned(long amount) => OnCoinEarned?.Invoke(amount);
        public static void PublishBalanceChanged(long currentBalance) => OnBalanceChanged?.Invoke(currentBalance);
        public static void PublishRunCoinChanged(long runCoin) => OnRunCoinChanged?.Invoke(runCoin);
        public static void PublishBillIssued(Bill bill) => OnBillIssued?.Invoke(bill);
        public static void PublishBillPaid(Bill bill) => OnBillPaid?.Invoke(bill);
        public static void PublishDayEnded(int completedDay) => OnDayEnded?.Invoke(completedDay);
        public static void PublishBillDueSoon(int daysLeft) => OnBillDueSoon?.Invoke(daysLeft);
        public static void PublishBankrupt() => OnBankrupt?.Invoke();
        public static void PublishTargetBroken(BreakInfo breakInfo) => OnTargetBroken?.Invoke(breakInfo);
        public static void PublishSwingResolved(HitSource source, bool isHit) => OnSwingResolved?.Invoke(source, isHit);
        public static void PublishStaminaChanged(float currentStamina, float maxStamina) => OnStaminaChanged?.Invoke(currentStamina, maxStamina);
        public static void PublishStaminaRestored(float restoredAmount) => OnStaminaRestored?.Invoke(restoredAmount);
        public static void PublishStaminaDepleted() => OnStaminaDepleted?.Invoke();
        public static void PublishFeverGaugeChanged(float currentGauge, float maxGauge) => OnFeverGaugeChanged?.Invoke(currentGauge, maxGauge);
        public static void PublishFeverStart() => OnFeverStart?.Invoke();
        public static void PublishFeverEnd() => OnFeverEnd?.Invoke();
        public static void PublishStageGoalReached(int stageNumber) => OnStageGoalReached?.Invoke(stageNumber);

        /// <summary>
        /// 서브시스템 등록 시 정적 이벤트를 초기화한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetAll()
        {
            OnCoinEarned = null;
            OnBalanceChanged = null;
            OnRunCoinChanged = null;
            OnBillIssued = null;
            OnBillPaid = null;
            OnDayEnded = null;
            OnBillDueSoon = null;
            OnBankrupt = null;
            OnTargetBroken = null;
            OnSwingResolved = null;
            OnStaminaChanged = null;
            OnStaminaRestored = null;
            OnStaminaDepleted = null;
            OnFeverGaugeChanged = null;
            OnFeverStart = null;
            OnFeverEnd = null;
            OnStageGoalReached = null;
        }
    }
}
