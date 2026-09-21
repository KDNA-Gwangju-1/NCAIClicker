using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.Targets
{
    /// <summary>
    /// 타격 대상의 내구도와 피격을 담당한다. 이동과 상태 전이는 작업 2.2 가 맡는다.
    ///
    /// 프리팹은 로직을 루트에, 메시를 Visual 자식에 둔다 (AGENTS.md).
    /// 그레이박스를 3D 에셋으로 갈아끼울 때(작업 6.6) 이 스크립트를 건드리지 않기 위한 구조다.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class Target : MonoBehaviour, IHittable
    {
        /// <summary>targets.csv 의 id. 수치는 전부 여기서 조회한다.</summary>
        [SerializeField] private string _targetId;

        [SerializeField] private BalanceData _balanceData;

        /// <summary>
        /// 메시를 담는 빈 자식. 연출(작업 6.3)이 스쿼시·스트레치로 여기를 스케일한다.
        /// 루트를 스케일하면 콜라이더까지 커지므로 건드리지 않는다 (ASSET_PIPELINE 1절).
        /// </summary>
        [SerializeField] private Transform _visual;

        /// <summary>
        /// 피격 판정의 기준 반경. **메시 크기에서 재지 않는다** —
        /// 모델이 바뀌어도 판정 크기는 그대로여야 밸런스가 유지된다 (ASSET_PIPELINE 1절).
        /// 그레이박스 높이 0.4 유닛의 절반을 기본값으로 둔다.
        /// </summary>
        [SerializeField] private float _baseHitRadius = 0.2f;

        private SphereCollider _hitCollider;

        /// <summary>
        /// 업그레이드 실효값 조회 통로 (#116). 스폰하는 쪽(CreatureManager)이 Initialize 전에 넣어 준다.
        /// 없으면 CSV 기준값을 그대로 쓴다 — 씬에 손으로 놓은 대상도 그대로 동작해야 한다 (#131).
        /// </summary>
        private IUpgradeStats _upgradeStats;

        /// <summary>
        /// 피격 판정 확대 퍼크가 더하는 비율(percent). 스폰하는 쪽이 넣어 준다 (#126).
        /// 이 컴포넌트가 OnPerkChosen 을 직접 구독하지 않는 이유는 인스턴스가 여럿이기 때문이다 —
        /// 화면의 크리처 수만큼 구독자가 생기고, 해제를 한 번만 놓쳐도 누수가 된다.
        /// </summary>
        private float _perkHitRadiusPercent;
        private float _currentHp;
        private float _staminaRestore;
        private bool _isAlive;

        /// <summary>
        /// 액면 추첨용 공유 난수기 (이슈 #178). 인스턴스마다 새로 만들면 같은 프레임에 여럿이
        /// 죽을 때 시드가 겹쳐 같은 결과만 나올 수 있어, 타입 전체가 하나를 공유한다.
        /// </summary>
        private static readonly System.Random _coinRandom = new System.Random();

        public bool IsAlive => _isAlive;

        public string TargetId => _targetId;

        public float CurrentHp => _currentHp;

        public float MaxHp { get; private set; }

        /// <summary>연출이 스케일할 트랜스폼. 메시 자체가 아니라 그 부모까지만 노출한다.</summary>
        public Transform Visual => _visual;

        private void Awake()
        {
            Initialize();
        }

        private void Start()
        {
            if (GetComponent<CreatureHpDisplay>() == null)
            {
                gameObject.AddComponent<CreatureHpDisplay>();
            }
            if (GetComponent<TargetHitEffects>() == null)
            {
                gameObject.AddComponent<TargetHitEffects>();
            }
        }

        /// <summary>
        /// 업그레이드 실효값 조회 통로를 넣는다. **Initialize 보다 먼저** 불러야 판정 반경에 반영된다.
        /// 서비스 계약이 아니라 조립(wiring) 통로다 (ARCHITECTURE "SetBillService" 문단).
        /// </summary>
        public void SetUpgradeStats(IUpgradeStats upgradeStats)
        {
            _upgradeStats = upgradeStats;
        }

        /// <summary>
        /// 퍼크로 늘어난 판정 반경 비율을 넣는다. 즉시 반경에 반영하므로 이미 살아 있는
        /// 크리처에도 런 도중 적용할 수 있다 (#126).
        /// </summary>
        public void SetPerkHitRadiusPercent(float percent)
        {
            _perkHitRadiusPercent = percent;
            ApplyHitRadius();
        }

        /// <summary>
        /// targets.csv 값으로 되돌린다. 스폰과 오브젝트 풀 재사용에서 다시 부를 수 있도록 공개한다.
        /// Awake 가 돌았는지에 기대지 않는다 — 풀에서 꺼내 쓰는 쪽이 이 메서드만 불러도 온전해야 한다.
        /// </summary>
        public void Initialize()
        {
            _isAlive = false;

            if (_hitCollider == null)
            {
                _hitCollider = GetComponent<SphereCollider>();
            }

            if (_balanceData == null)
            {
                Debug.LogError($"[Target] '{_targetId}' 에 BalanceData 가 연결되지 않았다. 프리팹 참조를 확인하라.");
                return;
            }

            var def = _balanceData.GetTarget(_targetId);
            if (def == null)
            {
                Debug.LogError($"[Target] targets.csv 에 id '{_targetId}' 가 없다. 프리팹의 _targetId 를 확인하라.");
                return;
            }

            // 내구도는 초기 최대 내구도로 계산한다 (ARCHITECTURE 코인 계산·정산 계약 2번).
            // 코인 액면은 여기서 정하지 않는다 — 파괴되는 순간(OnHit)에 추첨해야
            // "같은 저금통도 부술 때마다 다르게" 나온다 (이슈 #178, CoinLottery).
            _currentHp = def.Hp;
            MaxHp = def.Hp;
            _staminaRestore = def.StaminaRestore;
            _isAlive = true;

            ApplyHitRadius();
        }

        /// <summary>
        /// 피격 반경을 기준 반경보다 넓힌다. 확대 비율은 economy.csv 의 hit_radius_bonus 이고,
        /// 업그레이드(악력 단련)가 그 비율을 더 올린다 (BALANCE 6절 hit_radius, #131).
        /// 메시를 참조하지 않으므로 작업 6.6 에서 모델을 갈아끼워도 판정 크기가 변하지 않는다.
        /// </summary>
        private void ApplyHitRadius()
        {
            if (_hitCollider == null)
            {
                return;
            }

            if (_balanceData == null)
            {
                return;
            }

            var basePercent = _balanceData.Economy.HitRadiusBonusPercent;
            var percent = _upgradeStats == null
                ? basePercent
                : _upgradeStats.GetStat(StatId.HitRadius, basePercent);
            // 업그레이드와 퍼크는 같은 stat 을 건드리므로 비율끼리 더한다 (BALANCE 6절 percent 합산과 같은 규칙).
            _hitCollider.radius = _baseHitRadius * (1f + (percent + _perkHitRadiusPercent) / 100f);
        }

        /// <summary>피격을 외부(FSM 등)에 알리는 이벤트.</summary>
        public event System.Action<HitInfo> HitReceived;

        private void OnHitReceived(HitInfo info)
        {
            HitReceived?.Invoke(info);
        }

        /// <summary>
        /// 내구도만 깎는다. 코인은 여기서 주지 않는다 — 파괴될 때 한 번에 지급한다 (GDD 4절).
        /// 내구도를 float 로 두는 이유는 자동 망치의 작은 피해도 누적되게 하기 위해서다.
        /// </summary>
        public void OnHit(HitInfo info)
        {
            if (!_isAlive)
            {
                return;
            }

            _currentHp -= info.Damage;

            try
            {
                OnHitReceived(info);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }

            if (_currentHp > 0f)
            {
                return;
            }

            // 살아 있음 → 파괴됨으로 바뀌는 순간에만, 한 번만 발행한다 (계약 2번).
            _isAlive = false;

            // 액면 추첨은 파괴되는 지금 한다 (이슈 #178) — Initialize 에서 미리 정하면
            // 같은 프리팹 인스턴스가 매번 같은 액면만 내놓게 된다.
            var def = _balanceData != null ? _balanceData.GetTarget(_targetId) : null;
            var coins = def != null
                ? CoinLottery.Draw(_balanceData, def.MinDenomId, def.CoinCount, () => _coinRandom.NextDouble())
                : System.Array.Empty<CoinDrop>();
            var rawCoin = CoinLottery.SumValue(coins, _balanceData);

            GameEvents.PublishTargetBroken(
                new BreakInfo(_targetId, rawCoin, coins, _staminaRestore, transform.position));
        }
    }
}
