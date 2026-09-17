using NCAIClicker.Data;

namespace NCAIClicker.Interfaces
{
    /// <summary>
    /// 청구서 및 대출 관리 계약
    /// </summary>
    public interface IBillService
    {
        int CurrentDay { get; }
        int DaysLeft { get; }
        float LoanDailyCut { get; }

        /// <summary>현재 마감 전인 청구서. 없으면 null.</summary>
        Bill ActiveBill { get; }

        /// <summary>납부 직후 골라야 할 퍼크 후보 id 3개. 고르기 전까지만 값이 있고 고르면 비워진다.</summary>
        string[] OfferedPerkIds { get; }

        bool TryPay(Bill bill);
        bool TryTakeLoan(long amount);
        bool TryRepayLoan();

        /// <summary>OfferedPerkIds 중 하나를 고른다. 목록에 없는 id 면 false.</summary>
        bool TryChoosePerk(string perkId);
    }
}
