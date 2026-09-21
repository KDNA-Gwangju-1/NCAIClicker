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
    /// 동시 출현 수는 stages.csv 의 spawn_count 를 기준으로 하며, 업그레이드(저금통 수집벽)에
    /// 의해 동적으로 변할 수 있도록 계산한다.
    ///
    /// 부서진 자리는 시간이 지나도 자동으로 채워지지 않는다 (#156 B안 — 원작 재관찰 결과 시간
    /// 기반 개별 리스폰은 원작 기본 규칙이 아니었다. REFERENCE_ANALYSIS.md 9절).
    /// 대신 파괴할 때마다 economy.csv 의 extra_spawn_chance_on_destroy 확률로 즉시 1개가
    /// 추가되고, 필드가 완전히 비면(0마리) 그때만 1개가 즉시 채워진다.
    /// </summary>
    public class CreatureManager : MonoBehaviour, IRunScoped
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

        /// <summary>
        /// 업그레이드 실효값 조회 통로 (#116). Managers 프리팹에서 ManagerBootstrap 과
        /// GameManager 가 넣어 준다 (#131, #140). 없으면 CSV 기준값을 그대로 쓴다.
        /// </summary>
        private IUpgradeStats _upgradeStats;

        /// <summary>
        /// 단계 진행 상태 조회 통로 (이슈 #150). 없으면 기본값 1을 쓴다.
        /// </summary>
        private IStageService _stageService;

        /// <summary>
        /// 피격 판정 확대 퍼크가 더하는 비율(percent). 이번 런에서만 산다 (#126).
        /// Target 인스턴스가 여럿이라 여기서 한 번 받아 스폰 때 넘긴다.
        /// </summary>
        private float _perkHitRadiusPercent;
        private float _pendingPerkHitRadiusPercent;

        private bool _isRunning;

        private readonly List<GameObject> _activeCreatures = new List<GameObject>();

        public IReadOnlyList<GameObject> ActiveCreatures => _activeCreatures;
        public Bounds DeskBounds => _deskBounds;
        public int CurrentStageNumber => _currentStageNumber;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                SafeDestroy(this);
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
        /// 런을 시작한다. 예약해 둔 퍼크를 켜고 이번 단계의 크리처를 배치한다 (IRunScoped, #126·#140).
        /// GameManager 가 Managers 프리팹의 IRunScoped 를 모아 불러 준다.
        ///
        /// 퍼크를 스폰보다 **먼저** 켠다 — SpawnRandomCreature 가 새 대상에
        /// _perkHitRadiusPercent 를 그대로 물려주므로 순서가 뒤바뀌면 이번 런의 첫 크리처들이
        /// 퍼크를 받지 못한다.
        ///
        /// 초기 배치를 Start() 가 아니라 여기서 하는 이유는 DontDestroyOnLoad 다. Start 는 생애
        /// 한 번뿐이라 Game 씬에 두 번째로 들어갈 때 재초기화가 되지 않는다 (#140).
        /// </summary>
        public void BeginRun()
        {
            _isRunning = true;
            ApplyPerkRadius(_pendingPerkHitRadiusPercent);
            _pendingPerkHitRadiusPercent = 0f;
            if (_stageService != null)
            {
                _currentStageNumber = _stageService.CurrentStageNumber;
            }
            InitializeStage(_currentStageNumber);
        }

        /// <summary>런을 끝낸다. "이번 런" 퍼크와 필드에 남은 크리처는 여기서 사라진다.</summary>
        public void EndRun()
        {
            _isRunning = false;
            ApplyPerkRadius(0f);
            ClearAllCreatures();
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
        /// 업그레이드 실효값 조회 통로를 넣는다. 서비스 계약이 아니라 조립(wiring) 통로다
        /// (ARCHITECTURE "SetBillService" 문단).
        ///
        /// 이전에는 조립 지점이 증분을 직접 계산해 넘기는 SetUpgradeOverrides(int, float) 였다.
        /// stat 마다 인자를 늘려야 하고 반영 경로가 IUpgradeStats 와 두 갈래가 되어 걷어냈다 (#131).
        ///
        /// **여기서 스폰하지 않는다** (#140). 늘어난 동시 출현 수만큼 즉시 채우던 코드가 있었는데,
        /// 이 매니저가 Managers 프리팹으로 옮겨 오면서 ManagerBootstrap 이 **씬 로드 전에** 이
        /// 메서드를 부르게 됐다. 그 결과 런이 시작되기도 전에, 그것도 MainMenu 씬에서 크리처가
        /// 6마리 생겼다. 조립 통로는 부수효과를 갖지 않는다 — 늘어난 수는 다음 BeginRun 의
        /// InitializeStage 가 반영한다 (업그레이드는 메뉴·결과 화면에서만 사므로 런 도중에
        /// 목표치가 변할 일이 없다. BALANCE 6절).
        /// </summary>
        public void SetUpgradeStats(IUpgradeStats upgradeStats)
        {
            _upgradeStats = upgradeStats;
        }

        /// <summary>
        /// 단계 진행 상태 조회 통로를 넣는다 (이슈 #150).
        /// 매 런 시작 시 이 통로의 현재 단계 번호로 InitializeStage 를 부른다.
        /// </summary>
        public void SetStageService(IStageService stageService)
        {
            _stageService = stageService;
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
        /// 저금통 파괴 시 즉시 1개를 추가로 스폰할 확률(%, 0~100)을 구한다.
        /// 기본값은 0 — 저금통 수집벽 업그레이드가 이 값을 올린다.
        /// </summary>
        public float GetExtraSpawnChancePercent()
        {
            // 기본값을 코드에 두지 않는다. CSV 를 못 읽으면 확률 0(추가 생성 없음)으로 둔다.
            var baseChance = _balanceData == null || _balanceData.Economy == null
                ? 0f
                : _balanceData.Economy.ExtraSpawnChanceOnDestroy;
            return Mathf.Clamp(GetStat(StatId.ExtraSpawnChance, baseChance), 0f, 100f);
        }

        /// <summary>주입이 없으면 기준값 그대로다. 배선이 빠져도 게임이 돌아가야 한다.</summary>
        private float GetStat(StatId stat, float baseValue)
        {
            return _upgradeStats == null ? baseValue : _upgradeStats.GetStat(stat, baseValue);
        }

        /// <summary>
        /// 파괴된 대상을 치우고, 파괴 개수만큼 추가 생성 확률을 굴린 뒤 필드가 완전히
        /// 비었으면 1개만 즉시 채운다 (#156 B안).
        ///
        /// 목록에 없는 대상의 파괴 이벤트(유령)는 치운 것이 없어 removedCount 가 0 이므로
        /// 아무 것도 하지 않는다 (#141 과 같은 이유로 유효한 파괴에만 반응해야 한다).
        /// </summary>
        private void HandleTargetBroken(BreakInfo info)
        {
            var removedCount = RemoveDeadCreatures();
            if (removedCount <= 0)
            {
                return;
            }

            var extraSpawnChance = GetExtraSpawnChancePercent();
            for (var i = 0; i < removedCount; i++)
            {
                if (Random.Range(0f, 100f) < extraSpawnChance)
                {
                    SpawnRandomCreature();
                }
            }

            if (_activeCreatures.Count == 0)
            {
                SpawnRandomCreature();
            }
        }

        /// <summary>죽었거나 이미 사라진 대상을 목록에서 치우고 그 수를 돌려준다.</summary>
        private int RemoveDeadCreatures()
        {
            var removedCount = 0;
            for (var i = _activeCreatures.Count - 1; i >= 0; i--)
            {
                var creature = _activeCreatures[i];
                if (creature == null)
                {
                    // 밖에서 파괴된 것. 자리는 비었으므로 재등장 대상으로 센다.
                    _activeCreatures.RemoveAt(i);
                    removedCount++;
                    continue;
                }

                var target = creature.GetComponent<Target>();
                if (target != null && !target.IsAlive)
                {
                    SafeDestroy(creature);
                    _activeCreatures.RemoveAt(i);
                    removedCount++;
                }
            }
            return removedCount;
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
            // 기본값을 코드에 두지 않는다. CSV 를 못 읽으면 스폰하지 않는 편이 낫다 (AGENTS.md 데이터 절, #148).
            var stageDef = _balanceData == null ? null : _balanceData.GetStage(_currentStageNumber);
            if (stageDef == null)
            {
                return null;
            }

            var normalRatio = stageDef.NormalRatio;
            var anchorRatio = stageDef.AnchorRatio;
            var runnerRatio = stageDef.RunnerRatio;
            var touristRatio = stageDef.TouristRatio;

            var total = normalRatio + anchorRatio + runnerRatio + touristRatio;
            if (total <= 0f)
            {
                return null;
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
                    SafeDestroy(creature);
                }
            }
            _activeCreatures.Clear();
        }

        /// <summary>
        /// 플레이 모드에서는 Destroy, 에디트 모드 검증 환경에서는 DestroyImmediate 를 호출해
        /// 에디트 모드에서 Destroy 호출 오류가 발생하거나 씬에 잔류하는 것을 방지한다 (#161).
        /// </summary>
        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}
