using System.Collections.Generic;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
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
    public class CreatureManager : MonoBehaviour, IRunScoped
    {
        /// <summary>
        /// 재등장 대기의 하한. 밸런스 수치가 아니라 방어값이다 — 단축 업그레이드가 겹쳐
        /// 0 이하로 내려가면 매 프레임 스폰이 된다. CSV 에 넣을 성질의 값이 아니다.
        /// </summary>
        private const float MinSpawnIntervalSec = 0.1f;

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

        /// <summary>
        /// 업그레이드 실효값 조회 통로 (#116). Managers 프리팹에서 ManagerBootstrap 과
        /// GameManager 가 넣어 준다 (#131, #140). 없으면 CSV 기준값을 그대로 쓴다.
        /// </summary>
        private IUpgradeStats _upgradeStats;

        /// <summary>
        /// 피격 판정 확대 퍼크가 더하는 비율(percent). 이번 런에서만 산다 (#126).
        /// Target 인스턴스가 여럿이라 여기서 한 번 받아 스폰 때 넘긴다.
        /// </summary>
        private float _perkHitRadiusPercent;
        private float _pendingPerkHitRadiusPercent;

        private bool _isRunning;

        private readonly List<GameObject> _activeCreatures = new List<GameObject>();
        private readonly List<float> _respawnTimers = new List<float>();

        public IReadOnlyList<GameObject> ActiveCreatures => _activeCreatures;
        public Bounds DeskBounds => _deskBounds;
        public int CurrentStageNumber => _currentStageNumber;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            GameEvents.OnTargetBroken += HandleTargetBroken;
            GameEvents.OnPerkChosen += HandlePerkChosen;
        }

        private void OnDisable()
        {
            GameEvents.OnTargetBroken -= HandleTargetBroken;
            GameEvents.OnPerkChosen -= HandlePerkChosen;
        }

        /// <summary>
        /// 런을 시작한다. 예약해 둔 퍼크가 있으면 여기서 켠다 (IRunScoped, #126).
        /// GameManager 가 씬 구현체를 따로 모아 불러 준다 — 이 매니저는 Managers 프리팹 밖이다.
        /// </summary>
        public void BeginRun()
        {
            _isRunning = true;
            ApplyPerkRadius(_pendingPerkHitRadiusPercent);
            _pendingPerkHitRadiusPercent = 0f;
        }

        /// <summary>런을 끝낸다. "이번 런" 퍼크는 여기서 사라진다.</summary>
        public void EndRun()
        {
            _isRunning = false;
            ApplyPerkRadius(0f);
        }

        /// <summary>
        /// 피격 판정 확대 퍼크만 받는다. 런 도중이면 즉시, 밖이면 다음 런 시작에 켠다 (GDD 6절).
        /// </summary>
        private void HandlePerkChosen(string perkId)
        {
            if (_balanceData == null)
            {
                return;
            }

            var perk = _balanceData.GetPerk(perkId);
            if (perk == null || perk.Type != PerkType.HitRadiusBoost)
            {
                return;
            }

            if (_isRunning)
            {
                ApplyPerkRadius(_perkHitRadiusPercent + perk.Value);
                return;
            }
            _pendingPerkHitRadiusPercent += perk.Value;
        }

        /// <summary>이미 살아 있는 크리처에도 바로 반영한다. 런 도중에 고른 퍼크가 즉시 들어야 한다.</summary>
        private void ApplyPerkRadius(float percent)
        {
            _perkHitRadiusPercent = percent;
            for (var i = 0; i < _activeCreatures.Count; i++)
            {
                var creature = _activeCreatures[i];
                if (creature == null)
                {
                    continue;
                }
                var target = creature.GetComponent<Target>();
                if (target != null)
                {
                    target.SetPerkHitRadiusPercent(percent);
                }
            }
        }

        private void Update()
        {
            UpdateRespawnTimers(Time.deltaTime);
        }

        /// <summary>런을 시작한다. GameManager 가 런 시작 직전에 부른다 (IRunScoped, 이슈 #140).</summary>
        public void BeginRun()
        {
            Debug.Log($"[CreatureManager] BeginRun 호출됨 (현재 단계: {_currentStageNumber})");
            InitializeStage(_currentStageNumber);
        }

        /// <summary>런을 종료한다. GameManager 가 Result 전이 시 부른다 (IRunScoped, 이슈 #140).</summary>
        public void EndRun()
        {
            Debug.Log("[CreatureManager] EndRun 호출됨 (크리처 정리)");
            ClearAllCreatures();
        }

        /// <summary>
        /// 특정 스테이지 기준으로 스폰 매니저를 초기화하고 초기 대상을 배치한다.
        /// </summary>
        public void InitializeStage(int stageNumber)
        {
            _currentStageNumber = stageNumber;
            ClearAllCreatures();

            var targetCount = GetRequiredSpawnCount();
            Debug.Log($"[CreatureManager] {stageNumber}단계 초기화: 크리처 {targetCount}마리 스폰 시작");
            for (var i = 0; i < targetCount; i++)
            {
                SpawnRandomCreature();
            }
        }

        /// <summary>
        /// 업그레이드 실효값 조회 통로를 넣고, 늘어난 동시 출현 수만큼 즉시 채운다.
        /// 서비스 계약이 아니라 조립(wiring) 통로다 (ARCHITECTURE "SetBillService" 문단).
        ///
        /// 이전에는 조립 지점이 증분을 직접 계산해 넘기는 SetUpgradeOverrides(int, float) 였다.
        /// stat 마다 인자를 늘려야 하고 반영 경로가 IUpgradeStats 와 두 갈래가 되어 걷어냈다 (#131).
        /// </summary>
        public void SetUpgradeStats(IUpgradeStats upgradeStats)
        {
            _upgradeStats = upgradeStats;

            var needed = GetRequiredSpawnCount() - _activeCreatures.Count - _respawnTimers.Count;
            for (var i = 0; i < needed; i++)
            {
                SpawnRandomCreature();
            }
        }

        /// <summary>
        /// 현재 단계와 업그레이드를 합산한 목표 동시 출현 수를 구한다.
        ///
        /// 다른 소비처와 달리 런 시작에 굳히지 않는다 — spawn_count 의 **기준값이 단계마다 다르다**
        /// (#116 이 기준값을 호출측이 넘기도록 계약을 정한 이유). 굳혀 두면 단계가 오를 때 옛 값이 남는다.
        /// 대신 "업그레이드는 메뉴·결과 화면에서만 산다"(BALANCE 6절)는 규칙에 기댄다.
        /// </summary>
        public int GetRequiredSpawnCount()
        {
            // 기본값을 코드에 두지 않는다. CSV 를 못 읽으면 스폰하지 않는 편이 낫다 —
            // 임의의 숫자를 두면 CSV 와 다른 난이도가 조용히 돌아간다 (AGENTS.md 데이터 절).
            var stageDef = _balanceData == null ? null : _balanceData.GetStage(_currentStageNumber);
            if (stageDef == null || stageDef.SpawnCount <= 0)
            {
                return 0;
            }
            return Mathf.RoundToInt(GetStat(StatId.SpawnCount, stageDef.SpawnCount));
        }

        /// <summary>
        /// 재등장 대기 시간을 구한다.
        /// </summary>
        public float GetSpawnIntervalSec()
        {
            // 위와 같은 이유로 기본값을 코드에 두지 않는다.
            var baseInterval = _balanceData == null || _balanceData.Economy == null
                ? 0f
                : _balanceData.Economy.SpawnIntervalSec;
            if (baseInterval <= 0f)
            {
                return 0f;
            }
            return Mathf.Max(MinSpawnIntervalSec, GetStat(StatId.SpawnIntervalSec, baseInterval));
        }

        /// <summary>주입이 없으면 기준값 그대로다. 배선이 빠져도 게임이 돌아가야 한다.</summary>
        private float GetStat(StatId stat, float baseValue)
        {
            return _upgradeStats == null ? baseValue : _upgradeStats.GetStat(stat, baseValue);
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
                // 판정 반경에 업그레이드를 반영하려면 Initialize 보다 먼저 넣어야 한다.
                target.SetUpgradeStats(_upgradeStats);
                target.Initialize();
                target.SetPerkHitRadiusPercent(_perkHitRadiusPercent);
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
            Debug.Log($"[CreatureManager] 크리처 스폰 성공: {targetId} at {spawnPos} (현재 {_activeCreatures.Count}마리)");
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
