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
    /// 런타임 소비처는 **ManagerBootstrap 과 GameManager 둘뿐이다** — 복원은 앱 시작 1회라
    /// 조립 지점(ManagerBootstrap)이 맡고, 저장 시점과 새 회차 초기화는 초기화·종료 순서를
    /// 조정하는 GameManager 가 맡는다 (ARCHITECTURE 1절). 에디터 검증 하네스는 예외다.
    ///
    /// ISaveService 와 나눠 둔 이유는 소비처가 다르기 때문이다. 메인 메뉴는 `HasSave` 만 쓰는데
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
    }
}
