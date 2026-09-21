using System;
using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 런 상태(MainMenu → Running → Result)를 관리한다. 전이 규칙은 PATTERNS.md 4절.
    /// MainMenu/Running 전이는 현재 로드된 씬을 그대로 따른다 — 개발 중 Game 씬을 바로 열어도
    /// 상태가 맞게 잡혀야 하기 때문이다 (ARCHITECTURE.md 0절).
    /// Result 전이는 씬 전환 없이 결정한다. 전이를 실제로 일으키는 것은 OnStaminaDepleted 뿐이고,
    /// OnBankrupt 는 이미 전이된 뒤 확정되는 결과 통지다 (ARCHITECTURE.md 2절 "하루 종료 순서" —
    /// GameManager 만 이 두 이벤트를 구독한다).
    ///
    /// Running 진입 시 IRunScoped.BeginRun(), Result 진입 시 IRunScoped.EndRun() 을 호출해
    /// 매니저 구현 클래스를 직접 잡지 않고 런 라이프사이클을 배선한다 (이슈 #111).
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class GameManager : MonoBehaviour, IGameFlowService
    {
        private const string MainMenuSceneName = "MainMenu";
        private const string GameSceneName = "Game";

        /// <summary>MainMenu 버튼 등 외부 소비자는 이 인터페이스 타입으로만 접근한다 (계약 변경 #142).</summary>
        public static IGameFlowService Instance { get; private set; }

        public RunState CurrentState { get; private set; }

        private IRunScoped[] _runScopedServices;
        private IBillService _billService;

        /// <summary>
        /// 씬에 사는 런 경계 구현체 (#126). 프리팹 안의 것과 달리 캐시하지 않는다 —
        /// 씬이 다시 로드되면 인스턴스가 새로 생긴다.
        /// </summary>
        private IRunScoped[] _sceneRunScopedServices;

        private bool _isRunActive;

        /// <summary>
        /// ContinueRun 의 씬 재로드로 사라지기 전에 HammerSwingController 에서 옮겨 온 예약 퍼크(percent).
        /// GameManager 는 DontDestroyOnLoad 라 씬이 바뀌어도 값을 들고 있다가, 새로 생긴
        /// HammerSwingController 인스턴스에 WireSceneConsumers 에서 되돌려 준다 (#188 작업 중 발견).
        /// </summary>
        private float _pendingHammerPerkPowerPercent;

        private void Awake()
        {
            Instance = this;
            CurrentState = ResolveState(SceneManager.GetActiveScene().name) ?? RunState.MainMenu;
            _billService = GetComponentInChildren<IBillService>(true);
        }

        private void Start()
        {
            // 개발 중 Game 씬을 바로 열고 Play 를 눌러 시작한 경우 Awake 직후 런을 활성화한다.
            if (CurrentState == RunState.Running)
            {
                NotifyBeginRun();
            }
        }

        /// <summary>
        /// 새 회차 시작. 저장을 기본값으로 덮어쓴 뒤 Game 씬으로 전환한다.
        /// 덮어쓴다는 확인은 호출측(MainMenuController)이 먼저 받는다 — 이슈 #90 완료 기준.
        ///
        /// 지우는 일 자체는 ResetAndDistribute 가 한다 (이슈 #203) — 파일만 비우면 업그레이드
        /// 레벨·레거시 포인트·반지가 메모리에 남아 직전 회차의 성장을 달고 시작하게 되고,
        /// 볼륨·창모드 같은 설정까지 함께 날아간다. 설정 초기화(#196)와 **같은 메서드를 쓴다** —
        /// "무엇을 지우는가" 를 두 곳에 적으면 서로 다른 답을 낸다.
        ///
        /// 파산은 성장을 지우지 않는다 (이슈 #183). 지우는 경로는 이것과 설정 초기화 둘뿐이다.
        /// </summary>
        public void StartNewRun()
        {
            SaveManager.Persistence?.ResetAndDistribute();
            SceneManager.LoadScene(GameSceneName);
        }

        /// <summary>
        /// 이어하기. Game 씬으로 전환한다.
        ///
        /// **하루를 넘기기 전에 마감을 확정한다** (이슈 #211). Result 에서 넘어올 때만 판정한다 —
        /// 메인 메뉴의 "이어하기" 는 하루를 넘기는 것이 아니다. 미납 파산이면 씬을 넘기지 않고 파산 화면을 유지한다.
        ///
        /// **떠나기 전에 저장한다** (이슈 #203). 업그레이드·반지 상점이 이 버튼 바로 앞의
        /// 고지서 화면에 있어서, 여기서 저장하지 않으면 사 놓고 게임을 끈 플레이어가 산 것을
        /// 잃는다. ARCHITECTURE 저장 경계의 "런 시작 직전" 이 이 지점이다.
        ///
        /// **씬 재로드 전에 예약 퍼크부터 옮겨 둔다** (#188 작업 중 발견). HammerSwingController 는
        /// Managers 프리팹 밖이라 아래 LoadScene 이 인스턴스를 통째로 파괴한다 — 고지서 화면에서
        /// 고른 타격력 강화 퍼크가 이 시점에 옮겨지지 않으면 다음 런에서 조용히 사라진다.
        /// </summary>
        public void ContinueRun()
        {
            if (CurrentState == RunState.Result && _billService?.TryCloseDay() == true)
            {
                // 파산 확정 (#211). 이 경로는 씬을 다시 로드하지 않으므로 예약 퍼크를 옮길
                // 다음 런 자체가 없다 — TryCloseDay 가 발행한 OnBankrupt 를 HammerSwingController/
                // CreatureManager 가 직접 구독해 지운다 (#188).
                return;
            }

            CarryOverScenePerks();
            SaveManager.Persistence?.CollectAndSave();
            SceneManager.LoadScene(GameSceneName);
        }

        /// <summary>
        /// 씬 재로드로 사라질 씬 소비처(HammerSwingController)의 예약 퍼크를 다음 런으로 넘긴다
        /// (#188 작업 중 발견). CreatureManager 등 Managers 프리팹 소속 매니저는 DontDestroyOnLoad 라
        /// 이 작업이 필요 없다 — 씬 밖에 사는 소비처만 옮기면 된다.
        /// </summary>
        private void CarryOverScenePerks()
        {
            if (_sceneRunScopedServices == null)
            {
                return;
            }

            for (var i = 0; i < _sceneRunScopedServices.Length; i++)
            {
                if (_sceneRunScopedServices[i] is HammerSwingController hammer && hammer != null)
                {
                    _pendingHammerPerkPowerPercent += hammer.PendingPerkPowerPercent;
                }
            }
        }

        /// <summary>종료. 버튼이 SceneManager/Application API를 직접 부르지 않도록 GameManager가 대신한다.</summary>
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다. 빠뜨리면 코인이 두 배로 들어온다 (AGENTS.md).
        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            GameEvents.OnStaminaDepleted += HandleRunEnded;
            GameEvents.OnBankrupt += HandleRunEnded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            GameEvents.OnStaminaDepleted -= HandleRunEnded;
            GameEvents.OnBankrupt -= HandleRunEnded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var next = ResolveState(scene.name);
            if (!next.HasValue)
            {
                return;
            }

            // **여기서 복원하지 않는다** (이슈 #203). 매니저는 DontDestroyOnLoad 라 씬을 다시
            // 로드해도 값을 그대로 들고 있다 — 씬 로드마다 저장을 덮어씌우면 마지막 저장 이후에
            // 생긴 변경이 사라진다. 실제로 고지서 화면에서 반지를 사고 "다음 날"을 누르면
            // 구매가 통째로 되돌아갔다.
            //
            // 복원은 앱이 켜질 때 ManagerBootstrap 이 한 번만 한다. 그 뒤로 저장은 기록일 뿐,
            // 살아 있는 값의 출처가 아니다.
            SetState(next.Value);
        }

        /// <summary>Running 중에만 Result 로 전이한다. 스태미나 소진과 파산 둘 다 같은 전이를 부른다.</summary>
        private void HandleRunEnded()
        {
            if (CurrentState == RunState.Running)
            {
                SetState(RunState.Result);
            }
        }

        private static RunState? ResolveState(string sceneName)
        {
            switch (sceneName)
            {
                case MainMenuSceneName:
                    return RunState.MainMenu;
                case GameSceneName:
                    return RunState.Running;
                default:
                    return null;
            }
        }

        private void SetState(RunState next)
        {
            if (CurrentState == next)
            {
                return;
            }
            Debug.Log($"[GameManager] {CurrentState} -> {next}");
            CurrentState = next;

            if (next == RunState.Running)
            {
                NotifyBeginRun();
            }
            else
            {
                NotifyEndRun();
            }
        }

        private void EnsureRunScopedServices()
        {
            if (_runScopedServices != null && _runScopedServices.Length > 0)
            {
                return;
            }

            var services = GetComponentsInChildren<IRunScoped>(true);
            // ARCHITECTURE 1절 초기화 순서 준수. 순번은 GetServiceOrder 가 매긴다 —
            // 코인이 먼저고, 단계는 고지서보다 앞이다 (그 이유는 GetServiceOrder 주석).
            Array.Sort(services, (a, b) => GetServiceOrder(a).CompareTo(GetServiceOrder(b)));
            _runScopedServices = services;
        }

        private static int GetServiceOrder(IRunScoped service)
        {
            var typeName = service.GetType().Name;
            if (typeName.Contains("Economy"))
            {
                return 1;
            }
            if (typeName.Contains("Stamina"))
            {
                return 2;
            }
            if (typeName.Contains("Fever"))
            {
                return 3;
            }
            if (typeName.Contains("Creature"))
            {
                return 4;
            }
            // 단계는 고지서보다 **먼저** 와야 한다 (#164). ARCHITECTURE "하루 종료 순서"가
            // "런 목표 판정 → ... → 미납 확정 시 파산"으로 이미 정해 둔 순서다. 근거는 EndRun 에서
            // StageGoalManager 는 목표를 채웠으면 단계를 올리고 BillManager 는 파산이면 0 으로
            // 되돌리는데, 뒤집히면 파산인데 단계가 올라가기 때문이다. 둘 다 기타(10)로 두면
            // 동점이라 Array.Sort 가 순서를 보장하지 않는다.
            if (typeName.Contains("Stage"))
            {
                return 5;
            }
            if (typeName.Contains("Bill"))
            {
                return 6;
            }
            return 10;
        }

        private void NotifyBeginRun()
        {
            if (_isRunActive)
            {
                return;
            }
            _isRunActive = true;
            WireSceneConsumers();
            EnsureRunScopedServices();
            if (_runScopedServices == null)
            {
                return;
            }

            Debug.Log($"[GameManager] NotifyBeginRun 실행 ({_runScopedServices.Length}개 IRunScoped 서비스 활성화)");
            for (int i = 0; i < _runScopedServices.Length; i++)
            {
                _runScopedServices[i].BeginRun();
            }
            NotifyScene(true);
        }

        private void NotifyEndRun()
        {
            if (!_isRunActive)
            {
                return;
            }
            _isRunActive = false;
            EnsureRunScopedServices();
            if (_runScopedServices == null)
            {
                return;
            }

            for (int i = 0; i < _runScopedServices.Length; i++)
            {
                _runScopedServices[i].EndRun();
            }
            NotifyScene(false);

            // 하루가 끝나는 이 지점이 저장 체크포인트다 (이슈 #203). **EndRun 을 전부 돌린 뒤**에
            // 저장한다 — 단계 진행과 파산 초기화가 여기서 일어나므로, 먼저 저장하면 한 판 뒤처진
            // 값이 남는다.
            SaveManager.Persistence?.CollectAndSave();
        }

        /// <summary>
        /// 씬에 사는 소비처를 조립한다 — 업그레이드 실효값 조회 통로(#131)를 넣고,
        /// 런 경계를 알릴 IRunScoped 구현체를 모아 둔다(#126).
        ///
        /// ManagerBootstrap 은 씬 로드 **전**에 돌아 이들에 닿지 못하고, 이들은 Managers 프리팹
        /// 밖이라 GetComponentsInChildren 으로도 잡히지 않는다. ARCHITECTURE 가 "조립하는 지점
        /// (ManagerBootstrap 또는 GameManager 초기화) 한 곳만 구현 클래스를 안다"고 정했으므로
        /// 여기가 그 한 곳이다 — 조립이 끝난 뒤의 상호작용은 인터페이스로만 한다.
        ///
        /// 씬이 다시 로드되면 인스턴스가 새로 생기므로 런을 시작할 때마다 다시 찾는다.
        ///
        /// **CreatureManager 는 여기서 다루지 않는다** (#140). Managers 프리팹으로 옮겨 갔으므로
        /// 업그레이드 주입은 ManagerBootstrap 이, 런 경계는 EnsureRunScopedServices 가 맡는다.
        /// 여기에 다시 넣으면 BeginRun() 이 두 경로로 각각 불려 두 번 실행되고, 두 번째 호출이
        /// 방금 켠 퍼크 반경을 지운다 (#126 회귀).
        ///
        /// **새 인스턴스를 찾은 직후 예약 퍼크부터 되돌려 준다** (#188 작업 중 발견). ContinueRun 의
        /// CarryOverScenePerks 가 이전 인스턴스에서 옮겨 둔 값을, BeginRun 이 pending→active 로
        /// 승격하기 전에 여기서 새 인스턴스에 넣어야 한다.
        /// </summary>
        private void WireSceneConsumers()
        {
            var hammer = FindFirstObjectByType<HammerSwingController>(FindObjectsInactive.Include);

            if (hammer != null && _pendingHammerPerkPowerPercent > 0f)
            {
                hammer.AddPendingPerkPowerPercent(_pendingHammerPerkPowerPercent);
                _pendingHammerPerkPowerPercent = 0f;
            }

            var upgradeStats = GetComponentInChildren<IUpgradeStats>(true);
            if (upgradeStats != null && hammer != null)
            {
                hammer.SetUpgradeStats(upgradeStats);
            }

            // 런 경계도 같이 넘긴다. 이것은 Managers 프리팹 밖이라
            // GetComponentsInChildren<IRunScoped> 에 잡히지 않는다 (#126).
            _sceneRunScopedServices = new IRunScoped[]
            {
                hammer,
            };
        }

        /// <summary>씬 구현체에 런 경계를 알린다. 아직 배선되지 않았으면 건너뛴다.</summary>
        private void NotifyScene(bool isBegin)
        {
            if (_sceneRunScopedServices == null)
            {
                return;
            }

            for (var i = 0; i < _sceneRunScopedServices.Length; i++)
            {
                var service = _sceneRunScopedServices[i];
                // 인터페이스 참조로는 Unity 의 "파괴됨" 판정이 걸리지 않는다. 씬이 바뀌어
                // 오브젝트가 이미 사라졌을 수 있으므로 UnityEngine.Object 로 되돌려 확인한다.
                if (service == null || (service is UnityEngine.Object behaviour && behaviour == null))
                {
                    continue;
                }
                if (isBegin)
                {
                    service.BeginRun();
                }
                else
                {
                    service.EndRun();
                }
            }
        }
    }
}
