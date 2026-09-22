using NCAIClicker.Data;

namespace NCAIClicker.Interfaces
{
    /// <summary>
    /// 고지서 및 대출 관리 계약
    /// </summary>
    public interface IBillService
    {
        int CurrentDay { get; }
        int CurrentCycle { get; }
        int DaysLeft { get; }
        float LoanDailyCut { get; }

        /// <summary>현재 마감 전인 고지서. 없으면 null.</summary>
        Bill ActiveBill { get; }

        /// <summary>납부 직후 골라야 할 퍼크 후보 id 3개. 고르기 전까지만 값이 있고 고르면 비워진다.</summary>
        string[] OfferedPerkIds { get; }
        PostPaymentFlowState PaymentFlowState { get; }

        bool TryPay(Bill bill);
        bool TryTakeLoan(long amount);
        bool TryRepayLoan();

        /// <summary>OfferedPerkIds 중 하나를 고른다. 목록에 없는 id 면 false.</summary>
        bool TryChoosePerk(string perkId);
        bool TryConfirmPaidFeedback();
        bool TryEnterInvestmentMenu();
        bool TryCompletePostPaymentFlow();

        /// <summary>
        /// 플레이어가 스스로 파산을 선언한다 (이슈 #175). 마감 미납으로 자동 발동하는 파산과
        /// **같은 처리를 탄다** — OnBankrupt 를 발행하고 회차를 1일차로 되돌린다.
        ///
        /// 되돌릴 수 없다. 부르는 쪽이 확인 절차를 먼저 거친다 (고지서 화면의 파산 선고 버튼).
        /// 코인·단계는 사라지고 레거시 포인트와 반지는 남는다 (#183).
        /// </summary>
        /// <summary>
        /// 다음 날로 넘어가기 직전에 마감을 확정한다 (이슈 #211).
        /// 마감일이 지났는데 미납 상태이면 파산 처리 후 true 를 반환한다.
        /// 기한이 남았거나 이미 납부 완료된 상태이면 파산 없이 false 를 반환한다.
        /// </summary>
        bool TryCloseDay();

        void DeclareBankruptcy();

        void RestoreCycle(int cycle);
    }

    /// <summary>
    /// 날짜·고지서·대출 저장 복원 계약. SaveManager 만 쓴다 (이슈 #221, #143 제안 계승).
    ///
    /// CurrentDay·ActiveBill·OfferedPerkIds 는 IBillService 에 이미 있는 프로퍼티와 이름이
    /// 같다 — BillManager 는 그 프로퍼티를 새로 만들지 않고 그대로 이 계약도 만족한다.
    /// IUpgradePersistence·ILegacyPersistence 와 같은 모양(Restore* 메서드 + Current* 조회)을 쓴다.
    /// </summary>
    public interface IBillPersistence
    {
        void RestoreBillState(int currentDay, int billIndex, Bill activeBill,
                              Loan activeLoan, int lastLoanRepaidDay, string[] offeredPerkIds,
                              PostPaymentFlowState postPaymentFlowState = PostPaymentFlowState.None);

        int CurrentDay { get; }
        Bill ActiveBill { get; }
        string[] OfferedPerkIds { get; }
        PostPaymentFlowState PaymentFlowState { get; }
        int CurrentBillIndex { get; }
        Loan CurrentLoan { get; }
        int LastLoanRepaidDay { get; }
        int CurrentCycle { get; }
        void RestoreCycle(int cycle);
    }
}
