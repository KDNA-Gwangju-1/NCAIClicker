using NCAIClicker.Data;

namespace NCAIClicker.Interfaces
{
    /// <summary>
    /// 고지서 및 대출 관리 계약
    /// </summary>
    public interface IBillService
    {
        int CurrentDay { get; }
        int DaysLeft { get; }
        float LoanDailyCut { get; }

        /// <summary>현재 마감 전인 고지서. 없으면 null.</summary>
        Bill ActiveBill { get; }

        /// <summary>납부 직후 골라야 할 퍼크 후보 id 3개. 고르기 전까지만 값이 있고 고르면 비워진다.</summary>
        string[] OfferedPerkIds { get; }

        bool TryPay(Bill bill);
        bool TryTakeLoan(long amount);
        bool TryRepayLoan();

        /// <summary>OfferedPerkIds 중 하나를 고른다. 목록에 없는 id 면 false.</summary>
        bool TryChoosePerk(string perkId);

        /// <summary>
        /// 플레이어가 스스로 파산을 선언한다 (이슈 #175). 마감 미납으로 자동 발동하는 파산과
        /// **같은 처리를 탄다** — OnBankrupt 를 발행하고 회차를 1일차로 되돌린다.
        ///
        /// 되돌릴 수 없다. 부르는 쪽이 확인 절차를 먼저 거친다 (고지서 화면의 파산 선고 버튼).
        /// 코인·단계는 사라지고 레거시 포인트와 반지는 남는다 (#183).
        /// </summary>
        void DeclareBankruptcy();
    }
}
