using NCAIClicker.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// MainMenu 씬의 새 회차 시작/이어하기/종료 버튼을 GameManager로 위임한다.
    /// 씬 전환은 GameManager가 조정하며 이 컨트롤러는 SceneManager를 직접 부르지 않는다 (이슈 #90).
    /// 저장 존재 여부는 SaveManager.Instance.HasSave(이슈 #139)로 판정한다.
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

        private void RefreshContinueButton()
        {
            var saveService = SaveManager.Instance;
            _continueButton.interactable = saveService != null && saveService.HasSave;
        }

        private void HandleNewRunClicked()
        {
            var saveService = SaveManager.Instance;
            if (saveService != null && saveService.HasSave)
            {
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
            _settingsPanel.Open();
        }
    }
}
