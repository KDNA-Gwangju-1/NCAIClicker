using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using NCAIClicker.Targets;
using UnityEngine;

namespace NCAIClicker.Economy
{
    /// <summary>
    /// 자동 망치의 글로벌 타이머 적중을 담당한다. 완료 기준의 정본은 GitHub 이슈 #23.
    ///
    /// 망치마다 개별 타이머를 두지 않는다. 모든 자동 망치가 하나의 글로벌 타이머로 동기화되어
    /// 한 틱에 (보유 수 × 파워)를 한 대상에게 몰아 적용하고, 주기를 1 / 초당타격횟수로 두면
    /// 초당 총 데미지가 GDD 4절 공식(자동 망치 수 × 망치 파워 × 초당 타격 횟수)과 일치한다.
    /// 물리로 추적하지 않고 살아있는 대상 중 하나를 무작위로 골라 무조건 적중시킨다
    /// (GDD: "물리적으로 추적하지 않고, 글로벌 타이머에 따라 무조건 자동 적중으로 가산").
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class AutoHammerController : MonoBehaviour, IRunScoped
    {
        [SerializeField] private BalanceData _balanceData;

        private float _tickTimer;
        private bool _isRunning;

        /// <summary>업그레이드 실효값 조회 통로. ManagerBootstrap 이 넣어 준다 (이슈 #258).</summary>
        private IUpgradeStats _upgradeStats;

        /// <summary>
        /// 런 시작에 굳힌 보유 수. BeginRun 이 채우고 그 뒤로는 이 값만 쓴다 —
        /// 런 도중 레벨이 올라도 이번 런은 변하지 않는다 (BALANCE 6절 "효과는 다음 런부터",
        /// docs/TECH_NOTES/upgrades.md).
        /// </summary>
        private int _cachedCount;

        /// <summary>런 시작에 굳힌 자동 망치 보유 수. 조회용이다.</summary>
        public int AutoHammerCount => _cachedCount;

        /// <summary>업그레이드가 없을 때의 보유 수. economy.csv 의 auto_hammer_count_init 이며 기본은 0 이다.</summary>
        private int BaseCount => _balanceData != null ? _balanceData.Economy.AutoHammerCountInit : 0;

        private void Awake()
        {
            if (_balanceData == null)
            {
                Debug.LogError("[AutoHammerController] BalanceData 가 연결되지 않았다. " +
                               "자동 망치가 작동하지 않으니 Managers 프리팹의 참조를 확인하라.");
            }

            // 첫 BeginRun 전에 조회해도 기준값이 나오게 해 둔다.
            _cachedCount = BaseCount;
        }

        /// <summary>
        /// 런을 시작한다. GameManager 가 런 시작 직전에 부른다 (IRunScoped, 이슈 #71).
        /// 이걸 부르기 전에는 틱이 돌지 않는다 — MainMenu 에서 대상 없이 헛돌지 않게 막는다.
        /// </summary>
        public void BeginRun()
        {
            _tickTimer = 0f;
            _cachedCount = ResolveCount();
            _isRunning = true;
        }

        /// <summary>
        /// 이번 런의 보유 수를 정한다. 주입이 없으면 기준값 그대로다 — 배선이 빠진 화면에서
        /// 게임이 멈추는 것보다 업그레이드만 안 먹는 편이 낫다 (docs/TECH_NOTES/upgrades.md).
        ///
        /// GetStat 은 float 를 돌려주지만 보유 수는 개수라 반올림한다. 음수는 0 으로 막는다 —
        /// CSV 가 손으로 고쳐질 수 있고, 음수 개수는 ResolveTick 의 가드와 의미가 겹친다.
        /// </summary>
        private int ResolveCount()
        {
            var baseCount = BaseCount;
            if (_upgradeStats == null)
            {
                return baseCount;
            }

            return Mathf.Max(0, Mathf.RoundToInt(_upgradeStats.GetStat(StatId.AutoHammerCount, baseCount)));
        }

        /// <summary>런을 종료한다. GameManager 가 Result 전이 시 부른다 (IRunScoped, 이슈 #111).</summary>
        public void EndRun()
        {
            _isRunning = false;
        }

        /// <summary>
        /// 업그레이드 실효값 조회 통로를 넣는다. ManagerBootstrap 이 다른 프리팹 소비처
        /// (StaminaManager·FeverManager·CreatureManager)와 같은 자리에서 넣어 준다 (#131, #258).
        ///
        /// 이전에는 조립 지점이 증분을 계산해 밀어 넣는 SetBonusCount(int) 였다. #131 이 다른
        /// 소비처를 전부 이쪽으로 옮길 때 자동 망치만 남아 **부르는 곳이 없는 채로 방치됐고**,
        /// 그래서 상점에서 살 수는 있지만 게임에는 반영되지 않는 업그레이드가 됐다 (#258).
        /// </summary>
        public void SetUpgradeStats(IUpgradeStats upgradeStats)
        {
            _upgradeStats = upgradeStats;
        }

        private void Update()
        {
            if (!_isRunning || _balanceData == null)
            {
                return;
            }

            var hitsPerSec = _balanceData.Economy.AutoHammerHitsPerSec;
            if (hitsPerSec <= 0f)
            {
                return;
            }

            var interval = 1f / hitsPerSec;

            // 프레임이 밀려도 타격 박자가 어긋나지 않도록 나머지 시간을 이월한다 (0으로 리셋하지 않는다).
            _tickTimer += Time.deltaTime;
            while (_tickTimer >= interval)
            {
                _tickTimer -= interval;
                ResolveTick();
            }
        }

        /// <summary>틱 한 번을 판정한다. 대상이 없으면 미스 개념 없이 조용히 넘어간다.</summary>
        private void ResolveTick()
        {
            var count = AutoHammerCount;
            if (count <= 0)
            {
                return;
            }

            var target = PickRandomAliveTarget();
            if (target == null)
            {
                return;
            }

            var damage = count * _balanceData.Economy.AutoHammerPower;
            target.OnHit(new HitInfo(HitSource.AutoHammer, damage, target.transform.position));

            // 피버 게이지는 호버·자동 망치를 모두 센다. 정확도 집계에서만 소스를 가린다 (ARCHITECTURE 3절).
            GameEvents.PublishSwingResolved(HitSource.AutoHammer, true);
        }

        private static Target PickRandomAliveTarget()
        {
            var all = FindObjectsByType<Target>(FindObjectsSortMode.None);
            if (all.Length == 0)
            {
                return null;
            }

            var aliveCount = 0;
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].IsAlive)
                {
                    aliveCount++;
                }
            }
            if (aliveCount == 0)
            {
                return null;
            }

            var pick = Random.Range(0, aliveCount);
            for (var i = 0; i < all.Length; i++)
            {
                if (!all[i].IsAlive)
                {
                    continue;
                }
                if (pick == 0)
                {
                    return all[i];
                }
                pick--;
            }
            return null;
        }
    }
}
