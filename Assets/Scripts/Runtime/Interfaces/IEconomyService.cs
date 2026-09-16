namespace NCAIClicker.Interfaces
{
    /// <summary>
    /// 재화 정산 및 지갑 계약
    /// </summary>
    public interface IEconomyService
    {
        void AddCoin(decimal rawAmount);
        void AddLoanPrincipal(long amount);
        bool TrySpendCoin(long amount);
        long CurrentCoin { get; }
        long RunCoin { get; }
    }
}
