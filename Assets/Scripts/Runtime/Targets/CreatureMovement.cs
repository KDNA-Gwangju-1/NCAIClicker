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
    /// </summary>
    [RequireComponent(typeof(Target))]
    public class CreatureMovement : MonoBehaviour
    {
        [SerializeField] private BalanceData _balanceData;
        [SerializeField] private float _hitStunDuration = 0.15f;
        [SerializeField] private float _fleeDuration = 0.8f;
        [SerializeField] private float _baseIdleDuration = 1.0f;
        [SerializeField] private Bounds _movementBounds;

        private Target _target;
        private CreatureState _currentState = CreatureState.Idle;
        private float _moveSpeed;
        private float _turnIntervalSec;
        private float _turnTimer;
        private float _stateTimer;
        private float _idleTimer;
        private Vector3 _currentDirection;

        // 타격 누적에 따른 패닉 가속 처리
        private int _consecutiveHits;
        private float _comboResetTimer;

        public CreatureState CurrentState => _currentState;
        public Bounds MovementBounds => _movementBounds;
        public Vector3 CurrentDirection => _currentDirection;
        public float MoveSpeed => _moveSpeed;
        public float TurnIntervalSec => _turnIntervalSec;
        public int ConsecutiveHits => _consecutiveHits;

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
                        // 60% 확률로 멈춰 서서 주변을 살피는 정지 상태로 전이
                        if (Random.value < 0.6f)
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
                    // 짧은 경직 후 도망 상태로 즉시 전이
                    _stateTimer -= deltaTime;
                    if (_stateTimer <= 0f)
                    {
                        ChangeState(CreatureState.Fleeing);
                    }
                    break;

                case CreatureState.Fleeing:
                    // 타격 누적에 따른 가속 속도로 도망
                    _stateTimer -= deltaTime;
                    var panicMultiplier = Mathf.Clamp(1.2f + (_consecutiveHits * 0.35f), 1.2f, 2.6f);
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

            ChangeState(CreatureState.BeingHit);
        }

        public void PickRandomDirection()
        {
            var angle = Random.Range(0f, Mathf.PI * 2f);
            _currentDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized;
        }

        public void SetDirection(Vector3 direction)
        {
            direction.y = 0f;
            _currentDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
        }
    }
}
