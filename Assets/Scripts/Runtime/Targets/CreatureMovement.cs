using NCAIClicker.Data;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.Targets
{
    /// <summary>
    /// 크리처의 책상 평면 2축(XZ) 이동과 FSM 상태 머신을 제어한다 (ARCHITECTURE 4절).
    /// 상태 전이: Idle, Moving, BeingHit, Fleeing 순환.
    /// 이동은 책상 평면 안전 경계를 벗어나지 않는다.
    /// </summary>
    [RequireComponent(typeof(Target))]
    public class CreatureMovement : MonoBehaviour
    {
        [SerializeField] private BalanceData _balanceData;
        [SerializeField] private float _hitStunDuration = 0.2f;
        [SerializeField] private float _fleeDuration = 1.0f;
        [SerializeField] private float _fleeSpeedMultiplier = 1.5f;
        [SerializeField] private Bounds _movementBounds;

        private Target _target;
        private CreatureState _currentState = CreatureState.Idle;
        private float _moveSpeed;
        private float _turnIntervalSec;
        private float _turnTimer;
        private float _stateTimer;
        private Vector3 _currentDirection;

        public CreatureState CurrentState => _currentState;
        public Bounds MovementBounds => _movementBounds;
        public Vector3 CurrentDirection => _currentDirection;
        public float MoveSpeed => _moveSpeed;
        public float TurnIntervalSec => _turnIntervalSec;

        private void Awake()
        {
            _target = GetComponent<Target>();
        }

        private void OnEnable()
        {
            if (_target != null)
            {
                _target.OnHitReceived += HandleHitReceived;
            }
        }

        private void OnDisable()
        {
            if (_target != null)
            {
                _target.OnHitReceived -= HandleHitReceived;
            }
        }

        /// <summary>
        /// CSV 수치 및 이동 경계로 초기화한다.
        /// </summary>
        public void Initialize(BalanceData balanceData, string targetId, Bounds bounds)
        {
            _balanceData = balanceData;
            _movementBounds = bounds;

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
            ChangeState(CreatureState.Moving);
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
                    break;

                case CreatureState.Moving:
                    _turnTimer += deltaTime;
                    if (_turnTimer >= _turnIntervalSec && _turnIntervalSec > 0f)
                    {
                        _turnTimer = 0f;
                        PickRandomDirection();
                    }
                    MoveStep(deltaTime, _moveSpeed);
                    break;

                case CreatureState.BeingHit:
                    _stateTimer -= deltaTime;
                    if (_stateTimer <= 0f)
                    {
                        ChangeState(CreatureState.Fleeing);
                    }
                    break;

                case CreatureState.Fleeing:
                    _stateTimer -= deltaTime;
                    MoveStep(deltaTime, _moveSpeed * _fleeSpeedMultiplier);
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
                    PickRandomDirection();
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
            if (_currentState != CreatureState.BeingHit)
            {
                ChangeState(CreatureState.BeingHit);
            }
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
