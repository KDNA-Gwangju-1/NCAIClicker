using NCAIClicker.Data;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.Targets
{
    /// <summary>
    /// 크리처의 책상 평면 2축(XZ) 이동과 FSM 상태 머신을 제어한다 (ARCHITECTURE 4절).
    /// 평상시에는 멈춰 서서 대기(Idle)와 짧은 배회(Moving)를 번갈아 수행하며,
    /// 피격 시 경직(BeingHit) 후 타격 지점 반대 방향으로 도망(Fleeing)친다.
    /// 타격이 누적될수록 공포(패닉)가 누적되어 도망 속도가 가속된다.
    /// 이동 중에는 Visual 자식을 상하로 튀게 하고 루트를 이동 방향으로 돌린다 (#267).
    /// 루트 y 는 고정이라 타격 판정·HP 표시에 영향이 없고, 물리는 쓰지 않는다.
    /// </summary>
    [RequireComponent(typeof(Target))]
    public class CreatureMovement : MonoBehaviour
    {
        [SerializeField] private BalanceData _balanceData;
        [SerializeField] private float _hitStunDuration = 0.15f;
        [SerializeField] private float _fleeDuration = 0.5f;
        [SerializeField] private float _baseIdleDuration = 1.0f;
        [SerializeField] private Bounds _movementBounds;

        [Header("배회·피격 반응 (#267 — 원작 실측)")]
        [Tooltip("방향 전환 시점에 멈춰 서서 대기할 확률")]
        [SerializeField, Range(0f, 1f)] private float _idleChance = 0.25f;
        [Tooltip("피격 시 도망칠 확률. 나머지는 경직만 하고 하던 대로 이동한다")]
        [SerializeField, Range(0f, 1f)] private float _fleeChance = 0.35f;
        [SerializeField] private float _fleeSpeedMultiplierMax = 1.8f;

        [Header("바운스·회전 연출 (#267)")]
        [SerializeField] private float _hopHeight = 0.12f;
        [SerializeField] private float _hopPeriodSec = 0.45f;
        [SerializeField] private float _fleeHopPeriodSec = 0.25f;
        [SerializeField] private float _turnSpeedDegPerSec = 240f;
        [Tooltip("모델 정면이 +Z 가 아닐 때 Y축 보정 각도")]
        [SerializeField] private float _facingOffsetDeg = 0f;

        private Target _target;
        private CreatureState _currentState = CreatureState.Idle;
        private float _moveSpeed;
        private float _turnIntervalSec;
        private float _turnTimer;
        private float _stateTimer;
        private float _idleTimer;
        private Vector3 _currentDirection;
        private float _hopPhase;
        private bool _shouldFleeAfterHit;
        private Vector3 _visualRestLocalPos;
        private bool _hasVisualRestLocalPos;

        // 타격 누적에 따른 패닉 가속 처리
        private int _consecutiveHits;
        private float _comboResetTimer;

        public CreatureState CurrentState => _currentState;
        public Bounds MovementBounds => _movementBounds;
        public Vector3 CurrentDirection => _currentDirection;
        public float MoveSpeed => _moveSpeed;
        public float TurnIntervalSec => _turnIntervalSec;
        public int ConsecutiveHits => _consecutiveHits;
        public float HopHeight => _hopHeight;
        public float FacingOffsetDeg => _facingOffsetDeg;

        private void Awake()
        {
            _target = GetComponent<Target>();
        }

        private void OnEnable()
        {
            if (_target != null)
            {
                _target.HitReceived += HandleHitReceived;
            }
        }

        private void OnDisable()
        {
            if (_target != null)
            {
                _target.HitReceived -= HandleHitReceived;
            }
        }

        /// <summary>
        /// CSV 수치 및 이동 경계로 초기화한다.
        /// </summary>
        public void Initialize(BalanceData balanceData, string targetId, Bounds bounds)
        {
            _balanceData = balanceData;
            _movementBounds = bounds;
            _consecutiveHits = 0;
            _comboResetTimer = 0f;

            if (_balanceData != null)
            {
                var def = _balanceData.GetTarget(targetId);
                if (def != null)
                {
                    _moveSpeed = def.MoveSpeed;
                    _turnIntervalSec = def.TurnIntervalSec;
                }
            }

            PickRandomDirection();
            // 자연스러운 첫 시작을 위해 무작위 대기 시간 부여 후 Idle 시작
            _idleTimer = Random.Range(0.2f, _baseIdleDuration);
            ChangeState(CreatureState.Idle);
        }

        public void SetBounds(Bounds bounds)
        {
            _movementBounds = bounds;
        }

        private void Update()
        {
            if (_target != null && !_target.IsAlive)
            {
                if (_currentState != CreatureState.Idle)
                {
                    ChangeState(CreatureState.Idle);
                }
                return;
            }

            // 피격 콤보 쿨다운 갱신
            if (_consecutiveHits > 0)
            {
                _comboResetTimer -= Time.deltaTime;
                if (_comboResetTimer <= 0f)
                {
                    _consecutiveHits = 0;
                }
            }

            UpdateFSM(Time.deltaTime);
            UpdateVisual(Time.deltaTime);
        }

        /// <summary>
        /// 상태에 따라 Visual 자식을 상하로 튀게 하고 루트를 이동 방향으로 돌린다.
        /// 테스트 가능하도록 deltaTime 을 인자로 받는다.
        /// </summary>
        public void UpdateVisual(float deltaTime)
        {
            var isHopping = _currentState == CreatureState.Moving || _currentState == CreatureState.Fleeing;
            // 에디터 검증처럼 Awake 를 거치지 않은 경우를 위해 지연 조회한다
            if (_target == null)
            {
                _target = GetComponent<Target>();
            }
            var visual = _target != null ? _target.Visual : null;

            if (visual != null)
            {
                if (!_hasVisualRestLocalPos)
                {
                    _visualRestLocalPos = visual.localPosition;
                    _hasVisualRestLocalPos = true;
                }

                var height = 0f;
                if (isHopping)
                {
                    var period = _currentState == CreatureState.Fleeing ? _fleeHopPeriodSec : _hopPeriodSec;
                    if (period > 0f)
                    {
                        _hopPhase += deltaTime / period;
                        _hopPhase -= Mathf.Floor(_hopPhase);
                    }
                    // |sin| 으로 착지 순간이 뾰족한 포물선 모양을 만든다
                    height = _hopHeight * Mathf.Abs(Mathf.Sin(_hopPhase * Mathf.PI));
                }
                else
                {
                    _hopPhase = 0f;
                }

                var pos = _visualRestLocalPos;
                pos.y += height;
                visual.localPosition = pos;
            }

            if (isHopping)
            {
                RotateTowardDirection(deltaTime);
            }
        }

        private void RotateTowardDirection(float deltaTime)
        {
            var flatDir = _currentDirection;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude < 0.001f)
            {
                return;
            }

            var targetRot = Quaternion.LookRotation(flatDir, Vector3.up) * Quaternion.Euler(0f, _facingOffsetDeg, 0f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, _turnSpeedDegPerSec * deltaTime);
        }

        /// <summary>현재 이동 방향을 바라보는 목표 회전. 검증용.</summary>
        public Quaternion GetTargetRotation()
        {
            var flatDir = _currentDirection;
            flatDir.y = 0f;
            return Quaternion.LookRotation(flatDir.sqrMagnitude < 0.001f ? Vector3.forward : flatDir, Vector3.up)
                   * Quaternion.Euler(0f, _facingOffsetDeg, 0f);
        }

        /// <summary>
        /// FSM 상태별 전이와 이동을 갱신한다. 테스트 가능하도록 deltaTime을 인자로 받는다.
        /// </summary>
        public void UpdateFSM(float deltaTime)
        {
            switch (_currentState)
            {
                case CreatureState.Idle:
                    // 멈춰 서서 대기하다가 배회(Moving)로 전환
                    _idleTimer -= deltaTime;
                    if (_idleTimer <= 0f)
                    {
                        PickRandomDirection();
                        ChangeState(CreatureState.Moving);
                    }
                    break;

                case CreatureState.Moving:
                    // 배회 시간 경과 시 대기(Idle) 또는 방향 전환
                    _turnTimer += deltaTime;
                    if (_turnTimer >= _turnIntervalSec && _turnIntervalSec > 0f)
                    {
                        _turnTimer = 0f;
                        // 일정 확률로 멈춰 서서 주변을 살피는 정지 상태로 전이
                        if (Random.value < _idleChance)
                        {
                            _idleTimer = Random.Range(_baseIdleDuration * 0.7f, _baseIdleDuration * 1.3f);
                            ChangeState(CreatureState.Idle);
                            return;
                        }
                        PickRandomDirection();
                    }
                    MoveStep(deltaTime, _moveSpeed);
                    break;

                case CreatureState.BeingHit:
                    // 짧은 경직 후 도망 또는 배회 복귀. 원작은 맞아도 제자리에 머무는 경우가 더 많다 (#267)
                    _stateTimer -= deltaTime;
                    if (_stateTimer <= 0f)
                    {
                        ChangeState(_shouldFleeAfterHit ? CreatureState.Fleeing : CreatureState.Moving);
                    }
                    break;

                case CreatureState.Fleeing:
                    // 타격 누적에 따른 가속 속도로 도망
                    _stateTimer -= deltaTime;
                    var panicMultiplier = Mathf.Clamp(1.2f + (_consecutiveHits * 0.35f), 1.2f, _fleeSpeedMultiplierMax);
                    MoveStep(deltaTime, _moveSpeed * panicMultiplier);
                    if (_stateTimer <= 0f)
                    {
                        ChangeState(CreatureState.Moving);
                    }
                    break;
            }
        }

        private void MoveStep(float deltaTime, float speed)
        {
            if (speed <= 0f)
            {
                return;
            }

            var nextPos = transform.position + _currentDirection * (speed * deltaTime);
            nextPos.y = transform.position.y;
            nextPos = ClampAndBounce(nextPos);
            transform.position = nextPos;
        }

        /// <summary>
        /// 경계를 벗어난 좌표를 클램프하고 반사 벡터를 적용한다.
        /// </summary>
        public Vector3 ClampAndBounce(Vector3 pos)
        {
            if (_movementBounds.size.sqrMagnitude < 0.001f)
            {
                return pos;
            }

            if (pos.x < _movementBounds.min.x)
            {
                pos.x = _movementBounds.min.x;
                _currentDirection.x = Mathf.Abs(_currentDirection.x);
            }
            else if (pos.x > _movementBounds.max.x)
            {
                pos.x = _movementBounds.max.x;
                _currentDirection.x = -Mathf.Abs(_currentDirection.x);
            }

            if (pos.z < _movementBounds.min.z)
            {
                pos.z = _movementBounds.min.z;
                _currentDirection.z = Mathf.Abs(_currentDirection.z);
            }
            else if (pos.z > _movementBounds.max.z)
            {
                pos.z = _movementBounds.max.z;
                _currentDirection.z = -Mathf.Abs(_currentDirection.z);
            }

            _currentDirection.y = 0f;
            if (_currentDirection.sqrMagnitude > 0.001f)
            {
                _currentDirection.Normalize();
            }

            return pos;
        }

        public void ChangeState(CreatureState newState)
        {
            _currentState = newState;
            _stateTimer = 0f;

            switch (newState)
            {
                case CreatureState.BeingHit:
                    _stateTimer = _hitStunDuration;
                    break;

                case CreatureState.Fleeing:
                    _stateTimer = _fleeDuration;
                    break;

                case CreatureState.Moving:
                    _turnTimer = 0f;
                    break;

                case CreatureState.Idle:
                    break;
            }
        }

        private void HandleHitReceived(HitInfo info)
        {
            // 타격 누적 및 쿨다운 리셋 타이머 갱신
            _consecutiveHits++;
            _comboResetTimer = 1.5f;

            // 일정 확률로만 도망친다. 도망치지 않으면 방향을 유지한 채 경직만 한다
            _shouldFleeAfterHit = Random.value < _fleeChance;
            if (_shouldFleeAfterHit)
            {
                // 맞은 위치 반대 방향으로 도망 벡터 산출
                var fleeDir = transform.position - info.WorldPos;
                fleeDir.y = 0f;
                if (fleeDir.sqrMagnitude > 0.01f)
                {
                    _currentDirection = fleeDir.normalized;
                }
                else
                {
                    PickRandomDirection();
                }
            }

            ChangeState(CreatureState.BeingHit);
        }

        public void PickRandomDirection()
        {
            var angle = Random.Range(0f, Mathf.PI * 2f);
            _currentDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized;
        }

        /// <summary>다음 경직 후 도망 여부를 고정한다. 검증용.</summary>
        public void SetFleeAfterHit(bool shouldFlee)
        {
            _shouldFleeAfterHit = shouldFlee;
        }

        public void SetDirection(Vector3 direction)
        {
            direction.y = 0f;
            _currentDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
        }
    }
}
