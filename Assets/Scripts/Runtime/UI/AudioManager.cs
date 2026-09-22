using NCAIClicker.Data;
using NCAIClicker.Events;
using NCAIClicker.Interfaces;
using UnityEngine;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 타격·코인·피버·고지서 이벤트를 구독해 효과음을 재생한다. 완료 기준의 정본은 GitHub 이슈 #36.
    /// 다른 매니저를 참조하지 않고 GameEvents 만 듣는다 (AGENTS.md).
    ///
    /// 볼륨·화면 흔들림 설정 조회/적용(IAudioService)은 이슈 #196, 공용 계약 #202 에서 추가했다.
    /// Instance 는 EconomyManager.Instance(IEconomyService, #171)와 같은 패턴으로 인터페이스 타입만 연다.
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour, IAudioService
    {
        [SerializeField] private AudioClip _hitClip;
        [SerializeField] private AudioClip _coinClip;
        [SerializeField] private AudioClip _feverStartClip;
        [SerializeField] private AudioClip _billIssuedClip;
        [SerializeField] private AudioClip _billPaidClip;

        [Header("설정 (이슈 #196)")]
        [SerializeField] private AudioSource _bgmSource;

        public static IAudioService Instance { get; private set; }

        public float BgmVolume { get; private set; } = 1f;
        public float SfxVolume { get; private set; } = 1f;
        public bool IsScreenShakeEnabled { get; private set; } = true;

        private AudioSource _sfxSource;

        private void Awake()
        {
            _sfxSource = GetComponent<AudioSource>();
            Instance = this;

            var save = SaveManager.Instance?.Load();
            if (save != null)
            {
                SetBgmVolume(save.BgmVolume);
                SetSfxVolume(save.SfxVolume);
                SetScreenShakeEnabled(save.IsScreenShakeEnabled);
            }
        }

        public void SetBgmVolume(float linear01)
        {
            BgmVolume = Mathf.Clamp01(linear01);
            if (_bgmSource != null)
            {
                _bgmSource.volume = BgmVolume;
            }
        }

        public void SetSfxVolume(float linear01)
        {
            SfxVolume = Mathf.Clamp01(linear01);
            _sfxSource.volume = SfxVolume;
        }

        public void SetScreenShakeEnabled(bool enabled)
        {
            IsScreenShakeEnabled = enabled;
        }

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다 (AGENTS.md).
        private void OnEnable()
        {
            GameEvents.OnSwingResolved += HandleSwingResolved;
            GameEvents.OnCoinEarned += HandleCoinEarned;
            GameEvents.OnFeverStart += HandleFeverStart;
            GameEvents.OnBillIssued += HandleBillIssued;
            GameEvents.OnBillPaid += HandleBillPaid;
        }

        private void OnDisable()
        {
            GameEvents.OnSwingResolved -= HandleSwingResolved;
            GameEvents.OnCoinEarned -= HandleCoinEarned;
            GameEvents.OnFeverStart -= HandleFeverStart;
            GameEvents.OnBillIssued -= HandleBillIssued;
            GameEvents.OnBillPaid -= HandleBillPaid;
        }

        /// <summary>
        /// 호버 적중만 재생한다. 헛스윙까지 울리면 상시 스윙 중 소음이 끊이지 않고 (GDD 4절),
        /// 자동 망치까지 울리면 보유 수와 무관하게 초당 1회 타격음이 겹쳐 같은 문제가 난다.
        /// </summary>
        private void HandleSwingResolved(HitSource source, bool isHit)
        {
            if (isHit && source == HitSource.Hover)
            {
                Play(_hitClip);
            }
        }

        private void HandleCoinEarned(long amount)
        {
            Play(_coinClip);
        }

        private void HandleFeverStart()
        {
            Play(_feverStartClip);
        }

        private void HandleBillIssued(Bill bill)
        {
            Play(_billIssuedClip);
        }

        private void HandleBillPaid(Bill bill)
        {
            Play(_billPaidClip);
        }

        private void Play(AudioClip clip)
        {
            _sfxSource.PlayOneShot(clip);
        }
    }
}
