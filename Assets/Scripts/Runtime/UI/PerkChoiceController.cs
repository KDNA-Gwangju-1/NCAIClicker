using NCAIClicker.Data;
using NCAIClicker.Economy;
using NCAIClicker.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 납부 직후 퍼크 3장을 띄우고 한 장을 고르게 한다 (이슈 #92, GDD 6.9절).
    ///
    /// Managers 프리팹에 붙는다. 씬이 아니라 여기 붙는 이유는 두 가지다.
    ///   - `Game.unity` 와 `GameHud.prefab` 은 다른 담당의 것이라 건드리지 않는다 (AGENTS.md).
    ///   - 씬에 두면 누군가 배치해 주기 전까지 아무도 부르지 않는 코드가 된다. 실제로
    ///     #131·#126·#164 가 전부 그렇게 죽어 있었다.
    ///
    /// **후보 풀·뽑기·효과 적용은 4.2 의 몫이다** (#28 이 뽑고 #126 이 적용한다). 이 클래스는
    /// 제시와 선택 입력만 맡고, 고른 결과는 IBillService.TryChoosePerk 로 되돌려준다.
    /// </summary>
    public class PerkChoiceController : MonoBehaviour
    {
        /// <summary>
        /// 퍼크 이름·효과의 출처. 다른 매니저와 같은 방식으로 프리팹에서 넣어 준다.
        /// 없으면 카드를 채울 수 없으므로 패널을 띄우지 않는다.
        /// </summary>
        [SerializeField] private BalanceData _balanceData;

        /// <summary>
        /// 띄울 패널 프리팹. Resources.Load 대신 참조로 받는다 — 문자열 이름은 오타가 나도
        /// 컴파일이 통과하고 실행 시점에야 LogError 로 드러난다.
        /// </summary>
        [SerializeField] private GameObject _panelPrefab;

        private GameObject _panelInstance;
        private PerkCardView[] _cards;

        /// <summary>
        /// 이 씬에 EventSystem 이 없을 때만 켜는 예비 입력 처리기. 패널 프리팹 안에 꺼진 채로 들어 있다.
        /// </summary>
        private GameObject _fallbackEventSystem;

        /// <summary>정지 직전의 timeScale. 1 을 가정하지 않는다 — 나중에 배속 기능이 생겨도 돌려놓을 값은 원래 값이다.</summary>
        private float _timeScaleBeforePause;

        private bool _isPaused;

        // 정적 이벤트는 구독과 해제를 쌍으로 맞춘다 (AGENTS.md).
        private void OnEnable()
        {
            GameEvents.OnPerkOffered += HandlePerkOffered;
            SceneManager.sceneLoaded += HandleSceneLoaded;

            // 구독만으로는 **이미 나와 있는 후보를 놓친다.** 발행은 한 번뿐이라 그 시점에
            // 꺼져 있었으면 영영 못 받는다 — 저장에서 복원된 선택 대기 상태(SaveData.OfferedPerkIds)가
            // 그 경우다. ARCHITECTURE 3절 "UI는 구독 후 공용 조회 인터페이스로 초기 상태를 한 번 읽는다".
            var pending = BillManager.Instance?.OfferedPerkIds;
            if (pending != null && pending.Length > 0)
            {
                HandlePerkOffered(pending);
            }
        }

        private void OnDisable()
        {
            GameEvents.OnPerkOffered -= HandlePerkOffered;
            SceneManager.sceneLoaded -= HandleSceneLoaded;

            // 패널이 열린 채로 비활성화되면 게임이 영구 정지한다. 시간은 무슨 일이 있어도 되돌린다.
            ResumeTime();
            HidePanel();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Game")
            {
                return;
            }

            var pending = BillManager.Instance?.OfferedPerkIds;
            if (pending != null && pending.Length > 0)
            {
                HandlePerkOffered(pending);
            }
        }

        private void OnDestroy()
        {
            ResumeTime();
        }

        /// <summary>
        /// 후보가 오면 패널을 띄우고 **시간을 멈춘다.** GDD 6.9절이 "스태미나·스윙·스폰·피버·
        /// 기간제 효과의 시간이 모두 멈춘다"고 정했는데, 이 다섯이 전부 Time.deltaTime 으로
        /// 돌아가므로 timeScale 을 0 으로 두면 그대로 충족된다. 스윙은 클릭이 아니라 타이머
        /// 구동(HammerSwingController.Update)이라 정지 중에 입력이 새지 않는다.
        /// </summary>
        private void HandlePerkOffered(string[] perkIds)
        {
            if (perkIds == null || perkIds.Length == 0 || _balanceData == null)
            {
                return;
            }

            if (!TryPreparePanel())
            {
                return;
            }

            BindCards(perkIds);
            _panelInstance.SetActive(true);
            EnableFallbackEventSystemIfNeeded();
            PauseTime();
        }

        /// <summary>패널을 처음 쓸 때 한 번만 만든다. 실패하면 false — 그때는 정지도 하지 않는다.</summary>
        private bool TryPreparePanel()
        {
            if (_panelInstance != null)
            {
                return true;
            }
            if (_panelPrefab == null)
            {
                Debug.LogError("[PerkChoiceController] 패널 프리팹이 비어 있어 퍼크 선택을 띄우지 못했다.");
                return false;
            }

            // Managers 의 자식으로 둔다 — DontDestroyOnLoad 를 물려받아 씬이 바뀌어도 살아남는다.
            _panelInstance = Instantiate(_panelPrefab, transform);
            _panelInstance.name = _panelPrefab.name;
            _cards = _panelInstance.GetComponentsInChildren<PerkCardView>(true);
            _fallbackEventSystem = FindFallbackEventSystem(_panelInstance);
            _panelInstance.SetActive(false);

            if (_cards.Length == 0)
            {
                Debug.LogError("[PerkChoiceController] 패널에 PerkCardView 가 하나도 없다.");
            }
            return true;
        }

        private void BindCards(string[] perkIds)
        {
            for (var i = 0; i < _cards.Length; i++)
            {
                // 카드가 후보보다 많으면 남는 카드는 숨긴다 (Bind 가 null 을 그렇게 다룬다).
                var perk = i < perkIds.Length ? _balanceData.GetPerk(perkIds[i]) : null;
                _cards[i].Bind(perk, HandleCardChosen);
            }
        }

        /// <summary>
        /// **고르기 전에는 닫히지 않는다.** TryChoosePerk 가 true 를 줄 때만 닫는다 —
        /// 취소로 3장을 버릴 수 있으면 4.2 의 상태(OfferedPerkIds)가 갈라진다 (#92 완료 기준).
        /// 그래서 이 패널에는 닫기 버튼도 ESC 처리도 없다.
        /// </summary>
        private void HandleCardChosen(string perkId)
        {
            var billService = BillManager.Instance;
            if (billService == null || !billService.TryChoosePerk(perkId))
            {
                // 후보에 없는 id 였다. 패널을 연 채로 둔다 — 닫으면 고를 기회가 사라진다.
                Debug.LogWarning($"[PerkChoiceController] 퍼크 선택이 거부됐다: {perkId}");
                return;
            }

            HidePanel();
            ResumeTime();
        }

        private void HidePanel()
        {
            if (_fallbackEventSystem != null)
            {
                _fallbackEventSystem.SetActive(false);
            }
            if (_panelInstance != null)
            {
                _panelInstance.SetActive(false);
            }
        }

        private static GameObject FindFallbackEventSystem(GameObject panel)
        {
            var found = panel.GetComponentInChildren<EventSystem>(true);
            return found == null ? null : found.gameObject;
        }

        /// <summary>
        /// **Game 씬에는 EventSystem 이 없다.** 없으면 uGUI 가 클릭을 아예 처리하지 않아 카드를
        /// 눌러도 반응이 없다 — 화면만 뜨고 영영 못 고르니 게임이 멈춘 채로 남는다.
        /// 씬은 다른 담당의 것이라 고칠 수 없어, 패널이 자기 것을 들고 다닌다.
        ///
        /// **이미 있으면 켜지 않는다.** 활성 EventSystem 이 둘이면 Unity 가 경고를 내고 한쪽을
        /// 꺼 버린다 — MainMenu 씬에는 이미 하나 있다.
        /// </summary>
        private void EnableFallbackEventSystemIfNeeded()
        {
            if (_fallbackEventSystem == null || EventSystem.current != null)
            {
                return;
            }
            _fallbackEventSystem.SetActive(true);
        }

        private void PauseTime()
        {
            if (_isPaused)
            {
                return;
            }
            _timeScaleBeforePause = Time.timeScale;
            _isPaused = true;
            Time.timeScale = 0f;
        }

        private void ResumeTime()
        {
            if (!_isPaused)
            {
                return;
            }
            _isPaused = false;
            Time.timeScale = _timeScaleBeforePause;
        }
    }
}
