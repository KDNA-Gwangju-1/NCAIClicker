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
    /// Result 전이는 씬 전환 없이 OnStaminaDepleted/OnBankrupt 로 결정한다 (ARCHITECTURE.md 2절
    /// "하루 종료 순서" — GameManager 만 이 두 이벤트를 구독한다).
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

        /// <summary>
        /// 씬에 사는 런 경계 구현체 (#126). 프리팹 안의 것과 달리 캐시하지 않는다 —
        /// 씬이 다시 로드되면 인스턴스가 새로 생긴다.
        /// </summary>
        private IRunScoped[] _sceneRunScopedServices;

        private bool _isRunActive;

        private void Awake()
        {
            Instance = this;
            CurrentState = ResolveState(SceneManager.GetActiveScene().name) ?? RunState.MainMenu;
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
        /// </summary>
        public void StartNewRun()
        {
            SaveManager.Instance?.Save(new SaveData());
            SceneManager.LoadScene(GameSceneName);
        }

        /// <summary>이어하기. Game 씬으로 전환한다. 저장값 실제 복원 배선은 알려진 한계 — docs/TECH_NOTES/main-menu.md 참고.</summary>
        public void ContinueRun()
        {
            SceneManager.LoadScene(GameSceneName);
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
            if (next.HasValue)
            {
                SetState(next.Value);
            }
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
            // ARCHITECTURE 1절 초기화 순서 준수: 코인(Economy) -> 스태미나/피버
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
        /// </summary>
        private void WireSceneConsumers()
        {
            var hammer = FindFirstObjectByType<HammerSwingController>(FindObjectsInactive.Include);

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
