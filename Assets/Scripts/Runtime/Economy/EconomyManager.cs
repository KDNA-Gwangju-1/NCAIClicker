using System;
using System.Collections.Generic;
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
        IUpgradeStats, IUpgradeShop, IUpgradePersistence, ILegacyService, IRingShop, ILegacyPersistence,
        IUnlockPersistence
    {
        /// <summary>
        /// 코인 조회 통로. BillManager.Instance(IBillService)·SaveManager.Instance(ISaveService)·
        /// GameManager.Instance(IGameFlowService) 와 같은 패턴으로 **공용 인터페이스 타입으로만** 연다
        /// — 구현 클래스를 밖에 노출하지 않는다 (AGENTS.md).
        ///
        /// UI 가 OnEnable 에서 초기 상태를 한 번 읽는 용도다 (ARCHITECTURE 3절). 이후 갱신은
        /// OnBalanceChanged·OnRunCoinChanged 구독으로 받는다. BeginRun() 은 런 순수입만 발행하고
        /// 잔액은 발행하지 않아, 이 통로가 없으면 UI 가 런 시작 시 잔액을 알 방법이 없었다 (#171).
        /// </summary>
        public static IEconomyService Instance { get; private set; }

        /// <summary>
        /// 업그레이드 구매 창구. 레벨·다음 비용 조회와 구매를 UI 가 이 통로로만 한다 (#171).
        /// Instance 와 나눠 둔 이유는 소비처가 다르기 때문이다 — HUD 는 지갑만, 상점 화면(6.8)은
        /// 이쪽만 쓴다. 한 통로로 묶어 캐스팅하게 두면 인터페이스를 나눈 의미가 없어진다.
        /// </summary>
        public static IUpgradeShop Shop { get; private set; }

        /// <summary>
        /// 반지 구매 창구 (#183). Shop 과 나눠 둔 이유는 **쓰는 화폐가 다르기** 때문이다 —
        /// 업그레이드는 코인, 반지는 레거시 포인트다. 한 통로로 묶으면 화면이 어느 화폐를
        /// 쓰는지 캐스팅으로 판단하게 되고, 계약을 나눈 의미가 없어진다.
        /// </summary>
        public static IRingShop RingShop { get; private set; }

        /// <summary>
        /// 레거시 포인트 조회 창구 (#175). 반지 상점이 잔액 표시에 쓴다.
        /// 코인 잔액(Instance)과 나눠 둔다 — 파산 시 한쪽만 사라지는 서로 다른 화폐다.
        /// </summary>
        public static ILegacyService Legacy { get; private set; }

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

        /// <summary>
        /// 파산을 넘어 남는 영구 화폐 (이슈 #175). **회차 초기화에서 건드리지 않는다** —
        /// BillManager 의 파산 처리는 IWalletPersistence 로 코인만 비우므로 여기는 그대로 남는다.
        /// </summary>
        private long _legacyPoints;

        /// <summary>
        /// 반지의 계산부 (#183). 업그레이드와 나란히 둔다 — 레벨·비용·효과 계산은 같고
        /// 화폐(레거시 포인트)와 저장 수명(파산해도 남는다)만 다르다.
        /// </summary>
        private RingState _rings;

        private bool _isRunning;
        private long _lastPublishedRunCoin;

        /// <summary>이번 회차 누적 순수입 (#301). 크리처 해금 판정의 유일한 입력이다.</summary>
        private long _earnedTotal;

        /// <summary>
        /// 이번 런의 액면별 누적 개수 (이슈 #178). coins.csv 순서와 무관하게 파괴 순으로 쌓이므로,
        /// 조회 쪽(ResultUIController)이 BalanceData.Coins 순서로 다시 정렬해 읽는다.
        /// </summary>
        private readonly Dictionary<string, int> _runDenomCounts = new Dictionary<string, int>();

        public long CurrentCoin => _wallet.CurrentCoin;
        public long RunCoin => _wallet.RunCoin;
        public long EarnedTotal => _earnedTotal;

        public IReadOnlyList<CoinDrop> RunCoinBreakdown
        {
            get
            {
                var list = new List<CoinDrop>(_runDenomCounts.Count);
                foreach (var pair in _runDenomCounts)
                {
                    list.Add(new CoinDrop(pair.Key, pair.Value));
                }
                return list;
            }
        }

        /// <summary>저장할 소수 잔여. SaveData.CoinRemainder 에 그대로 넣는다.</summary>
        public string CurrentRemainderText => _wallet.RemainderText;

        private void Awake()
        {
            // 조회 통로는 BalanceData 검사보다 **먼저** 연다. BillManager.Awake 와 같은 순서다 —
            // 데이터가 없어 매니저가 제 일을 못 하더라도 통로 자체는 있어야, 소비처가
            // "매니저가 없다"와 "데이터가 없다"를 구별할 수 있다.
            Instance = this;
            Shop = this;
            RingShop = this;
            Legacy = this;

            if (_balanceData == null)
            {
                Debug.LogError("[EconomyManager] BalanceData 가 연결되지 않았다. " +
                               "배율을 1로 두고 진행하니 Managers 프리팹의 참조를 확인하라.");
                return;
            }

            _upgrades = new UpgradeState(_balanceData);
            _rings = new RingState(_balanceData);
            CacheUpgradedMultipliers();
        }

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다. 빠뜨리면 코인이 두 배로 들어온다 (AGENTS.md).
        private void OnEnable()
        {
            GameEvents.OnTargetBroken += HandleTargetBroken;
            GameEvents.OnFeverStart += HandleFeverStart;
            GameEvents.OnFeverEnd += HandleFeverEnd;
            GameEvents.OnPerkChosen += HandlePerkChosen;
            GameEvents.OnBillPaid += HandleBillPaid;
        }

        private void OnDisable()
        {
            GameEvents.OnTargetBroken -= HandleTargetBroken;
            GameEvents.OnFeverStart -= HandleFeverStart;
            GameEvents.OnFeverEnd -= HandleFeverEnd;
            GameEvents.OnPerkChosen -= HandlePerkChosen;
            GameEvents.OnBillPaid -= HandleBillPaid;
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
            _runDenomCounts.Clear();

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
            // 정산 때 이번 런 순수입을 회차 누적에 더하고, 새로 기준을 넘은 크리처를 알린다 (#301).
            // 결과 화면은 EndRun 뒤에 열리므로(GameManager.NotifyEndRun) 방금 번 돈까지 반영된 값을 본다.
            if (_isRunning)
            {
                AccumulateEarned(_wallet.RunCoin);
            }

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

        private void AccumulateEarned(long runCoin)
        {
            if (runCoin <= 0L)
            {
                return;
            }

            var before = _earnedTotal;
            _earnedTotal += runCoin;
            if (_balanceData == null)
            {
                return;
            }

            foreach (var target in _balanceData.GetUnlockOrder())
            {
                if (before < target.UnlockEarned && _earnedTotal >= target.UnlockEarned)
                {
                    GameEvents.PublishCreatureUnlocked(target.Id);
                }
            }
        }

        /// <summary>저장 복원·파산 초기화 (#301). 이벤트는 내지 않는다 — 복원은 해금 "순간" 이 아니다.</summary>
        public void RestoreEarnedTotal(long earnedTotal)
        {
            _earnedTotal = earnedTotal < 0L ? 0L : earnedTotal;
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
            AccumulateDenomCounts(info.Coins);
        }

        /// <summary>
        /// 결과 화면의 액면별 개수 조회용 누적. 지갑 계산과는 무관하다 — RawCoin 은 이미
        /// CoinLottery 가 합산해 넘겨준 값이라 여기서 다시 계산하지 않는다.
        /// </summary>
        private void AccumulateDenomCounts(IReadOnlyList<CoinDrop> coins)
        {
            if (coins == null)
            {
                return;
            }
            for (var i = 0; i < coins.Count; i++)
            {
                var drop = coins[i];
                _runDenomCounts.TryGetValue(drop.DenomId, out var existing);
                _runDenomCounts[drop.DenomId] = existing + drop.Count;
            }
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
            var afterUpgrades = _upgrades == null ? baseValue : _upgrades.GetStat(stat, baseValue);

            // **업그레이드 먼저, 반지 나중.** 반지는 파산을 넘어 남는 영구 층이라 회차 성장
            // 위에 얹힌다는 3층 구조(#183)를 순서로 못 박는다. 지금 반지 효과가 전부 add 라
            // 순서를 바꿔도 값이 같지만, percent 효과가 생기는 순간 결과가 갈린다.
            return _rings == null ? afterUpgrades : _rings.GetStat(stat, afterUpgrades);
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

        // ---------------------------------------------------------------- 레거시 포인트 (#175)

        public long CurrentLegacyPoints => _legacyPoints;

        /// <summary>
        /// 고지서를 내면 적립한다. **BillManager 를 건드리지 않는다** — OnBillPaid 가 Bill 을
        /// 통째로 실어 주므로 여기서 구독해 금액을 읽는다 (ARCHITECTURE 3절 이벤트 버스).
        ///
        /// **나머지는 버린다.** 원작이 "쓴 금액 50당 1점"이라 내림이 규칙이고, 코인처럼 소수
        /// 잔여를 이월하지 않는다. 고지서 금액이 계수보다 작으면 한 푼도 안 쌓인다 —
        /// 계수가 잠정값이라 3.8(#176) 재계산에서 이 경계를 같이 본다.
        /// </summary>
        private void HandleBillPaid(Bill bill)
        {
            if (bill == null || _balanceData == null)
            {
                return;
            }

            var perPoint = _balanceData.Economy.LegacyPointPerAmount;
            if (perPoint <= 0f)
            {
                return;
            }

            var earned = (long)Math.Floor(bill.Amount / (double)perPoint);
            AddLegacyPoints(earned);
        }

        public void AddLegacyPoints(long amount)
        {
            if (amount <= 0L)
            {
                return;
            }
            _legacyPoints += amount;
        }

        public bool TrySpendLegacyPoints(long amount)
        {
            if (amount <= 0L || _legacyPoints < amount)
            {
                return false;
            }
            _legacyPoints -= amount;
            return true;
        }

        /// <summary>
        /// 저장에서 되돌린다. **ringLevelsBySortOrder 는 null 로 올 수 있다** — v2 이하 저장에는
        /// 이 배열이 없고 JsonUtility 가 빈 배열이 아니라 null 로 되살린다 (SaveManager 의 case 2).
        /// </summary>
        public void RestoreLegacy(long points, int[] ringLevelsBySortOrder)
        {
            _legacyPoints = points < 0L ? 0L : points;
            _rings?.RestoreLevels(ringLevelsBySortOrder);
        }

        /// <summary>SaveManager 가 저장 직전에 읽어 SaveData.RingLevels 에 그대로 넣는다.</summary>
        public int[] CurrentRingLevels => _rings == null ? Array.Empty<int>() : _rings.ToArray();

        // ---------------------------------------------------------------- IRingShop (#183)

        public int GetRingLevel(string ringId)
        {
            return _rings == null ? 0 : _rings.GetLevel(ringId);
        }

        /// <summary>
        /// 다음 반지 레벨의 **레거시 포인트** 비용. 최대 레벨이거나 없는 id 면 더 살 수 없다는
        /// 뜻으로 long.MaxValue 를 돌려준다 — IUpgradeShop.GetNextCost 와 같은 약속이다.
        /// </summary>
        public long GetNextRingCost(string ringId)
        {
            if (_rings == null || !_rings.TryGetNextCost(ringId, out var cost))
            {
                return long.MaxValue;
            }
            return cost;
        }

        /// <summary>
        /// 반지를 한 레벨 산다. **코인이 아니라 레거시 포인트를 쓴다** — 두 화폐가 섞이지
        /// 않도록 차감은 TrySpendLegacyPoints 하나로만 한다.
        /// 차감과 레벨업이 함께 성공하거나 함께 실패한다 (TryPurchase 와 같은 규칙).
        /// </summary>
        public bool TryPurchaseRing(string ringId)
        {
            // 반지는 파산 후 프레스티지 구간에서만 산다 (이슈 #291). **UI 가 아니라 여기서 막는다** —
            // 규칙이 화면에만 있으면 반지 상점이 다른 화면에 생길 때 다시 샌다.
            //
            // 고지서 서비스가 연결돼 있지 않아도 실패한다. 연결이 빠진 것을 "제한 없음"으로
            // 읽으면 조립이 틀렸을 때 규칙이 조용히 사라진다.
            if (_billService == null || !_billService.IsPrestigeWindowOpen)
            {
                return false;
            }

            if (_rings == null || !_rings.TryGetNextCost(ringId, out var cost))
            {
                return false;
            }
            if (!TrySpendLegacyPoints(cost))
            {
                return false;
            }

            _rings.LevelUp(ringId);
            return true;
        }
    }
}
