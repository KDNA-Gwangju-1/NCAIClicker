using System.Collections.Generic;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Targets;
using UnityEngine;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 크리처들의 스폰과 필드 내 개체 수, 리스폰을 총괄하는 매니저 (ARCHITECTURE 1절).
    /// 동시 출현 수는 stages.csv 의 spawn_count,
    /// 재등장 대기는 economy.csv 의 spawn_interval_sec 을 기준으로 하며,
    /// 업그레이드(저금통 수집벽)에 의해 동적으로 변할 수 있도록 계산한다.
    /// </summary>
    public class CreatureManager : MonoBehaviour
    {
        public static CreatureManager Instance { get; private set; }

        [SerializeField] private BalanceData _balanceData;
        [SerializeField] private GameObject _targetNormalPrefab;
        [SerializeField] private GameObject _targetAnchorPrefab;
        [SerializeField] private GameObject _targetRunnerPrefab;
        [SerializeField] private GameObject _targetTouristPrefab;

        /// <summary>
        /// 책상 평면 중심(0, 0, 2) 기준 6x6 유닛에서 10% 안전 여백을 둔 이동/스폰 영역.
        /// </summary>
        [SerializeField] private Bounds _deskBounds = new Bounds(new Vector3(0f, 0f, 2f), new Vector3(4.8f, 1f, 4.8f));

        private int _currentStageNumber = 1;
        private int _bonusSpawnCount;
        private float _spawnIntervalMultiplier = 1f;

        private readonly List<GameObject> _activeCreatures = new List<GameObject>();
        private readonly List<float> _respawnTimers = new List<float>();

        public IReadOnlyList<GameObject> ActiveCreatures => _activeCreatures;
        public Bounds DeskBounds => _deskBounds;
        public int CurrentStageNumber => _currentStageNumber;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            GameEvents.OnTargetBroken += HandleTargetBroken;
        }

        private void OnDisable()
        {
            GameEvents.OnTargetBroken -= HandleTargetBroken;
        }

        private void Start()
        {
            InitializeStage(_currentStageNumber);
        }

        private void Update()
        {
            UpdateRespawnTimers(Time.deltaTime);
        }

        /// <summary>
        /// 특정 스테이지 기준으로 스폰 매니저를 초기화하고 초기 대상을 배치한다.
        /// </summary>
        public void InitializeStage(int stageNumber)
        {
            _currentStageNumber = stageNumber;
            ClearAllCreatures();

            var targetCount = GetRequiredSpawnCount();
            for (var i = 0; i < targetCount; i++)
            {
                SpawnRandomCreature();
            }
        }

        /// <summary>
        /// 업그레이드 효과를 외부에서 주입받아 반영한다 (하드코딩 방지).
        /// </summary>
        public void SetUpgradeOverrides(int bonusSpawnCount, float intervalMultiplier)
        {
            _bonusSpawnCount = Mathf.Max(0, bonusSpawnCount);
            _spawnIntervalMultiplier = Mathf.Clamp(intervalMultiplier, 0.1f, 2f);

            var needed = GetRequiredSpawnCount() - _activeCreatures.Count - _respawnTimers.Count;
            for (var i = 0; i < needed; i++)
            {
                SpawnRandomCreature();
            }
        }

        /// <summary>
        /// 현재 단계와 업그레이드를 합산한 목표 동시 출현 수를 구한다.
        /// </summary>
        public int GetRequiredSpawnCount()
        {
            var baseCount = 6;
            if (_balanceData != null)
            {
                var stageDef = _balanceData.GetStage(_currentStageNumber);
                if (stageDef != null && stageDef.SpawnCount > 0)
                {
                    baseCount = stageDef.SpawnCount;
                }
            }
            return baseCount + _bonusSpawnCount;
        }

        /// <summary>
        /// 재등장 대기 시간을 구한다.
        /// </summary>
        public float GetSpawnIntervalSec()
        {
            var baseInterval = 7.0f;
            if (_balanceData != null && _balanceData.Economy != null && _balanceData.Economy.SpawnIntervalSec > 0f)
            {
                baseInterval = _balanceData.Economy.SpawnIntervalSec;
            }
            return baseInterval * _spawnIntervalMultiplier;
        }

        private void HandleTargetBroken(BreakInfo info)
        {
            // 파괴된 대상 화면 제거 및 리스폰 쿨다운 등록
            for (var i = _activeCreatures.Count - 1; i >= 0; i--)
            {
                var c = _activeCreatures[i];
                if (c == null)
                {
                    _activeCreatures.RemoveAt(i);
                    continue;
                }
                var target = c.GetComponent<Target>();
                if (target != null && !target.IsAlive)
                {
                    Destroy(c);
                    _activeCreatures.RemoveAt(i);
                }
            }
            _respawnTimers.Add(GetSpawnIntervalSec());
        }

        public void UpdateRespawnTimers(float deltaTime)
        {
            for (var i = _respawnTimers.Count - 1; i >= 0; i--)
            {
                _respawnTimers[i] -= deltaTime;
                if (_respawnTimers[i] <= 0f)
                {
                    _respawnTimers.RemoveAt(i);
                    SpawnRandomCreature();
                }
            }
        }

        /// <summary>
        /// 단계별 비율에 따라 프리팹을 추첨하여 안전 영역 내에 스폰한다.
        /// </summary>
        public GameObject SpawnRandomCreature()
        {
            var prefab = PickPrefabByStageRatio();
            if (prefab == null)
            {
                return null;
            }

            var spawnPos = GetRandomSpawnPosition();
            var instance = Instantiate(prefab, spawnPos, Quaternion.identity);

            var target = instance.GetComponent<Target>();
            if (target != null)
            {
                target.Initialize();
            }

            var movement = instance.GetComponent<CreatureMovement>();
            if (movement == null)
            {
                movement = instance.AddComponent<CreatureMovement>();
            }

            var targetId = target != null ? target.TargetId : "normal";
            movement.Initialize(_balanceData, targetId, _deskBounds);

            var hpDisplay = instance.GetComponent<CreatureHpDisplay>();
            if (hpDisplay == null)
            {
                hpDisplay = instance.AddComponent<CreatureHpDisplay>();
            }

            _activeCreatures.Add(instance);
            return instance;
        }

        private GameObject PickPrefabByStageRatio()
        {
            var normalRatio = 0.6f;
            var anchorRatio = 0.15f;
            var runnerRatio = 0.1f;
            var touristRatio = 0.15f;

            if (_balanceData != null)
            {
                var stageDef = _balanceData.GetStage(_currentStageNumber);
                if (stageDef != null)
                {
                    normalRatio = stageDef.NormalRatio;
                    anchorRatio = stageDef.AnchorRatio;
                    runnerRatio = stageDef.RunnerRatio;
                    touristRatio = stageDef.TouristRatio;
                }
            }

            var total = normalRatio + anchorRatio + runnerRatio + touristRatio;
            if (total <= 0f)
            {
                return _targetNormalPrefab;
            }

            var roll = Random.Range(0f, total);
            if (roll < normalRatio)
            {
                return _targetNormalPrefab != null ? _targetNormalPrefab : _targetAnchorPrefab;
            }
            roll -= normalRatio;

            if (roll < anchorRatio)
            {
                return _targetAnchorPrefab != null ? _targetAnchorPrefab : _targetNormalPrefab;
            }
            roll -= anchorRatio;

            if (roll < runnerRatio)
            {
                return _targetRunnerPrefab != null ? _targetRunnerPrefab : _targetNormalPrefab;
            }

            return _targetTouristPrefab != null ? _targetTouristPrefab : _targetNormalPrefab;
        }

        public Vector3 GetRandomSpawnPosition()
        {
            var rx = Random.Range(_deskBounds.min.x, _deskBounds.max.x);
            var rz = Random.Range(_deskBounds.min.z, _deskBounds.max.z);
            return new Vector3(rx, 0f, rz);
        }

        public void ClearAllCreatures()
        {
            foreach (var creature in _activeCreatures)
            {
                if (creature != null)
                {
                    Destroy(creature);
                }
            }
            _activeCreatures.Clear();
            _respawnTimers.Clear();
        }
    }
}
