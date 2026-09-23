using System;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 게임 중 ESC 입력으로 호출되는 일시정지 패널을 제어한다 (이슈 #192, 작업 6.13).
    ///
    /// 시간 정지(Time.timeScale = 0)와 원래 값 복원, 퍼크 선택 도중 ESC 입력 차단,
    /// 설정 패널(#196) 조립 연동, 메인 메뉴 이동 및 종료 시 확인 팝업 흐름을 처리한다.
    /// Game.unity 씬에 EventSystem 이 없을 때를 대비한 예비 입력 처리기를 포함한다.
    /// </summary>
    public class PausePanelController : MonoBehaviour
    {
        private const string MainMenuSceneName = "MainMenu";

        [Header("루트 및 메인 패널")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private GameObject _mainPanel;

        [Header("텍스트")]
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _subtitleText;

        [Header("버튼")]
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _mainMenuButton;
        [SerializeField] private Button _quitButton;

        [Header("설정 패널 연동 (#196)")]
        [SerializeField] private SettingsPanelController _settingsPanel;

        [Header("확인 대화상자")]
        [SerializeField] private GameObject _confirmDialogRoot;
        [SerializeField] private TextMeshProUGUI _confirmTitleText;
        [SerializeField] private TextMeshProUGUI _confirmMessageText;
        [SerializeField] private Button _confirmOkButton;
        [SerializeField] private Button _confirmCancelButton;

        [Header("예비 EventSystem")]
        [SerializeField] private GameObject _fallbackEventSystem;

        private float _timeScaleBeforePause = 1f;
        private bool _isPaused;
        private ConfirmTarget _confirmTarget = ConfirmTarget.None;

        private float _cachedCurrentStamina;
        private float _cachedMaxStamina;
        private bool _hasCachedStamina;
        private bool _ownsFallbackEventSystem;

        private enum ConfirmTarget
        {
            None,
            MainMenu,
            Quit
        }

        public static event Action OnSettingsRequested;

        public bool IsPaused => _isPaused;
        public bool IsConfirmDialogOpen => _confirmDialogRoot != null && _confirmDialogRoot.activeSelf;
        public bool IsSettingsOpen => _settingsPanel != null && _settingsPanel.gameObject.activeSelf;
        public float TimeScaleBeforePause => _timeScaleBeforePause;

        public void SetSettingsPanel(SettingsPanelController settingsPanel)
        {
            if (_settingsPanel != null)
            {
                _settingsPanel.Closed -= HandleSettingsClosed;
                _settingsPanel.ResetPerformed -= HandleSettingsReset;
            }

            _settingsPanel = settingsPanel;

            if (_settingsPanel != null && enabled)
            {
                _settingsPanel.Closed += HandleSettingsClosed;
                _settingsPanel.ResetPerformed += HandleSettingsReset;
            }
        }

        private void Awake()
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }

            if (_confirmDialogRoot != null)
            {
                _confirmDialogRoot.SetActive(false);
            }

            if (_fallbackEventSystem != null)
            {
                _fallbackEventSystem.SetActive(false);
            }

            if (_settingsPanel != null)
            {
                _settingsPanel.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            GameEvents.OnStaminaChanged += HandleStaminaChanged;

            if (_settingsPanel != null)
            {
                _settingsPanel.Closed += HandleSettingsClosed;
                _settingsPanel.ResetPerformed += HandleSettingsReset;
            }

            if (_resumeButton != null)
            {
                _resumeButton.onClick.AddListener(HandleResumeClicked);
            }
            if (_settingsButton != null)
            {
                _settingsButton.onClick.AddListener(HandleSettingsClicked);
            }
            if (_mainMenuButton != null)
            {
                _mainMenuButton.onClick.AddListener(HandleMainMenuClicked);
            }
            if (_quitButton != null)
            {
                _quitButton.onClick.AddListener(HandleQuitClicked);
            }
            if (_confirmOkButton != null)
            {
                _confirmOkButton.onClick.AddListener(HandleConfirmOkClicked);
            }
            if (_confirmCancelButton != null)
            {
                _confirmCancelButton.onClick.AddListener(HandleConfirmCancelClicked);
            }
        }

        private void OnDisable()
        {
            GameEvents.OnStaminaChanged -= HandleStaminaChanged;

            if (_settingsPanel != null)
            {
                _settingsPanel.Closed -= HandleSettingsClosed;
                _settingsPanel.ResetPerformed -= HandleSettingsReset;
            }

            if (_resumeButton != null)
            {
                _resumeButton.onClick.RemoveListener(HandleResumeClicked);
            }
            if (_settingsButton != null)
            {
                _settingsButton.onClick.RemoveListener(HandleSettingsClicked);
            }
            if (_mainMenuButton != null)
            {
                _mainMenuButton.onClick.RemoveListener(HandleMainMenuClicked);
            }
            if (_quitButton != null)
            {
                _quitButton.onClick.RemoveListener(HandleQuitClicked);
            }
            if (_confirmOkButton != null)
            {
                _confirmOkButton.onClick.RemoveListener(HandleConfirmOkClicked);
            }
            if (_confirmCancelButton != null)
            {
                _confirmCancelButton.onClick.RemoveListener(HandleConfirmCancelClicked);
            }

            ResumeGame();
        }

        private void OnDestroy()
        {
            ResumeGame();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                HandleEscapePressed();
            }
        }

        public void HandleEscapePressed()
        {
            if (IsPerkChoiceActive())
            {
                return;
            }

            if (_confirmDialogRoot != null && _confirmDialogRoot.activeSelf)
            {
                CloseConfirmDialog();
                return;
            }

            if (_settingsPanel != null && _settingsPanel.gameObject.activeSelf)
            {
                _settingsPanel.Close();
                return;
            }

            if (_isPaused)
            {
                ResumeGame();
            }
            else
            {
                PauseGame();
            }
        }

        public void PauseGame()
        {
            if (_isPaused)
            {
                return;
            }

            _timeScaleBeforePause = Time.timeScale;
            _isPaused = true;
            Time.timeScale = 0f;

            UpdateSubtitle();

            if (_panelRoot != null)
            {
                _panelRoot.SetActive(true);
            }
            if (_mainPanel != null)
            {
                _mainPanel.SetActive(true);
            }
            if (_confirmDialogRoot != null)
            {
                _confirmDialogRoot.SetActive(false);
            }
            if (_settingsPanel != null)
            {
                _settingsPanel.gameObject.SetActive(false);
            }

            EnableFallbackEventSystemIfNeeded();
        }

        public void ResumeGame()
        {
            if (!_isPaused)
            {
                return;
            }

            _isPaused = false;
            Time.timeScale = _timeScaleBeforePause;

            if (_confirmDialogRoot != null)
            {
                _confirmDialogRoot.SetActive(false);
            }
            if (_settingsPanel != null && _settingsPanel.gameObject.activeSelf)
            {
                _settingsPanel.Close();
            }
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }

            DisableFallbackEventSystem();
        }

        private void HandleResumeClicked()
        {
            ResumeGame();
        }

        private void HandleSettingsClicked()
        {
            OnSettingsRequested?.Invoke();

            if (_settingsPanel != null)
            {
                if (_mainPanel != null)
                {
                    _mainPanel.SetActive(false);
                }
                _settingsPanel.Open();
            }
            else
            {
                Debug.Log("[PausePanelController] 설정 버튼 클릭됨 (설정 패널 미연결)");
            }
        }

        private void HandleSettingsClosed()
        {
            if (_isPaused && _mainPanel != null)
            {
                _mainPanel.SetActive(true);
            }
        }

        /// <summary>
        /// 설정 패널이 저장을 초기화했다 (이슈 #220). 진행 중이던 런을 접고 메인 메뉴로 보낸다 —
        /// 저장을 지워도 **이미 시작된 런은 되감기지 않기 때문**이다. BeginRun 이 이미 돌아
        /// 그 판의 크리처·스태미나·피버가 살아 있는 채로 저장 쪽 상태(코인·성장·단계·날짜·고지서)만
        /// 새 회차 값이 되어 앞뒤가 맞지 않는다.
        ///
        /// **"메인 메뉴로" 버튼과 같은 경로를 탄다.** 확인 절차는 설정 패널이 이미 거쳤으므로
        /// 여기서 다시 묻지 않는다 — 확인을 두 곳에 두면 한쪽을 건너뛰는 경로가 생긴다
        /// (BillManager.DeclareBankruptcy 주석, #175).
        ///
        /// timeScale 복원을 설정 패널이 아니라 여기서 하는 이유는 **원래 값을 아는 것이 이 클래스뿐**
        /// 이기 때문이다. 일시정지 중이면 0 이라, 복원하지 않고 씬을 넘기면 메뉴가 0배속으로 열린다.
        /// </summary>
        private void HandleSettingsReset()
        {
            _isPaused = false;
            Time.timeScale = _timeScaleBeforePause;
            SceneManager.LoadScene(MainMenuSceneName);
        }

        private void HandleMainMenuClicked()
        {
            OpenConfirmDialog(ConfirmTarget.MainMenu);
        }

        private void HandleQuitClicked()
        {
            OpenConfirmDialog(ConfirmTarget.Quit);
        }

        private void OpenConfirmDialog(ConfirmTarget target)
        {
            _confirmTarget = target;

            if (_confirmTitleText != null)
            {
                _confirmTitleText.text = target == ConfirmTarget.MainMenu
                    ? "메인 메뉴로 이동"
                    : "게임 종료";
            }

            if (_confirmMessageText != null)
            {
                _confirmMessageText.text = target == ConfirmTarget.MainMenu
                    ? "오늘 벌어들인 코인은 모두 사라집니다.\n메인 메뉴로 이동하시겠습니까?"
                    : "오늘 벌어들인 코인은 모두 사라집니다.\n게임을 종료하시겠습니까?";
            }

            if (_confirmDialogRoot != null)
            {
                _confirmDialogRoot.SetActive(true);
            }
        }

        private void CloseConfirmDialog()
        {
            _confirmTarget = ConfirmTarget.None;
            if (_confirmDialogRoot != null)
            {
                _confirmDialogRoot.SetActive(false);
            }
        }

        private void HandleConfirmOkClicked()
        {
            var target = _confirmTarget;
            CloseConfirmDialog();

            _isPaused = false;
            Time.timeScale = _timeScaleBeforePause;

            if (target == ConfirmTarget.MainMenu)
            {
                SceneManager.LoadScene(MainMenuSceneName);
            }
            else if (target == ConfirmTarget.Quit)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        private void HandleConfirmCancelClicked()
        {
            CloseConfirmDialog();
        }

        private void HandleStaminaChanged(float current, float max)
        {
            _cachedCurrentStamina = current;
            _cachedMaxStamina = max;
            _hasCachedStamina = true;

            if (_isPaused)
            {
                UpdateSubtitle();
            }
        }

        private void UpdateSubtitle()
        {
            if (_subtitleText == null)
            {
                return;
            }

            var day = BillManager.Instance != null ? BillManager.Instance.CurrentDay : 1;

            if (_hasCachedStamina)
            {
                var cur = Mathf.CeilToInt(_cachedCurrentStamina);
                var max = Mathf.CeilToInt(_cachedMaxStamina);
                _subtitleText.text = $"{day}일차 · 스태미나 {cur} / {max}";
            }
            else
            {
                _subtitleText.text = $"{day}일차";
            }
        }

        private static bool IsPerkChoiceActive()
        {
            var pendingPerks = BillManager.Instance?.OfferedPerkIds;
            if (pendingPerks != null && pendingPerks.Length > 0)
            {
                return true;
            }

            return false;
        }

        private void EnableFallbackEventSystemIfNeeded()
        {
            if (_fallbackEventSystem == null)
            {
                return;
            }

            if (EventSystem.current == null)
            {
                _fallbackEventSystem.SetActive(true);
                _ownsFallbackEventSystem = true;
            }
        }

        private void DisableFallbackEventSystem()
        {
            if (_ownsFallbackEventSystem && _fallbackEventSystem != null)
            {
                _fallbackEventSystem.SetActive(false);
                _ownsFallbackEventSystem = false;
            }
        }
    }
}
