using System;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// 코인 지급의 유일한 주체. OnTargetBroken 을 받아 배율과 대출 징수를 적용해 지갑에 넣는다.
    /// 계산은 CoinWallet 에 맡기고 여기서는 계수를 모으고 이벤트를 발행하는 일만 한다.
    /// 규칙의 정본은 docs/ARCHITECTURE.md "코인 계산·정산 계약" 3~8번이다.
    ///
    /// 업그레이드 실효값 조회·구매·레벨 저장(IUpgradeStats/IUpgradeShop/IUpgradePersistence)도
    /// 여기서 구현한다. ARCHITECTURE 1절이 "코인, 업그레이드 비용/레벨 계산"을 이 매니저로 배정했다
    /// (이슈 #116). 소비처 배선(StaminaManager 등이 IUpgradeStats 로 갈아타는 것)은 이 이슈 범위 밖이다.
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class EconomyManager : MonoBehaviour, IEconomyService, IRunScoped, IWalletPersistence,
        IUpgradeStats, IUpgradeShop, IUpgradePersistence
    {
        [SerializeField] private BalanceData _balanceData;

        private readonly CoinWallet _wallet = new CoinWallet();

        /// <summary>대출 징수율의 출처. BillManager 가 초기화 때 넣어 준다. 없으면 징수는 0이다.</summary>
        private IBillService _billService;

        private bool _isFeverActive;
        private long _lastPublishedRunCoin;

        /// <summary>업그레이드별 현재 레벨. upgrades.csv sort_order 로 인덱싱한다 (SaveData.UpgradeLevels 와 같은 순서).</summary>
        private int[] _upgradeLevels = Array.Empty<int>();

        public long CurrentCoin => _wallet.CurrentCoin;
        public long RunCoin => _wallet.RunCoin;

        /// <summary>저장할 소수 잔여. SaveData.CoinRemainder 에 그대로 넣는다.</summary>
        public string CurrentRemainderText => _wallet.RemainderText;

        private void Awake()
        {
            if (_balanceData == null)
            {
                Debug.LogError("[EconomyManager] BalanceData 가 연결되지 않았다. " +
                               "배율을 1로 두고 진행하니 Managers 프리팹의 참조를 확인하라.");
            }

            EnsureUpgradeLevelsSize();
        }

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다. 빠뜨리면 코인이 두 배로 들어온다 (AGENTS.md).
        private void OnEnable()
        {
            GameEvents.OnTargetBroken += HandleTargetBroken;
            GameEvents.OnFeverStart += HandleFeverStart;
            GameEvents.OnFeverEnd += HandleFeverEnd;
        }

        private void OnDisable()
        {
            GameEvents.OnTargetBroken -= HandleTargetBroken;
            GameEvents.OnFeverStart -= HandleFeverStart;
            GameEvents.OnFeverEnd -= HandleFeverEnd;
        }

        /// <summary>BillManager 가 자기 자신을 넘겨 준다. 구현 클래스를 직접 참조하지 않기 위한 통로다.</summary>
        public void SetBillService(IBillService billService)
        {
            _billService = billService;
        }

        /// <summary>
        /// 파괴 보상을 지급한다. 인자는 배율 적용 전 원시값이며 호출측은 계수를 곱하지 않는다 (계약 3번).
        /// </summary>
        public void AddCoin(decimal rawAmount)
        {
            var deposited = _wallet.AddEarning(rawAmount, GetFeverMultiplier(), GetBonusMultiplier(),
                                               GetLoanDailyCut());
            if (deposited != 0L)
            {
                GameEvents.PublishCoinEarned(deposited);
                GameEvents.PublishBalanceChanged(_wallet.CurrentCoin);
            }
            PublishRunCoinIfChanged();
        }

        /// <summary>대출 원금 입금. 배율과 징수를 타지 않고 런 순수입에도 들어가지 않는다 (계약 8번).</summary>
        public void AddLoanPrincipal(long amount)
        {
            _wallet.AddLoanPrincipal(amount);
            GameEvents.PublishBalanceChanged(_wallet.CurrentCoin);
        }

        public bool TrySpendCoin(long amount)
        {
            if (!_wallet.TrySpendCoin(amount))
            {
                return false;
            }
            GameEvents.PublishBalanceChanged(_wallet.CurrentCoin);
            return true;
        }

        /// <summary>
        /// 런을 시작한다. 런 순수입만 0으로 되돌리고 지갑 잔액과 소수 잔여는 유지한다.
        /// GameManager 가 런 시작 직전에 부른다.
        /// </summary>
        public void BeginRun()
        {
            _wallet.BeginRun();
            _isFeverActive = false;
            _lastPublishedRunCoin = 0L;
            GameEvents.PublishRunCoinChanged(0L);
        }

        /// <summary>
        /// 런을 종료한다. 런 종료 시 내부 플래그를 정리한다.
        /// GameManager 가 Result 전이 시 부른다 (IRunScoped, 이슈 #111).
        /// </summary>
        public void EndRun()
        {
            _isFeverActive = false;
        }

        /// <summary>저장 데이터에서 지갑을 되살린다. SaveManager 가 초기화 때 부른다.</summary>
        public void RestoreWallet(long balance, string remainderText)
        {
            _wallet.Restore(balance, remainderText);
            _lastPublishedRunCoin = 0L;
            GameEvents.PublishBalanceChanged(_wallet.CurrentCoin);
            GameEvents.PublishRunCoinChanged(0L);
        }

        private void HandleTargetBroken(BreakInfo info)
        {
            AddCoin(info.RawCoin);
        }

        private void HandleFeverStart()
        {
            _isFeverActive = true;
        }

        private void HandleFeverEnd()
        {
            _isFeverActive = false;
        }

        // CSV 의 float 배율은 곱하기 전에 decimal 로 바꾼다 (계약 4번).
        private decimal GetFeverMultiplier()
        {
            if (!_isFeverActive || _balanceData == null)
            {
                return 1m;
            }
            return (decimal)_balanceData.Fever.CoinMultiplier;
        }

        private decimal GetBonusMultiplier()
        {
            if (_balanceData == null)
            {
                return 1m;
            }
            return (decimal)_balanceData.Economy.CoinBonusMultiplier;
        }

        private decimal GetLoanDailyCut()
        {
            if (_billService == null)
            {
                return 0m;
            }
            return (decimal)_billService.LoanDailyCut;
        }

        /// <summary>정수값이 실제로 바뀐 때만 발행한다. 파괴마다 같은 값을 다시 쏘지 않는다.</summary>
        private void PublishRunCoinIfChanged()
        {
            var runCoin = _wallet.RunCoin;
            if (runCoin == _lastPublishedRunCoin)
            {
                return;
            }
            _lastPublishedRunCoin = runCoin;
            GameEvents.PublishRunCoinChanged(runCoin);
        }

        // ---- IUpgradeStats ----

        /// <summary>
        /// 업그레이드가 적용된 실효값. BalanceData 의 고정 기준값을 쓰는 스탯 전용이다.
        /// spawn_count 처럼 기준값이 현재 단계에 따라 달라지는 스탯은 아래 오버로드를 쓴다.
        /// </summary>
        public float GetStat(StatId stat)
        {
            return GetStat(stat, GetBaseValue(stat));
        }

        /// <summary>
        /// 효과값은 base × (1 + percent 합 / 100) + add 합 이며 해당 레벨까지 누적한다 (BALANCE.md 6절).
        /// </summary>
        public float GetStat(StatId stat, float baseValue)
        {
            if (_balanceData == null)
            {
                return baseValue;
            }

            float percentSum = 0f;
            float addSum = 0f;

            var upgrades = _balanceData.Upgrades;
            for (int i = 0; i < upgrades.Count; i++)
            {
                var def = upgrades[i];
                int level = GetLevel(def);
                if (level <= 0)
                {
                    continue;
                }

                var effects = def.Effects;
                for (int e = 0; e < effects.Count; e++)
                {
                    var effect = effects[e];
                    if (effect.Stat != stat)
                    {
                        continue;
                    }

                    var total = effect.ValuePerLevel * level;
                    if (effect.Type == EffectType.Percent)
                    {
                        percentSum += total;
                    }
                    else
                    {
                        addSum += total;
                    }
                }
            }

            return baseValue * (1f + percentSum / 100f) + addSum;
        }

        /// <summary>
        /// 스탯별 BalanceData 고정 기준값. spawn_count 는 단계별 값(StageDef)이라 여기 없다 —
        /// 호출측(스폰 매니저)이 현재 단계 기준값을 GetStat(StatId, baseValue) 오버로드로 넘긴다.
        /// </summary>
        private float GetBaseValue(StatId stat)
        {
            if (_balanceData == null)
            {
                return 0f;
            }

            switch (stat)
            {
                case StatId.BaseHitPower: return _balanceData.Economy.BaseHitPower;
                case StatId.HitRadius: return _balanceData.Economy.HitRadiusBonusPercent;
                case StatId.AutoHammerCount: return _balanceData.Economy.AutoHammerCountInit;
                case StatId.AutoHammerPower: return _balanceData.Economy.AutoHammerPower;
                case StatId.AutoHammerHitsPerSec: return _balanceData.Economy.AutoHammerHitsPerSec;
                case StatId.FeverDuration: return _balanceData.Fever.DurationSec;
                case StatId.FeverMultiplier: return _balanceData.Fever.CoinMultiplier;
                case StatId.FeverGaugePerHit: return _balanceData.Fever.GaugePerHit;
                case StatId.MaxStamina: return _balanceData.Stamina.Max;
                case StatId.IdleDrainPerSec: return _balanceData.Stamina.IdleDrainPerSec;
                case StatId.MoveDrainPerUnit: return _balanceData.Stamina.MoveDrainPerUnit;
                case StatId.HitDrainPerSwing: return _balanceData.Stamina.HitDrainPerSwing;
                case StatId.CoinBonusMultiplier: return _balanceData.Economy.CoinBonusMultiplier;
                case StatId.SpawnIntervalSec: return _balanceData.Economy.SpawnIntervalSec;
                case StatId.SpawnCount:
                    Debug.LogWarning("[EconomyManager] spawn_count 는 단계별 기준값이라 GetStat(StatId) " +
                                     "만으로는 계산할 수 없다. GetStat(StatId, baseValue) 오버로드를 써라.");
                    return 0f;
                default:
                    return 0f;
            }
        }

        // ---- IUpgradeShop ----

        public int GetLevel(string upgradeId)
        {
            var def = _balanceData?.GetUpgrade(upgradeId);
            return def == null ? 0 : GetLevel(def);
        }

        /// <summary>
        /// 비용은 ceil(InitCost × growth^현재레벨). growth 는 UpgradeDef.CostGrowth,
        /// 0 이하면 EconomyConfig.UpgradeCostGrowth 를 쓴다 (BALANCE.md 6절).
        /// 이미 최대 레벨이면 더 살 수 없다는 뜻으로 long.MaxValue 를 돌려준다.
        /// </summary>
        public long GetNextCost(string upgradeId)
        {
            var def = _balanceData?.GetUpgrade(upgradeId);
            if (def == null)
            {
                return long.MaxValue;
            }

            if (GetLevel(def) >= def.MaxLevel)
            {
                return long.MaxValue;
            }

            float growth = def.CostGrowth > 0f ? def.CostGrowth : _balanceData.Economy.UpgradeCostGrowth;
            double cost = def.InitCost * Math.Pow(growth, GetLevel(def));
            return (long)Math.Ceiling(cost);
        }

        /// <summary>
        /// 표시 값과 구매 가능 여부는 전부 GetLevel/GetNextCost 로 조회한 뒤 이걸 부른다.
        /// 지갑 차감은 기존 TrySpendCoin 을 그대로 재사용한다 — 코인 계산 경로를 둘로 만들지 않는다.
        /// </summary>
        public bool TryPurchase(string upgradeId)
        {
            var def = _balanceData?.GetUpgrade(upgradeId);
            if (def == null)
            {
                Debug.LogWarning($"[EconomyManager] 존재하지 않는 업그레이드 id: {upgradeId}");
                return false;
            }

            if (GetLevel(def) >= def.MaxLevel)
            {
                return false;
            }

            var cost = GetNextCost(upgradeId);
            if (!TrySpendCoin(cost))
            {
                return false;
            }

            EnsureUpgradeLevelsSize();
            _upgradeLevels[def.SortOrder]++;
            return true;
        }

        // ---- IUpgradePersistence ----

        /// <summary>저장 데이터에서 업그레이드 레벨을 되살린다. SaveManager 가 초기화 때 부른다.</summary>
        public void RestoreUpgradeLevels(int[] levelsBySortOrder)
        {
            EnsureUpgradeLevelsSize();
            if (levelsBySortOrder == null)
            {
                return;
            }

            var count = Mathf.Min(levelsBySortOrder.Length, _upgradeLevels.Length);
            for (int i = 0; i < count; i++)
            {
                _upgradeLevels[i] = levelsBySortOrder[i];
            }
        }

        /// <summary>SaveManager 가 저장 직전에 읽어 SaveData.UpgradeLevels 에 그대로 넣는다.</summary>
        public int[] CurrentUpgradeLevels
        {
            get
            {
                EnsureUpgradeLevelsSize();
                var copy = new int[_upgradeLevels.Length];
                Array.Copy(_upgradeLevels, copy, _upgradeLevels.Length);
                return copy;
            }
        }

        private int GetLevel(UpgradeDef def)
        {
            EnsureUpgradeLevelsSize();
            if (def.SortOrder < 0 || def.SortOrder >= _upgradeLevels.Length)
            {
                Debug.LogWarning($"[EconomyManager] {def.Id} 의 SortOrder({def.SortOrder}) 가 범위를 벗어났다.");
                return 0;
            }
            return _upgradeLevels[def.SortOrder];
        }

        /// <summary>BalanceData 의 업그레이드 개수에 맞춰 배열 크기를 맞춘다. 레벨 값은 그대로 보존한다.</summary>
        private void EnsureUpgradeLevelsSize()
        {
            int required = _balanceData != null ? _balanceData.Upgrades.Count : 0;
            if (_upgradeLevels.Length == required)
            {
                return;
            }

            var resized = new int[required];
            Array.Copy(_upgradeLevels, resized, Mathf.Min(_upgradeLevels.Length, required));
            _upgradeLevels = resized;
        }
    }
}
