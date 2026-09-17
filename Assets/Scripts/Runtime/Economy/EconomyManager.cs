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
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class EconomyManager : MonoBehaviour, IEconomyService, IRunScoped, IWalletPersistence
    {
        [SerializeField] private BalanceData _balanceData;

        private readonly CoinWallet _wallet = new CoinWallet();

        /// <summary>업그레이드 레벨과 비용. BalanceData 가 있어야 만들 수 있어 Awake 에서 늦게 만든다.</summary>
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

        /// <summary>업그레이드의 현재 레벨. 0 이면 아직 사지 않은 것이다.</summary>
        public int GetUpgradeLevel(string upgradeId)
        {
            return _upgrades == null ? 0 : _upgrades.GetLevel(upgradeId);
        }

        public bool IsUpgradeMaxLevel(string upgradeId)
        {
            return _upgrades == null || _upgrades.IsMaxLevel(upgradeId);
        }

        /// <summary>다음 레벨 비용. 최대 레벨이거나 없는 id 면 false 다.</summary>
        public bool TryGetUpgradeCost(string upgradeId, out long cost)
        {
            cost = 0L;
            return _upgrades != null && _upgrades.TryGetNextCost(upgradeId, out cost);
        }

        /// <summary>
        /// 업그레이드를 한 레벨 산다. 코인 차감과 레벨업이 **함께 성공하거나 함께 실패한다** —
        /// 코인만 빠지고 레벨이 안 오르는 일이 없도록 여기 한 곳에서 처리한다.
        ///
        /// 구매는 메뉴·결과 화면에서만 하고 효과는 다음 런부터 적용된다 (BALANCE 6절).
        /// 런 도중에 부르지 않는 것은 호출측 책임이다.
        /// </summary>
        /// <returns>실제로 샀으면 true. 코인 부족·최대 레벨·없는 id 면 false</returns>
        public bool TryPurchaseUpgrade(string upgradeId)
        {
            if (_upgrades == null || !_upgrades.TryGetNextCost(upgradeId, out var cost))
            {
                return false;
            }

            // 잔액 확인과 차감을 지갑 한 곳에 맡긴다. 여기서 먼저 비교하면 둘이 어긋날 수 있다.
            if (!_wallet.TrySpendCoin(cost))
            {
                return false;
            }

            _upgrades.LevelUp(upgradeId);
            GameEvents.PublishBalanceChanged(_wallet.CurrentCoin);
            return true;
        }

        /// <summary>
        /// 업그레이드가 적용된 실효값. 기준값은 호출측이 넘긴다 (BALANCE 6절 표).
        /// spawn_count 처럼 기준값이 단계마다 다른 stat 이 있어 한 곳에서 꺼낼 수 없다.
        /// </summary>
        public float GetUpgradedStat(StatId stat, float baseValue)
        {
            return _upgrades == null ? baseValue : _upgrades.GetStat(stat, baseValue);
        }

        /// <summary>저장용 업그레이드 레벨. SaveData.UpgradeLevels 에 그대로 넣는다.</summary>
        public int[] CurrentUpgradeLevels => _upgrades == null ? new int[0] : _upgrades.ToArray();

        /// <summary>저장 데이터에서 업그레이드 레벨을 되살린다. SaveManager 가 초기화 때 부른다.</summary>
        public void RestoreUpgradeLevels(int[] levels)
        {
            if (_upgrades == null)
            {
                return;
            }
            if (levels == null || levels.Length != _upgrades.Count)
            {
                Debug.LogWarning("[EconomyManager] 저장된 업그레이드 레벨 수가 upgrades.csv 와 다르다. " +
                                 "겹치는 만큼만 복원한다.");
            }
            _upgrades.RestoreLevels(levels);
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
    }
}
