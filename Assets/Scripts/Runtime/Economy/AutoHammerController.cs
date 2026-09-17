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
        private int _bonusCount;

        /// <summary>기준값 + 업그레이드 보너스. 업그레이드 통로는 #116 계약이 확정되기 전까지 SetBonusCount 로만 받는다.</summary>
        public int AutoHammerCount =>
            (_balanceData != null ? _balanceData.Economy.AutoHammerCountInit : 0) + _bonusCount;

        private void Awake()
        {
            if (_balanceData == null)
            {
                Debug.LogError("[AutoHammerController] BalanceData 가 연결되지 않았다. " +
                               "자동 망치가 작동하지 않으니 Managers 프리팹의 참조를 확인하라.");
            }
        }

        /// <summary>
        /// 런을 시작한다. GameManager 가 런 시작 직전에 부른다 (IRunScoped, 이슈 #71).
        /// 이걸 부르기 전에는 틱이 돌지 않는다 — MainMenu 에서 대상 없이 헛돌지 않게 막는다.
        /// </summary>
        public void BeginRun()
        {
            _tickTimer = 0f;
            _isRunning = true;
        }

        /// <summary>런을 종료한다. GameManager 가 Result 전이 시 부른다 (IRunScoped, 이슈 #111).</summary>
        public void EndRun()
        {
            _isRunning = false;
        }

        /// <summary>
        /// 업그레이드가 보유 수를 늘릴 때 쓰는 주입 통로. CreatureManager.SetUpgradeOverrides,
        /// EconomyManager.SetBillService 와 같은 일반 메서드 주입 방식이라 새 공용 인터페이스가 필요 없다.
        /// </summary>
        public void SetBonusCount(int bonusCount)
        {
            _bonusCount = Mathf.Max(0, bonusCount);
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
