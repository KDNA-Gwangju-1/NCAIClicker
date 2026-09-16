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
        bool TryPay(Bill bill);
        bool TryTakeLoan(long amount);
        bool TryRepayLoan();
    }
}
