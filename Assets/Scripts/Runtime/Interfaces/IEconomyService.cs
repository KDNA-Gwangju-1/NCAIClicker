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
    /// 저장 복원 계약. SaveManager·BillManager 가 쓴다 — BillManager 는 파산 시 회차 초기화에 쓴다
    /// (이슈 #71, 파산 초기화는 #158).
    /// </summary>
    public interface IWalletPersistence
    {
        void RestoreWallet(long balance, string remainderText);
        string CurrentRemainderText { get; }
    }

    /// <summary>
    /// 업그레이드가 적용된 실효값 조회 계약. 소비처는 BalanceData 기준값 대신 이것을 읽는다 (이슈 #116).
    /// 기준값은 항상 호출측이 넘긴다 — spawn_count 처럼 기준값이 현재 단계(StageDef)에 따라
    /// 달라지는 스탯이 있어, BalanceData 기준값 표를 여기서 복제하지 않도록 통일했다
    /// (#24 구현 중 발견, #116 코멘트, docs/TECH_NOTES/upgrades.md "왜 이 방법인가").
    /// </summary>
    public interface IUpgradeStats
    {
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

    /// <summary>
    /// 레거시 포인트 적립·조회·소비 계약 (이슈 #175).
    ///
    /// 코인과 별개의 화폐다. **코인은 파산하면 0 이 되지만 이 포인트는 남는다** — 그것이
    /// 이 화폐의 존재 이유다 (GDD 9절, 원작의 세 층 성장 중 영구 층).
    /// 적립은 고지서 납부에 비례한다. 계수는 economy.csv 가 들고 있다.
    /// </summary>
    public interface ILegacyService
    {
        long CurrentLegacyPoints { get; }
        void AddLegacyPoints(long amount);
        bool TrySpendLegacyPoints(long amount);
    }

    /// <summary>
    /// 반지 구매 계약. 반지 상점 화면이 쓴다 (이슈 #183).
    ///
    /// IUpgradeShop 과 모양이 같지만 **쓰는 화폐가 다르다** — 이쪽은 레거시 포인트로 산다.
    /// 둘을 한 계약으로 합치면 화폐를 인자로 받아야 해서, 이미 머지된 업그레이드 구매
    /// 화면(6.8)의 계약을 깨야 한다. 그래서 나란히 둔다.
    /// </summary>
    public interface IRingShop
    {
        int GetRingLevel(string ringId);
        long GetNextRingCost(string ringId);
        bool TryPurchaseRing(string ringId);
    }

    /// <summary>
    /// 레거시 포인트·반지 레벨 저장 복원 계약. SaveManager 만 쓴다 (이슈 #175).
    ///
    /// 반지 레벨은 업그레이드와 같이 **정렬 순서(sort_order) 기준 배열**로 주고받는다 —
    /// id 문자열을 저장 파일에 넣으면 CSV 에서 id 를 바꿀 때 저장이 깨진다 (#116 과 같은 이유).
    ///
    /// **ringLevelsBySortOrder 는 null 일 수 있다.** v2 이하 저장에는 이 필드가 없고,
    /// JsonUtility 는 없는 배열 필드를 빈 배열이 아니라 null 로 되살린다. 길이가 CSV 행 수보다
    /// 짧을 수도 있다(반지가 늘어난 경우) — 구현이 둘 다 감당한다.
    /// </summary>
    public interface ILegacyPersistence
    {
        void RestoreLegacy(long points, int[] ringLevelsBySortOrder);
        int[] CurrentRingLevels { get; }
    }
}
