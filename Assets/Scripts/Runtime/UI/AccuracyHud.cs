using NCAIClicker.Data;
using NCAIClicker.Events;
using TMPro;
using UnityEngine;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 이번 런의 정확도를 보여준다. 이슈 #33 완료 기준의 "정확도" — `적중 / 전체 스윙` 백분율.
    ///
    /// **자동 망치를 세지 않는다.** BALANCE 5절과 docs/TECH_NOTES/fever-gauge.md 가
    /// "호버·자동 망치의 적중 여부를 함께 전달하고 정확도는 호버만 집계한다" 로 정했다.
    /// 자동 망치는 항상 적중(HitSource.AutoHammer, true)으로 발행되므로 섞으면 정확도가
    /// 플레이어 실력이 아니라 업그레이드 레벨을 보여주게 된다.
    ///
    /// 분모가 맞는 근거: HammerSwingController 는 빗나간 스윙도 (Hover, false) 로 발행한다
    /// (docs/TECH_NOTES/hover-swing.md "명중 여부와 무관하게 매 스윙"). 망치가 상시 스윙하므로
    /// 헛스윙도 시간을 쓰는데, 그 손실을 보여주는 것이 이 숫자의 목적이다.
    ///
    /// **런 단위로 초기화한다** (이슈 본문: 누적 평균은 후반에 둔감해져 피드백이 안 된다).
    /// 초기화 지점은 OnEnable 이다 — Game 씬이 런마다 새로 로드되므로(GameManager.StartNewRun /
    /// ContinueRun 이 LoadScene 을 부른다) 이 위젯의 OnEnable 이 곧 런의 시작이다.
    /// IRunScoped 를 쓰지 않은 이유: 씬에 사는 IRunScoped 구현체는 GameManager 가 손으로 모으는
    /// 배열(WireSceneConsumers)에 이름이 박혀 있어, HUD 를 넣으려면 코어 플레이 모듈 파일을
    /// 고쳐야 한다. 씬 로드가 이미 런 경계와 일치하므로 그 대가를 치를 이유가 없다.
    /// </summary>
    public class AccuracyHud : MonoBehaviour
    {
        private const string PlaceholderText = "정확도 —";

        [SerializeField] private TextMeshProUGUI _label;

        private int _swingCount;
        private int _hitCount;

        private void Awake()
        {
            if (_label == null)
            {
                _label = GetComponent<TextMeshProUGUI>();
            }
        }

        private void OnEnable()
        {
            GameEvents.OnSwingResolved += HandleSwingResolved;

            // 런 단위 초기화. 씬 로드 = 런 시작이므로 여기가 그 지점이다.
            _swingCount = 0;
            _hitCount = 0;
            Render();
        }

        private void OnDisable()
        {
            GameEvents.OnSwingResolved -= HandleSwingResolved;
        }

        private void HandleSwingResolved(HitSource source, bool isHit)
        {
            if (source != HitSource.Hover)
            {
                return;
            }

            _swingCount++;
            if (isHit)
            {
                _hitCount++;
            }
            Render();
        }

        private void Render()
        {
            if (_label == null)
            {
                return;
            }

            if (_swingCount <= 0)
            {
                _label.text = PlaceholderText;
                return;
            }

            var percent = Mathf.RoundToInt(_hitCount * 100f / _swingCount);
            _label.text = $"정확도 {percent}% ({_hitCount}/{_swingCount})";
        }
    }
}
