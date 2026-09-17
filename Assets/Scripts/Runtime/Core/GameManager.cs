using NCAIClicker.Events;
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
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        private const string MainMenuSceneName = "MainMenu";
        private const string GameSceneName = "Game";

        public static GameManager Instance { get; private set; }

        public RunState CurrentState { get; private set; }

        private void Awake()
        {
            Instance = this;
            CurrentState = ResolveState(SceneManager.GetActiveScene().name) ?? RunState.MainMenu;
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
        }
    }
}
