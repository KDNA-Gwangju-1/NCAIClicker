using NCAIClicker.Data;
using NCAIClicker.Events;
using UnityEngine;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 타격·코인·피버·청구서 이벤트를 구독해 효과음을 재생한다. 완료 기준의 정본은 GitHub 이슈 #36.
    /// 다른 매니저를 참조하지 않고 GameEvents 만 듣는다 (AGENTS.md).
    ///
    /// Managers 프리팹(Resources/Managers)에 붙인다. 생성은 ManagerBootstrap 이 한다.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        [SerializeField] private AudioClip _hitClip;
        [SerializeField] private AudioClip _coinClip;
        [SerializeField] private AudioClip _feverStartClip;
        [SerializeField] private AudioClip _billIssuedClip;
        [SerializeField] private AudioClip _billPaidClip;

        private AudioSource _sfxSource;

        private void Awake()
        {
            _sfxSource = GetComponent<AudioSource>();
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

        /// <summary>적중만 재생한다 — 헛스윙까지 울리면 상시 스윙 중 소음이 끊이지 않는다 (GDD 4절).</summary>
        private void HandleSwingResolved(HitSource source, bool isHit)
        {
            if (isHit)
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
