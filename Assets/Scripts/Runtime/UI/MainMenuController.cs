using NCAIClicker.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// MainMenu 씬의 새 회차 시작/이어하기/종료 버튼을 GameManager로 위임한다.
    /// 씬 전환은 GameManager가 조정하며 이 컨트롤러는 SceneManager를 직접 부르지 않는다 (이슈 #90).
    /// 저장 존재 여부는 SaveManager.Instance.HasSave(이슈 #139)로 판정한다.
    /// 이어하기 라벨·우하단 캡션·덮어쓰기 부제의 "N일차 · 레거시 N LP" 는 저장을 읽어 채운다 (이슈 #184).
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private Button _newRunButton;
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private GameObject _overwriteConfirmPanel;
        [SerializeField] private Button _overwriteConfirmYesButton;
        [SerializeField] private Button _overwriteConfirmNoButton;

        [Header("설정 (이슈 #196)")]
        [SerializeField] private Button _settingsButton;
        [SerializeField] private SettingsPanelController _settingsPanel;

        [Header("저장 상태 표시 (이슈 #184)")]
        [SerializeField] private TMP_Text _continueLabel;
        [SerializeField] private TMP_Text _saveStatusText;
        [SerializeField] private TMP_Text _overwriteConfirmSubtitle;

        private const string ContinueLabelBase = "이어하기";
        private const string VersionLine = "v0.1 · NCAI Team Two";

        private void Awake()
        {
            if (_overwriteConfirmPanel != null)
            {
                _overwriteConfirmPanel.SetActive(false);
            }
        }

        private void OnEnable()
        {
            RefreshContinueButton();

            _newRunButton.onClick.AddListener(HandleNewRunClicked);
            _continueButton.onClick.AddListener(HandleContinueClicked);
            _quitButton.onClick.AddListener(HandleQuitClicked);
            _overwriteConfirmYesButton.onClick.AddListener(HandleOverwriteConfirmed);
            _overwriteConfirmNoButton.onClick.AddListener(HandleOverwriteCanceled);
            _settingsButton.onClick.AddListener(HandleSettingsClicked);
        }

        private void OnDisable()
        {
            _newRunButton.onClick.RemoveListener(HandleNewRunClicked);
            _continueButton.onClick.RemoveListener(HandleContinueClicked);
            _quitButton.onClick.RemoveListener(HandleQuitClicked);
            _overwriteConfirmYesButton.onClick.RemoveListener(HandleOverwriteConfirmed);
            _overwriteConfirmNoButton.onClick.RemoveListener(HandleOverwriteCanceled);
            _settingsButton.onClick.RemoveListener(HandleSettingsClicked);
        }

        // 덮어쓰기 확인이 떠 있을 때 ESC 는 취소다 — 다이얼로그의 "ESC — 취소" 안내와 맞춘다 (이슈 #184)
        private void Update()
        {
            if (_overwriteConfirmPanel == null || !_overwriteConfirmPanel.activeSelf)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                HandleOverwriteCanceled();
            }
        }

        private void RefreshContinueButton()
        {
            var saveService = SaveManager.Instance;
            var hasSave = saveService != null && saveService.HasSave;
            _continueButton.interactable = hasSave;

            if (!hasSave)
            {
                SetText(_continueLabel, ContinueLabelBase);
                SetText(_saveStatusText, $"저장 없음\n{VersionLine}");
                SetText(_overwriteConfirmSubtitle, "저장된 회차가 있다");
                return;
            }

            var save = saveService.Load();
            var dayText = $"{save.CurrentDay}일차";
            SetText(_continueLabel, $"{ContinueLabelBase} <size=75%><color=#C8B79A>· {dayText}</color></size>");
            SetText(_saveStatusText, $"저장됨 · {dayText} · 레거시 {save.LegacyPoints} LP\n{VersionLine}");
            SetText(_overwriteConfirmSubtitle, $"저장된 회차가 있다 · {dayText}");
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        private void HandleNewRunClicked()
        {
            var saveService = SaveManager.Instance;
            if (saveService != null && saveService.HasSave)
            {
                _overwriteConfirmPanel.transform.SetAsLastSibling();
                _overwriteConfirmPanel.SetActive(true);
                return;
            }

            GameManager.Instance?.StartNewRun();
        }

        private void HandleContinueClicked()
        {
            GameManager.Instance?.ContinueRun();
        }

        private void HandleQuitClicked()
        {
            GameManager.Instance?.QuitGame();
        }

        private void HandleOverwriteConfirmed()
        {
            _overwriteConfirmPanel.SetActive(false);
            GameManager.Instance?.StartNewRun();
        }

        private void HandleOverwriteCanceled()
        {
            _overwriteConfirmPanel.SetActive(false);
        }

        private void HandleSettingsClicked()
        {
            _settingsPanel.transform.SetAsLastSibling();
            _settingsPanel.Open();
        }
    }
}
