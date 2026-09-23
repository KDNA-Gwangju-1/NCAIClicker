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

        /// <summary>
        /// 단계별 종류 출현 가중치 (stage_spawns.csv, #293). 한 단계에 행이 없는 종류는 그 단계에서
        /// 등장하지 않는다 — 크리처 해금은 이 행의 유무로 표현한다 (#247).
        /// </summary>
        public List<StageSpawnDef> StageSpawns = new();
        public List<PerkDef> Perks = new();

        /// <summary>고지서에 찍히는 발신처와 제목. 금액·기한과 무관한 표기용 데이터다 (이슈 #34).</summary>
        public List<BillNameDef> BillNames = new();

        /// <summary>
        /// 코인 액면과 추첨 가중치의 정본 (이슈 #178). coins.csv 파일 순서를 그대로 유지한다 —
        /// 결과 화면이 이 순서로 액면 칸을 채운다 (ResultUIController).
        /// </summary>
        public List<CoinDef> Coins = new();

        public TargetDef GetTarget(string id) => Targets.Find(t => t.Id == id);
        public UpgradeDef GetUpgrade(string id) => Upgrades.Find(u => u.Id == id);
        public PerkDef GetPerk(string id) => Perks.Find(p => p.Id == id);
        public CoinDef GetCoin(string id) => Coins.Find(c => c.Id == id);

        public List<RingDef> Rings = new();

        public RingDef GetRing(string id) => Rings.Find(r => r.Id == id);

        /// <summary>stageNumber 는 1부터 시작한다.</summary>
        public StageDef GetStage(int stageNumber) => Stages.Find(s => s.Stage == stageNumber);

        /// <summary>stageNumber 단계에 등장하는 종류와 가중치. 파일 순서를 유지한다.</summary>
        public List<StageSpawnDef> GetStageSpawns(int stageNumber) => StageSpawns.FindAll(s => s.Stage == stageNumber);

        /// <summary>
        /// 종류가 처음 등장하는 단계 (#247). stage_spawns.csv 에서 비율이 0 보다 큰 첫 단계이며, 없으면 0 이다.
        /// 해금은 이 값으로만 판정한다 — 코드에 해금 목록을 두지 않는다.
        /// </summary>
        public int GetUnlockStage(string targetId)
        {
            var first = 0;
            foreach (var spawn in StageSpawns)
            {
                if (spawn.TargetId == targetId && spawn.Ratio > 0f && (first == 0 || spawn.Stage < first))
                {
                    first = spawn.Stage;
                }
            }
            return first;
        }

        /// <summary>stageNumber 단계까지 해금된 종류를 해금 순서(같은 단계면 targets.csv 순서)로 돌려준다.</summary>
        public List<TargetDef> GetUnlockedTargets(int stageNumber)
        {
            var unlocked = Targets.FindAll(t =>
            {
                var stage = GetUnlockStage(t.Id);
                return stage > 0 && stage <= stageNumber;
            });
            // List.Sort 는 안정 정렬이 아니라 원래 순서를 보조 키로 쓴다.
            var order = new Dictionary<string, int>();
            for (var i = 0; i < Targets.Count; i++)
            {
                order[Targets[i].Id] = i;
            }
            unlocked.Sort((a, b) =>
            {
                var byStage = GetUnlockStage(a.Id).CompareTo(GetUnlockStage(b.Id));
                return byStage != 0 ? byStage : order[a.Id].CompareTo(order[b.Id]);
            });
            return unlocked;
        }

        /// <summary>stageNumber 다음 단계들 중 가장 먼저 해금되는 종류. 더 없으면 null.</summary>
        public TargetDef GetNextUnlockTarget(int stageNumber)
        {
            TargetDef next = null;
            var nextStage = int.MaxValue;
            foreach (var target in Targets)
            {
                var stage = GetUnlockStage(target.Id);
                if (stage > stageNumber && stage < nextStage)
                {
                    next = target;
                    nextStage = stage;
                }
            }
            return next;
        }

        /// <summary>
        /// 씨앗값으로 고지서 이름을 고른다. 고지서마다 다른 이름이 나오되, **같은 고지서를 다시 열면
        /// 같은 이름**이 나와야 한다 — 열 때마다 바뀌면 "아까 그 고지서가 맞나" 를 의심하게 된다.
        /// 그래서 난수 생성기 대신 씨앗값을 흩는 해시를 쓴다.
        /// </summary>
        public BillNameDef GetBillName(int seed)
        {
            if (BillNames.Count == 0)
            {
                return null;
            }

            // 작은 씨앗값이 순서대로 들어와도 결과가 이웃하지 않게 흩는다 (Knuth 곱셈 해시).
            unchecked
            {
                var hashed = (uint)seed * 2654435761u;
                return BillNames[(int)(hashed % (uint)BillNames.Count)];
            }
        }
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

        /// <summary>호버 적중 1회당 자동 망치 발동 확률(%). 퍼크가 올린다 (3.13).</summary>
        public float AutoHammerProcChancePercent;

        /// <summary>자동 망치 한 사이클(장전·강타·반동) 길이(초). 타이머 주기가 아니라 연출 길이다.</summary>
        public float AutoHammerSwingSec;
        public float HitRadiusBonusPercent;

        /// <summary>호버 망치 조준 판정 반경. 대상 콜라이더를 넓히는 HitRadiusBonusPercent 와는 다른 축이다.</summary>
        public float ReticleRadius;
        public float CoinBonusMultiplier;

        /// <summary>
        /// 미사용 호환 필드 (#156 B안 채택으로 시간 기반 개별 리스폰을 제거했다).
        /// 되돌릴 경우를 대비해 값은 0으로 두고 필드는 남긴다 — MoveDrainPerUnit 과 같은 취급이다.
        /// 근거는 REFERENCE_ANALYSIS.md 9절.
        /// </summary>
        public float SpawnIntervalSec;

        /// <summary>
        /// 저금통 파괴 시 즉시 1개를 추가로 스폰할 확률(%). 기본값은 0 — 업그레이드(저금통 수집벽)가
        /// 이 값을 올린다. 원작 재관찰(REFERENCE_ANALYSIS.md 9절)에서 확인한 확률 기반 추가 생성 축이다.
        /// </summary>
        public float ExtraSpawnChanceOnDestroy;

        /// <summary>고지서 납부액 이만큼당 레거시 포인트 1점 (이슈 #175). 0 이하면 적립하지 않는다.</summary>
        public float LegacyPointPerAmount;

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
    /// 고지서와 대출. 한 번의 런이 게임 속 하루이며, 고지서는 며칠 뒤 마감을 갖는다.
    /// 마감일까지 못 내면 파산이고, 대출로만 막을 수 있다 (GDD 4절).
    /// </summary>
    [Serializable]
    public class BillConfig
    {
        /// <summary>고지서 기본 납부 기한(일). 단계별 값은 StageDef.DueDays 가 덮어쓴다.</summary>
        public int DueDays;

        /// <summary>대출을 쓸 수 있게 되는 고지서 순번. 첫 고지서는 대출 없이 막아야 한다.</summary>
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

        /// <summary>부수면 회복되는 스태미나. 회복형(tourist)만 0보다 크다.</summary>
        public float StaminaRestore;

        public float MoveSpeed;
        public float TurnIntervalSec;

        /// <summary>파괴 시 뽑는 코인 개수 (이슈 #178). coins.csv 가중치로 이만큼 추첨한다.</summary>
        public int CoinCount;

        /// <summary>이 값 이상의 액면만 추첨 후보가 된다 (coins.csv 의 CoinDef.Value 기준, 이슈 #178).</summary>
        public string MinDenomId;

        /// <summary>타격마다 남은 내구도와 무관하게 즉시 파괴될 확률 0~1 (#293). 피냐타형만 0보다 크다.</summary>
        public float InstantBreakChance;

        /// <summary>분노 시 돌진 속도 (world-unit/sec, #297). 0 이면 분노하지 않는다. 화난 저금통만 0보다 크다.</summary>
        public float ChargeSpeed;

        /// <summary>돌진 충돌 피해 = 분노시킨 타격의 피해(호버 최종 파워) × 이 값 (#297). 원작 0.7.</summary>
        public float ChargeDamageRatio;
    }

    /// <summary>
    /// 코인 액면 하나. coins.csv 에서 그대로 읽는다 (이슈 #178).
    /// </summary>
    [Serializable]
    public class CoinDef
    {
        public string Id;
        public int Value;

        /// <summary>추첨 시 상대 가중치. 클수록 자주 나온다.</summary>
        public int Weight;

        /// <summary>결과 화면 액면 칩 색상. 16진 문자열(coins.csv display_color)을 그대로 둔다 — 파싱은 UI가 필요할 때 한다.</summary>
        public string DisplayColor;
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
        AutoHammerProcChance,
        AutoHammerSwingSec,
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
        ExtraSpawnChance,
    }

    public enum EffectType
    {
        /// <summary>기준값에 그대로 더한다. value 0.35 → +0.35</summary>
        Add,
        /// <summary>기준값의 백분율을 더한다. value 2 → +2%</summary>
        Percent,
    }

    [Serializable]
    /// <summary>
    /// 스탯 하나에 얹는 효과. **업그레이드와 반지가 함께 쓴다** (이슈 #183) — 계산이 같아서
    /// 형을 나누지 않았다. 이름은 먼저 생긴 쪽을 따른다.
    /// </summary>
    public class UpgradeEffect
    {
        public StatId Stat;
        public EffectType Type;
        public float ValuePerLevel;
    }

    /// <summary>
    /// 반지 한 종류 (이슈 #183). 업그레이드와 같은 모양이지만 **사는 화폐가 다르다** —
    /// 이쪽은 레거시 포인트로 사고, 파산해도 레벨이 남는다.
    /// 효과는 UpgradeEffect 를 그대로 쓴다. 스탯에 얹는 계산이 완전히 같아서,
    /// 형만 새로 파면 GetStat 합성 코드가 두 벌이 된다.
    /// </summary>
    [Serializable]
    public class RingDef
    {
        public string Id;
        public string DisplayName;
        public string Description;

        /// <summary>레거시 포인트 단위다. 코인이 아니다.</summary>
        public long InitCost;

        public float CostGrowth;
        public int MaxLevel;
        public int SortOrder;
        public List<UpgradeEffect> Effects = new();
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
        public long BillAmount;

        /// <summary>이 단계의 고지서 납부 기한(일). 단계가 오르면 짧아진다.</summary>
        public int DueDays;

        public int SpawnCount;
    }

    /// <summary>
    /// 한 단계에서 한 종류가 뽑힐 상대 가중치. stage_spawns.csv 한 행이다 (#293).
    /// </summary>
    [Serializable]
    public class StageSpawnDef
    {
        public int Stage;
        public string TargetId;
        public float Ratio;
    }

    /// <summary>
    /// 고지서 조기 납부(4.2) 보상 4종. id 는 스네이크 케이스 그대로 PerkType 이름이 된다 —
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
    public class BillNameDef
    {
        public string Id;
        public string Issuer;
        public string Title;
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
