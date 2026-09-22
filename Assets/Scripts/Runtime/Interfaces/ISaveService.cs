using NCAIClicker.Data;

namespace NCAIClicker.Interfaces
{
    /// <summary>
    /// 저장 및 로드 계약
    /// </summary>
    public interface ISaveService
    {
        SaveData Load();
        void Save(SaveData data);

        /// <summary>저장 파일이 실제로 존재하는지 여부. Load()는 없어도 항상 기본값을 반환해 구분이 안 된다 (이슈 #139).</summary>
        bool HasSave { get; }
    }

    /// <summary>
    /// 매니저 상태를 저장에 모으고 되돌리는 계약 (이슈 #203).
    /// 런타임 소비처는 **ManagerBootstrap·GameManager·SettingsPanelController 셋이다** —
    /// 복원은 앱 시작 1회라 조립 지점(ManagerBootstrap)이 맡고, 저장 시점과 새 회차 초기화는
    /// 초기화·종료 순서를 조정하는 GameManager 가 맡으며(ARCHITECTURE 1절), 저장 초기화는
    /// 설정 패널(#196)이 부른다 — 성장을 지우는 두 경로가 같은 메서드를 써야 하기 때문이다.
    /// 에디터 검증 하네스는 예외다.
    ///
    /// ISaveService 와 나눠 둔 이유는 소비처가 다르기 때문이다. 메인 메뉴는 `HasSave` 와 표시용 `Load()` 만 쓰는데
    /// 한 계약으로 묶으면 그쪽에서도 수집·분배가 보인다.
    ///
    /// **IRunScoped 로 대신할 수 없다.** 복원은 다른 매니저의 BeginRun 보다 먼저, 저장은 모든
    /// EndRun 보다 나중이어야 하는데, GameManager 는 두 경계에 같은 순서 배열을 쓴다 —
    /// 한 순번으로 양쪽 끝을 잡을 수 없다.
    /// </summary>
    public interface IGamePersistence
    {
        /// <summary>지금 매니저들이 들고 있는 값을 모아 저장한다.</summary>
        void CollectAndSave();

        /// <summary>저장을 읽어 매니저들에 되돌린다.</summary>
        void LoadAndDistribute();

        /// <summary>
        /// 성장을 지운다 — 빈 저장을 쓰고 **곧바로 분배한다** (이슈 #203).
        ///
        /// **파일만 비우면 안 된다.** 매니저는 DontDestroyOnLoad 라 업그레이드 레벨·레거시
        /// 포인트·반지가 메모리에 그대로 남고, 다음 저장이 그 값을 파일에 도로 써서 초기화가
        /// 없던 일이 된다. 실제로 그렇게 됐다 (#196 저장 초기화).
        ///
        /// **설정(볼륨·창모드·화면 흔들림)은 성장이 아니므로 남긴다.** 넘겨받지 않고 현재
        /// 저장 파일에서 옮겨 담으므로, **부르기 전에 설정을 파일에 반영해 두어야 한다** —
        /// 설정을 들고 있는 것은 SaveManager 가 아니라 부르는 쪽이다.
        /// </summary>
        void ResetAndDistribute();
    }
}
