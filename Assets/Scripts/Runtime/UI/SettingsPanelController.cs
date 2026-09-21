using NCAIClicker.Data;
using NCAIClicker.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 설정 패널. MainMenu·Game(일시정지 경유) 양쪽에서 같은 프리팹으로 연다 (이슈 #196).
    /// 볼륨·화면 흔들림은 AudioManager.Instance(IAudioService, 계약 #202)에만 적용을 맡기고,
    /// 이 컨트롤러는 원시값(슬라이더 0~1, 버튼 on/off)만 넘긴다 (AGENTS.md).
    /// 창 모드는 매니저를 거치지 않고 Screen.fullScreen을 직접 쓴다 — Unity API 호출일 뿐 매니저 간
    /// 상태 전달이 아니다.
    /// </summary>
    public class SettingsPanelController : MonoBehaviour
    {
        [Header("소리")]
        [SerializeField] private Slider _bgmSlider;
        [SerializeField] private TextMeshProUGUI _bgmValueText;
        [SerializeField] private Slider _sfxSlider;
        [SerializeField] private TextMeshProUGUI _sfxValueText;

        [Header("화면")]
        [SerializeField] private Button _fullscreenButton;
        [SerializeField] private TextMeshProUGUI _fullscreenLabel;
        [SerializeField] private Button _windowedButton;
        [SerializeField] private TextMeshProUGUI _windowedLabel;
        [SerializeField] private Button _screenShakeToggleButton;
        [SerializeField] private TextMeshProUGUI _screenShakeStatusText;

        [Header("화면 — 선택 표시 색 (레이아웃 명세 #192 댓글)")]
        [SerializeField] private Color _selectedBg = new Color32(0xE8, 0xB0, 0x4B, 255);
        [SerializeField] private Color _selectedText = new Color32(0x16, 0x13, 0x0F, 255);
        [SerializeField] private Color _neutralBg = new Color32(0x3B, 0x35, 0x30, 255);
        [SerializeField] private Color _neutralText = new Color32(0xE8, 0xDD, 0xCB, 255);

        [Header("저장 데이터 초기화")]
        [SerializeField] private Button _resetButton;
        [SerializeField] private GameObject _resetConfirmPanel;
        [SerializeField] private Button _resetConfirmYesButton;
        [SerializeField] private Button _resetConfirmNoButton;

        [Header("닫기")]
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _closeButton;

        public event System.Action Closed;

        private void Awake()
        {
            if (_resetConfirmPanel != null)
            {
                _resetConfirmPanel.SetActive(false);
            }
        }

        private void OnEnable()
        {
            RefreshFromSave();

            _bgmSlider.onValueChanged.AddListener(HandleBgmSliderChanged);
            _sfxSlider.onValueChanged.AddListener(HandleSfxSliderChanged);
            _fullscreenButton.onClick.AddListener(HandleFullscreenClicked);
            _windowedButton.onClick.AddListener(HandleWindowedClicked);
            _screenShakeToggleButton.onClick.AddListener(HandleScreenShakeToggleClicked);
            _resetButton.onClick.AddListener(HandleResetClicked);
            _resetConfirmYesButton.onClick.AddListener(HandleResetConfirmed);
            _resetConfirmNoButton.onClick.AddListener(HandleResetCanceled);
            _closeButton.onClick.AddListener(Close);
            _backButton.onClick.AddListener(Close);
        }

        private void OnDisable()
        {
            _bgmSlider.onValueChanged.RemoveListener(HandleBgmSliderChanged);
            _sfxSlider.onValueChanged.RemoveListener(HandleSfxSliderChanged);
            _fullscreenButton.onClick.RemoveListener(HandleFullscreenClicked);
            _windowedButton.onClick.RemoveListener(HandleWindowedClicked);
            _screenShakeToggleButton.onClick.RemoveListener(HandleScreenShakeToggleClicked);
            _resetButton.onClick.RemoveListener(HandleResetClicked);
            _resetConfirmYesButton.onClick.RemoveListener(HandleResetConfirmed);
            _resetConfirmNoButton.onClick.RemoveListener(HandleResetCanceled);
            _closeButton.onClick.RemoveListener(Close);
            _backButton.onClick.RemoveListener(Close);
        }

        public void Open()
        {
            gameObject.SetActive(true);
            // 여는 버튼(메인 메뉴의 설정 버튼 등)이 이 패널보다 나중에 캔버스에 추가돼 있으면
            // 그리기 순서상 패널 위에 겹쳐 보인다 — 열 때마다 형제 목록 맨 뒤로 보내 항상 맨 위에 그린다.
            transform.SetAsLastSibling();
            // ContentSizeFitter는 다음 프레임에야 계산돼 첫 프레임에 패널 높이가 0으로 보일 수 있다.
            if (transform is RectTransform rectTransform)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
            }
        }

        public void Close()
        {
            PersistCurrentSettings();

            if (_resetConfirmPanel != null)
            {
                _resetConfirmPanel.SetActive(false);
            }

            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void RefreshFromSave()
        {
            var audio = AudioManager.Instance;
            var bgm = audio?.BgmVolume ?? 1f;
            var sfx = audio?.SfxVolume ?? 1f;
            var shakeOn = audio?.IsScreenShakeEnabled ?? true;

            _bgmSlider.SetValueWithoutNotify(bgm);
            RefreshVolumeLabel(_bgmValueText, bgm);
            _sfxSlider.SetValueWithoutNotify(sfx);
            RefreshVolumeLabel(_sfxValueText, sfx);

            RefreshWindowModeButtons(Screen.fullScreen);
            RefreshScreenShakeVisual(shakeOn);
        }

        // 볼륨·창모드·화면 흔들림은 여기서 즉시 반영만 하고, 디스크 저장은 Close()에서 한 번만 한다 —
        // 슬라이더 드래그 중 매 프레임 onValueChanged가 울려 그때마다 저장 파일을 다시 쓰면
        // 성능 낭비이자 쓰기 경합 위험이다.
        private void HandleBgmSliderChanged(float value)
        {
            AudioManager.Instance?.SetBgmVolume(value);
            RefreshVolumeLabel(_bgmValueText, value);
        }

        private void HandleSfxSliderChanged(float value)
        {
            AudioManager.Instance?.SetSfxVolume(value);
            RefreshVolumeLabel(_sfxValueText, value);
        }

        private void HandleFullscreenClicked()
        {
            Screen.fullScreen = true;
            RefreshWindowModeButtons(true);
        }

        private void HandleWindowedClicked()
        {
            Screen.fullScreen = false;
            RefreshWindowModeButtons(false);
        }

        private void HandleScreenShakeToggleClicked()
        {
            var audio = AudioManager.Instance;
            var next = !(audio?.IsScreenShakeEnabled ?? true);
            audio?.SetScreenShakeEnabled(next);
            RefreshScreenShakeVisual(next);
        }

        private void HandleResetClicked()
        {
            if (_resetConfirmPanel != null)
            {
                _resetConfirmPanel.SetActive(true);
            }
        }

        /// <summary>
        /// 업그레이드·납부 기록만 지운다. 방금 조정한 볼륨·창모드·화면 흔들림 값은 그대로 남는다 —
        /// DoD가 "업그레이드와 납부 기록이 사라진다"고만 했고 설정 초기화는 요구하지 않았다.
        ///
        /// **파일만 비우던 것을 ResetAndDistribute 로 바꿨다** (이슈 #203). 매니저는
        /// DontDestroyOnLoad 라 파일을 비워도 업그레이드 레벨·레거시 포인트·반지를 메모리에
        /// 그대로 들고 있었고, 3.10 이 붙인 자동 저장이 그 값을 파일에 도로 써서 **초기화가
        /// 없던 일이 됐다.** 이제 메모리까지 함께 비운다.
        ///
        /// 설정을 먼저 파일에 반영하는 이유는 `ResetAndDistribute` 가 설정을 넘겨받지 않고
        /// 현재 저장에서 옮겨 담기 때문이다 — 설정은 패널을 닫을 때만 반영되므로, 열어 둔 채
        /// 초기화하면 방금 조정한 값이 아니라 옛 값이 살아남는다.
        /// </summary>
        private void HandleResetConfirmed()
        {
            PersistCurrentSettings();
            SaveManager.Persistence?.ResetAndDistribute();

            if (_resetConfirmPanel != null)
            {
                _resetConfirmPanel.SetActive(false);
            }
        }

        private void HandleResetCanceled()
        {
            if (_resetConfirmPanel != null)
            {
                _resetConfirmPanel.SetActive(false);
            }
        }

        private void PersistCurrentSettings()
        {
            var saveService = SaveManager.Instance;
            if (saveService == null)
            {
                return;
            }

            var data = saveService.Load();
            var audio = AudioManager.Instance;
            data.BgmVolume = audio?.BgmVolume ?? 1f;
            data.SfxVolume = audio?.SfxVolume ?? 1f;
            data.IsFullscreen = Screen.fullScreen;
            data.IsScreenShakeEnabled = audio?.IsScreenShakeEnabled ?? true;
            saveService.Save(data);
        }

        private void RefreshWindowModeButtons(bool isFullscreen)
        {
            SetSelected(_fullscreenButton, _fullscreenLabel, isFullscreen);
            SetSelected(_windowedButton, _windowedLabel, !isFullscreen);
        }

        private void SetSelected(Button button, TextMeshProUGUI label, bool selected)
        {
            var bg = selected ? _selectedBg : _neutralBg;
            var text = selected ? _selectedText : _neutralText;

            if (button.targetGraphic != null)
            {
                button.targetGraphic.color = bg;
            }

            if (label != null)
            {
                label.color = text;
            }
        }

        private void RefreshScreenShakeVisual(bool isOn)
        {
            if (_screenShakeToggleButton != null && _screenShakeToggleButton.targetGraphic != null)
            {
                _screenShakeToggleButton.targetGraphic.color = isOn ? _selectedBg : _neutralBg;
            }

            if (_screenShakeStatusText != null)
            {
                _screenShakeStatusText.text = isOn
                    ? "켬 — 타격 연출의 카메라 흔들림"
                    : "꺼짐 — 타격 연출의 카메라 흔들림";
            }
        }

        private static void RefreshVolumeLabel(TextMeshProUGUI label, float linear01)
        {
            if (label != null)
            {
                label.text = Mathf.RoundToInt(linear01 * 100f).ToString();
            }
        }
    }
}
