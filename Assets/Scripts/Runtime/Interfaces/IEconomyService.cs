using NCAIClicker.Data;

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
    /// 런 경계 계약. GameManager 만 쓴다 (이슈 #71, #111).
    /// </summary>
    public interface IRunScoped
    {
        void BeginRun();
        void EndRun();
    }

    /// <summary>
    /// 저장 복원 계약. SaveManager 만 쓴다 (이슈 #71).
    /// </summary>
    public interface IWalletPersistence
    {
        void RestoreWallet(long balance, string remainderText);
        string CurrentRemainderText { get; }
    }

    /// <summary>
    /// 업그레이드가 적용된 실효값 조회 계약. 소비처는 BalanceData 기준값 대신 이것을 읽는다 (이슈 #116).
    /// 대부분의 스탯은 BalanceData 안의 고정 기준값을 쓰지만, spawn_count 처럼 기준값이 현재 단계
    /// (StageDef)에 따라 달라지는 스탯은 호출측이 기준값을 직접 넘기는 오버로드를 쓴다.
    /// </summary>
    public interface IUpgradeStats
    {
        float GetStat(StatId stat);
        float GetStat(StatId stat, float baseValue);
    }

    /// <summary>
    /// 업그레이드 구매 계약. 메뉴·결과 화면(작업 6.8)이 쓴다 (이슈 #116).
    /// </summary>
    public interface IUpgradeShop
    {
        int GetLevel(string upgradeId);
        long GetNextCost(string upgradeId);
        bool TryPurchase(string upgradeId);
    }

    /// <summary>
    /// 업그레이드 레벨 저장 복원 계약. SaveManager 만 쓴다 (이슈 #116).
    /// </summary>
    public interface IUpgradePersistence
    {
        void RestoreUpgradeLevels(int[] levelsBySortOrder);
        int[] CurrentUpgradeLevels { get; }
    }
}
