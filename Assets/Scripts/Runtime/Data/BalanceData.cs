using System;
using System.Collections.Generic;
using UnityEngine;

namespace NCAIClicker.Data
{
    /// <summary>
    /// 밸런스 수치 전체를 담는 단일 에셋.
    /// 이 파일은 Assets/GameData/Balance/*.csv 에서 자동 생성된다. 손으로 고치지 말 것.
    /// 수치를 바꾸려면 CSV를 고치고 메뉴 [NCAI/밸런스 CSV 임포트]를 실행한다.
    /// 근거와 검산은 docs/BALANCE.md 참고.
    /// </summary>
    [CreateAssetMenu(fileName = "BalanceData", menuName = "NCAI/Balance Data")]
    public class BalanceData : ScriptableObject
    {
        public StaminaConfig Stamina = new();
        public EconomyConfig Economy = new();
        public FeverConfig Fever = new();
        public BillConfig Bill = new();

        public List<TargetDef> Targets = new();
        public List<UpgradeDef> Upgrades = new();
        public List<StageDef> Stages = new();
        public List<PerkDef> Perks = new();

        public TargetDef GetTarget(string id) => Targets.Find(t => t.Id == id);
        public UpgradeDef GetUpgrade(string id) => Upgrades.Find(u => u.Id == id);
        public PerkDef GetPerk(string id) => Perks.Find(p => p.Id == id);

        /// <summary>stageNumber 는 1부터 시작한다.</summary>
        public StageDef GetStage(int stageNumber) => Stages.Find(s => s.Stage == stageNumber);
    }

    [Serializable]
    public class StaminaConfig
    {
        public float Max;
        public float IdleDrainPerSec;
        public float MoveDrainPerUnit;
        public float HitDrainPerSwing;
        public float FeverDrainMultiplier;
    }

    [Serializable]
    public class EconomyConfig
    {
        public float BaseHitPower;
        public float HoverSwingIntervalSec;
        public int AutoHammerCountInit;
        public float AutoHammerPower;
        public float AutoHammerHitsPerSec;
        public float HitRadiusBonusPercent;
        public float CoinBonusMultiplier;

        /// <summary>부서진 자리에 새 저금통이 등장하기까지의 대기 시간.</summary>
        public float SpawnIntervalSec;
        public float UpgradeCostGrowth;
        public float StageGoalGrowth;
    }

    [Serializable]
    public class FeverConfig
    {
        public float GaugeMax;
        public float GaugePerHit;
        public float GaugeDecayPerSec;
        public float DecayGraceSec;
        public float DurationSec;
        public float CoinMultiplier;
    }

    /// <summary>
    /// 청구서와 대출. 한 번의 런이 게임 속 하루이며, 청구서는 며칠 뒤 마감을 갖는다.
    /// 마감일까지 못 내면 파산이고, 대출로만 막을 수 있다 (GDD 4절).
    /// </summary>
    [Serializable]
    public class BillConfig
    {
        /// <summary>청구서 기본 납부 기한(일). 단계별 값은 StageDef.DueDays 가 덮어쓴다.</summary>
        public int DueDays;

        /// <summary>대출을 쓸 수 있게 되는 청구서 순번. 첫 청구서는 대출 없이 막아야 한다.</summary>
        public int LoanUnlockBillIndex;

        public float LoanInterestRate;

        /// <summary>상환 전까지 매일 징수되는 수입 비율. 이 징수분은 부채를 줄이지 않는다.</summary>
        public float LoanDailyCutMin;
        public float LoanDailyCutMax;

        public int LoanCooldownDays;
        public int LoanMaxConcurrent;
    }

    [Serializable]
    public class TargetDef
    {
        public string Id;
        public string DisplayName;
        public int Hp;
        public float CoinMult;
        public int BreakBonus;

        /// <summary>부수면 회복되는 스태미나. 회복형(tourist)만 0보다 크다.</summary>
        public float StaminaRestore;

        public float MoveSpeed;
        public float TurnIntervalSec;
    }

    /// <summary>
    /// 업그레이드가 건드릴 수 있는 수치 목록.
    /// upgrade_effects.csv 의 stat 열에 이 이름을 스네이크 케이스로 적는다 (base_hit_power 등).
    /// 여기에 없는 이름을 CSV에 적으면 임포트가 실패하므로 오타가 런타임까지 가지 않는다.
    /// </summary>
    public enum StatId
    {
        BaseHitPower,
        HitRadius,
        AutoHammerCount,
        AutoHammerPower,
        AutoHammerHitsPerSec,
        FeverDuration,
        FeverMultiplier,
        FeverGaugePerHit,
        MaxStamina,
        IdleDrainPerSec,
        MoveDrainPerUnit,
        HitDrainPerSwing,
        CoinBonusMultiplier,
        SpawnCount,
        SpawnIntervalSec,
    }

    public enum EffectType
    {
        /// <summary>기준값에 그대로 더한다. value 0.35 → +0.35</summary>
        Add,
        /// <summary>기준값의 백분율을 더한다. value 2 → +2%</summary>
        Percent,
    }

    [Serializable]
    public class UpgradeEffect
    {
        public StatId Stat;
        public EffectType Type;
        public float ValuePerLevel;
    }

    [Serializable]
    public class UpgradeDef
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public long InitCost;

        /// <summary>0 이하면 economy.csv 의 upgrade_cost_growth 를 쓴다.</summary>
        public float CostGrowth;

        public int MaxLevel;
        public int SortOrder;
        public List<UpgradeEffect> Effects = new();
    }

    [Serializable]
    public class StageDef
    {
        public int Stage;
        public long GoalCoin;
        public long BillAmount;

        /// <summary>이 단계의 청구서 납부 기한(일). 단계가 오르면 짧아진다.</summary>
        public int DueDays;

        public float NormalRatio;
        public float AnchorRatio;
        public float RunnerRatio;
        public float TouristRatio;
        public int SpawnCount;
    }

    /// <summary>
    /// 청구서 조기 납부(4.2) 보상 4종. id 는 스네이크 케이스 그대로 PerkType 이름이 된다 —
    /// 4종 고정이라 둘을 분리해도 얻는 게 없다 (ponytail).
    /// 실제 효과 적용은 이 어셈블리의 몫이 아니다 — GetPerk(id)로 값을 읽어 각 시스템이 직접 적용한다.
    /// </summary>
    public enum PerkType
    {
        StaminaRestore,
        CoinGainBoost,
        HitPowerBoost,
        HitRadiusBoost,
    }

    [Serializable]
    public class PerkDef
    {
        public string Id;
        public string DisplayName;
        public PerkType Type;

        /// <summary>의미는 Type에 따라 다르다: 스태미나 회복량(점수) / 코인 배율(ratio) / 타격력·판정 보너스(percent).</summary>
        public float Value;

        /// <summary>CoinGainBoost 에서만 0보다 크다. 나머지는 즉시 적용되거나 런이 끝날 때까지 지속돼 지속시간이 없다.</summary>
        public float DurationSec;
    }
}
