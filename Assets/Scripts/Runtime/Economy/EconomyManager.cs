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
