using NCAIClicker.Data;
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
        private float _currentHp;
        private decimal _rawCoin;
        private float _staminaRestore;
        private bool _isAlive;

        public bool IsAlive => _isAlive;

        public string TargetId => _targetId;

        /// <summary>연출이 스케일할 트랜스폼. 메시 자체가 아니라 그 부모까지만 노출한다.</summary>
        public Transform Visual => _visual;

        private void Awake()
        {
            Initialize();
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

            // 원시 보상은 남은 내구도가 아니라 초기 최대 내구도로 계산한다
            // (ARCHITECTURE 코인 계산·정산 계약 2번).
            _currentHp = def.Hp;
            _rawCoin = def.Hp * (decimal)def.CoinMult + def.BreakBonus;
            _staminaRestore = def.StaminaRestore;
            _isAlive = true;

            ApplyHitRadius();
        }

        /// <summary>
        /// 피격 반경을 기준 반경보다 넓힌다. 확대 비율은 economy.csv 의 hit_radius_bonus 다 (GDD 4절).
        /// 메시를 참조하지 않으므로 작업 6.6 에서 모델을 갈아끼워도 판정 크기가 변하지 않는다.
        /// </summary>
        private void ApplyHitRadius()
        {
            if (_hitCollider == null)
            {
                return;
            }

            var bonus = 1f + _balanceData.Economy.HitRadiusBonusPercent / 100f;
            _hitCollider.radius = _baseHitRadius * bonus;
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
            if (_currentHp > 0f)
            {
                return;
            }

            // 살아 있음 → 파괴됨으로 바뀌는 순간에만, 한 번만 발행한다 (계약 2번).
            _isAlive = false;
            GameEvents.PublishTargetBroken(
                new BreakInfo(_targetId, _rawCoin, _staminaRestore, transform.position));
        }
    }
}
