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
    /// 여기서 구현한다. 계산 자체는 UpgradeState(#24)에 맡기고 여기서는 창구 역할만 한다 —
    /// Edit Mode가 MonoBehaviour 생명주기를 부르지 않아 계산부를 MonoBehaviour 밖에 둬야
    /// 검증할 수 있기 때문이다 (CoinWallet과 같은 이유, docs/TECH_NOTES/upgrades.md 참고).
    /// ARCHITECTURE 1절이 "코인, 업그레이드 비용/레벨 계산"을 이 매니저로 배정했다 (이슈 #116).
    /// 소비처 배선(StaminaManager 등이 IUpgradeStats 로 갈아타는 것)은 이 이슈 범위 밖이다.
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class EconomyManager : MonoBehaviour, IEconomyService, IRunScoped, IWalletPersistence,
        IUpgradeStats, IUpgradeShop, IUpgradePersistence
    {
        [SerializeField] private BalanceData _balanceData;

        private readonly CoinWallet _wallet = new CoinWallet();

        /// <summary>업그레이드 레벨·비용·실효값 계산부 (#24). BalanceData 가 있어야 만들 수 있어 Awake 에서 늦게 만든다.</summary>
        private UpgradeState _upgrades;

        /// <summary>대출 징수율의 출처. BillManager 가 초기화 때 넣어 준다. 없으면 징수는 0이다.</summary>
        private IBillService _billService;

        private bool _isFeverActive;

        /// <summary>
        /// 런 시작에 굳힌 실효 배율 (#131). 업그레이드 효과는 **다음 런부터** 반영한다
        /// (BALANCE 6절) — 매번 GetStat 을 부르면 런 도중에 값이 바뀔 수 있는 구조가 남는다.
        /// </summary>
        private float _runFeverMultiplier;
        private float _runBonusMultiplier;

        /// <summary>
        /// 위 두 값이 한 번이라도 채워졌는지. **0 을 "아직 안 채움"으로 쓰지 않는다** —
        /// 배율 0 은 코인을 통째로 없애는 값이라 초기화 누락과 구별되지 않으면 조용히 수입이 사라진다.
        /// </summary>
        private bool _hasCachedMultipliers;

        /// <summary>
        /// 코인 획득 강화 퍼크의 배율과 남은 시간 (#126). 코인 배율은 EconomyManager 안에서만
        /// 적용한다는 규칙(AGENTS.md) 때문에 퍼크라도 여기서 곱한다.
        /// 런 밖에서 고른 퍼크는 _pendingPerkMultiplier 에 예약했다가 다음 런 시작부터 센다 (GDD 6절).
        /// </summary>
        private float _perkCoinMultiplier = 1f;
        private float _perkRemainingSec;
        private float _pendingPerkMultiplier;
        private float _pendingPerkDurationSec;

        private bool _isRunning;
        private long _lastPublishedRunCoin;

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
                return;
            }

            _upgrades = new UpgradeState(_balanceData);
            CacheUpgradedMultipliers();
        }

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다. 빠뜨리면 코인이 두 배로 들어온다 (AGENTS.md).
        private void OnEnable()
        {
            GameEvents.OnTargetBroken += HandleTargetBroken;
            GameEvents.OnFeverStart += HandleFeverStart;
            GameEvents.OnFeverEnd += HandleFeverEnd;
            GameEvents.OnPerkChosen += HandlePerkChosen;
        }

        private void OnDisable()
        {
            GameEvents.OnTargetBroken -= HandleTargetBroken;
            GameEvents.OnFeverStart -= HandleFeverStart;
            GameEvents.OnFeverEnd -= HandleFeverEnd;
            GameEvents.OnPerkChosen -= HandlePerkChosen;
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
            CacheUpgradedMultipliers();
            _isFeverActive = false;
            _isRunning = true;
            _lastPublishedRunCoin = 0L;

            // 결과 화면에서 고른 기간제 퍼크는 여기서부터 시간을 센다 (GDD 6절).
            _perkCoinMultiplier = _pendingPerkMultiplier > 0f ? _pendingPerkMultiplier : 1f;
            _perkRemainingSec = _pendingPerkDurationSec;
            _pendingPerkMultiplier = 0f;
            _pendingPerkDurationSec = 0f;

            GameEvents.PublishRunCoinChanged(0L);
        }

        /// <summary>
        /// 런을 종료한다. 런 종료 시 내부 플래그를 정리한다.
        /// GameManager 가 Result 전이 시 부른다 (IRunScoped, 이슈 #111).
        /// </summary>
        public void EndRun()
        {
            _isFeverActive = false;
            _isRunning = false;

            // 런이 끝나면 남은 기간제 퍼크는 버린다. 런 밖에서는 코인이 들어오지 않아
            // 시간만 흘려 보내면 다음 런에 껍데기만 남는다.
            _perkCoinMultiplier = 1f;
            _perkRemainingSec = 0f;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 기간제 퍼크의 남은 시간을 흘린다. Update 에서 분리한 이유는 Edit Mode 검증에서
        /// 경과 시간을 직접 먹여야 하기 때문이다 — Time.deltaTime 은 에디터 프레임에 좌우된다.
        /// </summary>
        private void Tick(float deltaSeconds)
        {
            if (!_isRunning || _perkRemainingSec <= 0f)
            {
                return;
            }

            _perkRemainingSec -= deltaSeconds;
            if (_perkRemainingSec > 0f)
            {
                return;
            }
            _perkRemainingSec = 0f;
            _perkCoinMultiplier = 1f;
        }

        /// <summary>
        /// 코인 획득 강화 퍼크를 받는다. 값과 지속 시간의 출처는 perks.csv 하나다.
        /// 런 도중이면 즉시 시작하고, 밖이면 다음 런 시작까지 예약한다 (GDD 6절).
        /// 같은 퍼크를 또 받으면 덮어쓴다 — 중첩 규칙이 정해져 있지 않다 (docs/TECH_NOTES/perks.md).
        /// </summary>
        private void HandlePerkChosen(string perkId)
        {
            if (_balanceData == null)
            {
                return;
            }

            var perk = _balanceData.GetPerk(perkId);
            if (perk == null || perk.Type != PerkType.CoinGainBoost || perk.DurationSec <= 0f)
            {
                return;
            }

            if (_isRunning)
            {
                _perkCoinMultiplier = perk.Value;
                _perkRemainingSec = perk.DurationSec;
                return;
            }
            _pendingPerkMultiplier = perk.Value;
            _pendingPerkDurationSec = perk.DurationSec;
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

        /// <summary>
        /// 두 배율의 실효값을 한 번에 굳힌다. Awake 와 BeginRun 에서만 부른다.
        /// 피버 **지속 시간** 쪽은 여기서 얹지 않는다 — 지속 시간을 세는 것은 FeverManager 이고,
        /// 그쪽이 IUpgradeStats(#116)를 주입받아 스스로 읽는다.
        /// </summary>
        private void CacheUpgradedMultipliers()
        {
            if (_balanceData == null)
            {
                return;
            }
            _runFeverMultiplier = GetStat(StatId.FeverMultiplier, _balanceData.Fever.CoinMultiplier);
            _runBonusMultiplier = GetStat(StatId.CoinBonusMultiplier, _balanceData.Economy.CoinBonusMultiplier);
            _hasCachedMultipliers = true;
        }

        /// <summary>
        /// 캐시가 비어 있으면 지금 채운다. Awake 가 돌지 않은 경로(런 밖 지급, 에디터 검증)에서
        /// 배율이 0 이 되는 것을 막는다.
        /// </summary>
        private void EnsureCachedMultipliers()
        {
            if (!_hasCachedMultipliers)
            {
                CacheUpgradedMultipliers();
            }
        }

        // CSV 의 float 배율은 곱하기 전에 decimal 로 바꾼다 (계약 4번).
        private decimal GetFeverMultiplier()
        {
            if (!_isFeverActive || _balanceData == null)
            {
                return 1m;
            }
            EnsureCachedMultipliers();
            return (decimal)_runFeverMultiplier;
        }

        private decimal GetBonusMultiplier()
        {
            if (_balanceData == null)
            {
                return 1m;
            }
            EnsureCachedMultipliers();
            // 기간제 퍼크는 보너스 배율에 **곱한다** — perks.csv 의 coin_gain_boost 가
            // "코인 배율에 곱한다"로 연산을 정한다. 업그레이드(coin_bonus_multiplier)는 같은 자리에
            // 더해지므로 연산이 서로 다르다. economy.csv 의 note 가 그 둘을 구분해 적어 두었다.
            return (decimal)_runBonusMultiplier * (decimal)_perkCoinMultiplier;
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
        /// 업그레이드가 적용된 실효값. 기준값은 호출측이 넘긴다(BALANCE 6절 표) — spawn_count 처럼
        /// 기준값이 현재 단계(StageDef)에 따라 달라지는 스탯이 있어 이 계약에서 통일했다
        /// (#116, #24 구현 중 발견해 코멘트로 남김). 계산 자체는 UpgradeState 에 맡긴다.
        /// </summary>
        public float GetStat(StatId stat, float baseValue)
        {
            return _upgrades == null ? baseValue : _upgrades.GetStat(stat, baseValue);
        }

        // ---- IUpgradeShop ----

        public int GetLevel(string upgradeId)
        {
            return _upgrades == null ? 0 : _upgrades.GetLevel(upgradeId);
        }

        /// <summary>
        /// 비용은 ceil(InitCost × growth^현재레벨) (BALANCE.md 6절, 계산은 UpgradeState).
        /// 이미 최대 레벨이거나 없는 id 면 더 살 수 없다는 뜻으로 long.MaxValue 를 돌려준다.
        /// </summary>
        public long GetNextCost(string upgradeId)
        {
            if (_upgrades == null || !_upgrades.TryGetNextCost(upgradeId, out var cost))
            {
                return long.MaxValue;
            }
            return cost;
        }

        /// <summary>
        /// 표시 값과 구매 가능 여부는 전부 GetLevel/GetNextCost 로 조회한 뒤 이걸 부른다.
        /// 지갑 차감은 기존 TrySpendCoin 을 그대로 재사용한다 — 코인 계산 경로를 둘로 만들지 않는다.
        /// 차감과 레벨업이 함께 성공하거나 함께 실패한다 — 비용 조회가 실패하면 코인을 건드리지
        /// 않고, 차감이 실패하면 레벨을 올리지 않는다 (docs/TECH_NOTES/upgrades.md).
        /// </summary>
        public bool TryPurchase(string upgradeId)
        {
            if (_upgrades == null || !_upgrades.TryGetNextCost(upgradeId, out var cost))
            {
                return false;
            }

            if (!TrySpendCoin(cost))
            {
                return false;
            }

            _upgrades.LevelUp(upgradeId);
            return true;
        }

        // ---- IUpgradePersistence ----

        /// <summary>저장 데이터에서 업그레이드 레벨을 되살린다. SaveManager 가 초기화 때 부른다.</summary>
        public void RestoreUpgradeLevels(int[] levelsBySortOrder)
        {
            _upgrades?.RestoreLevels(levelsBySortOrder);
        }

        /// <summary>SaveManager 가 저장 직전에 읽어 SaveData.UpgradeLevels 에 그대로 넣는다.</summary>
        public int[] CurrentUpgradeLevels => _upgrades == null ? Array.Empty<int>() : _upgrades.ToArray();
    }
}
