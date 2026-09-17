using System;
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
    public class GameManager : MonoBehaviour
    {
        private const string MainMenuSceneName = "MainMenu";
        private const string GameSceneName = "Game";

        public static GameManager Instance { get; private set; }

        public RunState CurrentState { get; private set; }

        private IRunScoped[] _runScopedServices;
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
            return 10;
        }

        private void NotifyBeginRun()
        {
            if (_isRunActive)
            {
                return;
            }
            _isRunActive = true;
            WireSceneUpgradeConsumers();
            EnsureRunScopedServices();
            if (_runScopedServices == null)
            {
                return;
            }

            for (int i = 0; i < _runScopedServices.Length; i++)
            {
                _runScopedServices[i].BeginRun();
            }
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
        }

        /// <summary>
        /// 씬에 사는 업그레이드 소비처에 실효값 조회 통로를 넣는다 (#131).
        ///
        /// ManagerBootstrap 은 씬 로드 **전**에 돌아 이들에 닿지 못하고, 이들은 Managers 프리팹
        /// 밖이라 GetComponentsInChildren 으로도 잡히지 않는다. ARCHITECTURE 가 "조립하는 지점
        /// (ManagerBootstrap 또는 GameManager 초기화) 한 곳만 구현 클래스를 안다"고 정했으므로
        /// 여기가 그 한 곳이다 — 조립이 끝난 뒤의 상호작용은 인터페이스로만 한다.
        ///
        /// 씬이 다시 로드되면 인스턴스가 새로 생기므로 런을 시작할 때마다 다시 찾는다.
        /// </summary>
        private void WireSceneUpgradeConsumers()
        {
            var upgradeStats = GetComponentInChildren<IUpgradeStats>(true);
            if (upgradeStats == null)
            {
                return;
            }

            var hammer = FindFirstObjectByType<HammerSwingController>(FindObjectsInactive.Include);
            if (hammer != null)
            {
                hammer.SetUpgradeStats(upgradeStats);
            }

            var creatures = FindFirstObjectByType<CreatureManager>(FindObjectsInactive.Include);
            if (creatures != null)
            {
                creatures.SetUpgradeStats(upgradeStats);
            }
        }
    }
}
