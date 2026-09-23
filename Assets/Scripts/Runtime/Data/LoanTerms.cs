using System;
using UnityEngine;

namespace NCAIClicker.Data
{
    /// <summary>
    /// 대출 조건 계산식 (이슈 #273). BillManager 가 대출을 확정할 때와 고지서 화면이 금액 선택을
    /// 미리 보여 줄 때 **같은 이 식**을 부른다 — 화면이 식을 따로 가지면 미리보기와 실제 값이 조용히 어긋난다.
    ///
    /// Loan 에 두지 않는 이유: Loan 은 저장 DTO 라 메서드를 넣지 않는다 (PATTERNS.md 6절).
    /// </summary>
    public static class LoanTerms
    {
        /// <summary>
        /// 이자 포함 상환액. 소수 부분은 올려 정수로 확정한다 (ARCHITECTURE.md "코인 계산·정산 계약" 8번).
        /// 이진 부동소수로 곱하면 올림이 오차를 키운다 — double 로는 410 × 1.1 이 451.00000000000006 이 되어
        /// 452 로 한 푼 더 올라간다 (1~5000 중 228개가 이렇게 틀린다, #273 에서 확인). 그래서 decimal 로 계산한다.
        /// </summary>
        public static long CalculateOwed(long principal, float interestRate)
        {
            var owed = principal * (1m + (decimal)interestRate);
            return (long)Math.Ceiling(owed);
        }

        /// <summary>
        /// 일일 징수율을 빌린 금액에 비례해 정한다 — 고지서 전액을 빌리면 상한, 조금만 빌리면 하한에 가깝다.
        /// 대출할 때 한 번만 정하고 미상환 기간 내내 고정한다 (ARCHITECTURE.md Loan.DailyCut).
        /// 범위 안에서 무작위로 뽑지 않는 이유: 같은 선택이 늘 같은 결과를 내야 7.2 밸런싱 실측과
        /// Edit Mode 검증이 성립하고, "많이 빌릴수록 비싸다" 는 저울질도 이쪽이 분명하다.
        /// </summary>
        public static float CalculateDailyCut(long amount, long billAmount, BillConfig config)
        {
            var ratio = billAmount <= 0L ? 1f : Mathf.Clamp01((float)amount / billAmount);
            return Mathf.Lerp(config.LoanDailyCutMin, config.LoanDailyCutMax, ratio);
        }
    }
}
