using System.Collections.Generic;
using NCAIClicker.Core;
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

        /// <summary>
        /// 망치마다 이번 틱에 때릴 대상. **틱이 시작될 때** 고른다 (팀장 결정 2026-09-22, #258).
        ///
        /// 망치 10기가 한 대상에 몰리지 않고 각자 다른 대상을 가리킨다. 살아 있는 대상이 보유
        /// 수보다 적으면 겹치는 것을 허용한다 — 때릴 곳이 없다고 쉬게 하면 대상이 하나 남은
        /// 구간에서 자동 망치가 통째로 멈춘다.
        ///
        /// 틱 시작에 고르는 이유는 연출(AutoHammerVisual)이 장전 구간부터 목표를 알아야 그쪽으로
        /// 이동해 내려찍을 수 있어서다. 장전 중에 그 대상이 호버 망치에 부서지면 타격 순간에
        /// 다시 고른다 — **데미지 정확성이 우선**이고 망치가 순간이동하는 것은 드문 경우다.
        /// </summary>
        private readonly List<Target> _pendingTargets = new List<Target>();

        /// <summary>살아 있는 대상을 담는 재사용 버퍼. 틱마다 새 리스트를 만들면 GC 가 쌓인다 (7.1 프로파일링).</summary>
        private readonly List<Target> _aliveBuffer = new List<Target>();

        /// <summary>최종 타격 파워의 출처. 씬 소속이라 런마다 다시 찾는다 (#258).</summary>
        private HammerSwingController _hammerSwing;

        /// <summary>
        /// 이번 틱의 진행도 0~1. 연출이 장전·강타·반동 구간을 나누는 데 쓴다.
        /// 런이 돌지 않거나 주기를 알 수 없으면 0 이다.
        /// </summary>
        public float TickProgress
        {
            get
            {
                var interval = TickInterval;
                return interval <= 0f ? 0f : Mathf.Clamp01(_tickTimer / interval);
            }
        }

        /// <summary>연출이 세울 망치 수. 이번 틱에 실제로 목표를 가진 망치만 센다.</summary>
        public int PendingTargetCount => _pendingTargets.Count;

        /// <summary>index 번째 망치가 이번 틱에 때릴 대상의 위치. 대상이 없으면 false 다.</summary>
        public bool TryGetPendingTargetPosition(int index, out Vector3 position)
        {
            if (index >= 0 && index < _pendingTargets.Count)
            {
                var target = _pendingTargets[index];
                if (target != null && target.IsAlive)
                {
                    position = target.transform.position;
                    return true;
                }
            }

            position = Vector3.zero;
            return false;
        }

        /// <summary>틱 주기(초). hits_per_sec 가 0 이하면 0 이다 — 그때는 타이머가 돌지 않는다.</summary>
        private float TickInterval
        {
            get
            {
                var hitsPerSec = _balanceData != null ? _balanceData.Economy.AutoHammerHitsPerSec : 0f;
                return hitsPerSec <= 0f ? 0f : 1f / hitsPerSec;
            }
        }

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

            // 씬이 다시 로드되면 이전 인스턴스는 파괴돼 있다. 런마다 다시 찾는다.
            _hammerSwing = null;
            EnsureVisual();
        }

        /// <summary>
        /// 틱 연출이 없으면 만든다. HammerSwingController 가 HammerSwingVisual 을 만드는 것과
        /// 같은 방식이다 (#151) — 씬을 건드리지 않고 연출을 붙이기 위해서다.
        ///
        /// 이 매니저는 DontDestroyOnLoad 지만 연출은 씬과 함께 사라지므로 런 시작마다 확인한다.
        /// </summary>
        private static void EnsureVisual()
        {
            if (FindFirstObjectByType<AutoHammerVisual>(FindObjectsInactive.Include) == null)
            {
                var go = new GameObject("AutoHammerVisual (Auto)");
                go.AddComponent<AutoHammerVisual>();
            }
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
            _pendingTargets.Clear();
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

            var interval = TickInterval;
            if (interval <= 0f)
            {
                return;
            }

            // 대상을 미리 골라 두어야 연출이 장전 구간부터 그쪽으로 이동할 수 있다.
            EnsurePendingTargets();

            // 프레임이 밀려도 타격 박자가 어긋나지 않도록 나머지 시간을 이월한다 (0으로 리셋하지 않는다).
            _tickTimer += Time.deltaTime;
            while (_tickTimer >= interval)
            {
                _tickTimer -= interval;
                ResolveTick();

                // 다음 틱의 목표를 곧바로 정한다 — 장전이 이미 시작된 셈이기 때문이다.
                _pendingTargets.Clear();
                EnsurePendingTargets();
            }
        }

        /// <summary>
        /// 자동 망치 1기의 한 방. **호버 망치의 최종 파워를 그대로 받는다** (팀장 결정, #258) —
        /// 업그레이드(완력 단련)와 타격력 강화 퍼크가 자동 망치에도 얹힌다.
        ///
        /// 그전에는 economy.csv 의 auto_hammer_power 를 날것으로 써서 강화를 하나도 받지 않았다.
        /// 완력 단련을 만렙까지 올리면 호버는 8.0 인데 자동 망치는 0.6 에 머물러, 업그레이드를 살수록
        /// 자동 망치가 무의미해지는 구조였다.
        ///
        /// auto_hammer_power 는 이제 **최종 파워에 곱하는 계수**다 (1.0 = 호버와 동일). 죽은 열로
        /// 남기지 않으려고 의미를 바꿔 살려 둔 것이고, 세기를 조절할 때는 CSV 스키마를 건드리지 않고
        /// 이 값만 내리면 된다.
        ///
        /// 호버 컨트롤러는 씬 소속이라 런마다 새로 찾는다. 못 찾으면 업그레이드까지만 얹은 값으로
        /// 떨어진다 — 퍼크는 그쪽이 들고 있어 여기서 알 길이 없다.
        /// </summary>
        private float ResolveHitPower()
        {
            var coefficient = _balanceData.Economy.AutoHammerPower;

            if (_hammerSwing == null)
            {
                _hammerSwing = FindFirstObjectByType<HammerSwingController>(FindObjectsInactive.Include);
            }

            if (_hammerSwing != null)
            {
                return _hammerSwing.RunHitPower * coefficient;
            }

            var basePower = _balanceData.Economy.BaseHitPower;
            var upgraded = _upgradeStats == null
                ? basePower
                : _upgradeStats.GetStat(StatId.BaseHitPower, basePower);
            return upgraded * coefficient;
        }

        /// <summary>
        /// 이번 틱에 망치들이 때릴 대상을 정한다. 보유 수만큼 서로 다른 대상을 고르고, 살아 있는
        /// 대상이 모자라면 겹치는 것을 허용한다 — 때릴 곳이 없다고 쉬게 하면 대상이 하나 남은
        /// 구간에서 자동 망치가 통째로 멈춘다.
        /// </summary>
        private void EnsurePendingTargets()
        {
            var count = AutoHammerCount;
            if (count <= 0)
            {
                _pendingTargets.Clear();
                return;
            }

            // 이미 살아 있는 목표가 보유 수만큼 차 있으면 그대로 둔다 — 틱 안에서 흔들리지 않게.
            if (_pendingTargets.Count == count)
            {
                var allAlive = true;
                for (var i = 0; i < _pendingTargets.Count; i++)
                {
                    if (_pendingTargets[i] == null || !_pendingTargets[i].IsAlive)
                    {
                        allAlive = false;
                        break;
                    }
                }

                if (allAlive)
                {
                    return;
                }
            }

            _pendingTargets.Clear();
            CollectAliveTargets(_aliveBuffer);
            if (_aliveBuffer.Count == 0)
            {
                return;
            }

            // 겹치지 않게 나눠 주기 위해 섞은 뒤 순서대로 배분한다. 보유 수가 더 많으면 앞에서부터
            // 다시 돈다 (Count 로 나머지를 취한다).
            for (var i = _aliveBuffer.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (_aliveBuffer[i], _aliveBuffer[j]) = (_aliveBuffer[j], _aliveBuffer[i]);
            }

            for (var i = 0; i < count; i++)
            {
                _pendingTargets.Add(_aliveBuffer[i % _aliveBuffer.Count]);
            }
        }

        /// <summary>
        /// 틱 한 번을 판정한다. **망치마다 따로 때린다** (팀장 결정 2026-09-22, #258).
        ///
        /// 틱당 총 피해는 여전히 보유 수 × 파워라 GDD 4절 공식은 그대로다 — 맞는 대상만 흩어진다.
        /// 그래서 단일 대상 폭딜이 아니라 여러 대상을 동시에 깎는 쪽이 됐고, 코인이 들어오는
        /// 타이밍이 달라진다 (7.2 실측 항목).
        ///
        /// 대상이 없으면 미스 개념 없이 조용히 넘어간다.
        /// </summary>
        private void ResolveTick()
        {
            if (AutoHammerCount <= 0 || _pendingTargets.Count == 0)
            {
                return;
            }

            var power = ResolveHitPower();
            for (var i = 0; i < _pendingTargets.Count; i++)
            {
                // 장전 중에 호버 망치가 부쉈으면 그 망치만 다시 고른다.
                var target = _pendingTargets[i];
                if (target == null || !target.IsAlive)
                {
                    target = PickRandomAliveTarget();
                }

                if (target == null)
                {
                    continue;
                }

                target.OnHit(new HitInfo(HitSource.AutoHammer, power, target.transform.position));

                // 피버 게이지는 FeverManager 가 호버만 센다 (팀장 결정, #258). 여기서 발행을 멈추지
                // 않는 이유는 정확도 집계(AccuracyHud)와 결과 화면이 같은 이벤트를 듣기 때문이다.
                GameEvents.PublishSwingResolved(HitSource.AutoHammer, true);
            }
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

        /// <summary>
        /// 살아 있는 대상을 버퍼에 모은다. 호출측이 버퍼를 재사용하므로 여기서 새 리스트를
        /// 만들지 않는다 — 틱마다 할당하면 7.1 프로파일링의 GC 항목에 그대로 잡힌다.
        /// </summary>
        private static void CollectAliveTargets(List<Target> buffer)
        {
            buffer.Clear();
            var all = FindObjectsByType<Target>(FindObjectsSortMode.None);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].IsAlive)
                {
                    buffer.Add(all[i]);
                }
            }
        }
    }
}
