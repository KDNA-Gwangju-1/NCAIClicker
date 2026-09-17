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

    /// <summary>
    /// 런 경계 계약. GameManager 만 쓴다 (이슈 #71).
    /// </summary>
    public interface IRunScoped
    {
        void BeginRun();
    }

    /// <summary>
    /// 저장 복원 계약. SaveManager 만 쓴다 (이슈 #71).
    /// </summary>
    public interface IWalletPersistence
    {
        void RestoreWallet(long balance, string remainderText);
        string CurrentRemainderText { get; }
    }
}
