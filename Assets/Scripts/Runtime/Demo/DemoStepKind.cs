#if UNITY_EDITOR
namespace NCAIClicker.Demo
{
    /// <summary>시연 시나리오 한 단계의 종류. 녹화용 에디터 전용 (#45).</summary>
    public enum DemoStepKind
    {
        /// <summary>라벨·이름이 일치하는 버튼이 나타나면 이동해 클릭한다. 시간 안에 없으면 건너뛴다.</summary>
        ClickButton,

        /// <summary>일치하는 버튼 중 n번째(왼쪽→오른쪽)를 클릭한다. 퍼크 카드처럼 같은 버튼이 여럿일 때 쓴다.</summary>
        ClickIndex,

        /// <summary>가장 가까운 크리처를 쫓아 커서를 올려 둔다. 라벨의 버튼이 나타나면 일찍 끝난다.</summary>
        HuntCreatures,

        /// <summary>라벨의 버튼이 나타날 때까지 기다린다.</summary>
        WaitUntilButton,

        /// <summary>정해진 시간만큼 가만히 있는다.</summary>
        Wait,

        /// <summary>화면 비율 좌표(0~1)로 이동한다.</summary>
        MoveTo,

        /// <summary>
        /// 자막 한 장을 자막 파일(.ass)에 기록한다. 기다리지 않고 다음 단계로 넘어간다.
        /// 라벨이 문구, 초가 표시 시간. 순번이 1이면 직전 WaitForEvent 가 성공했을 때만 기록한다.
        /// </summary>
        Caption,

        /// <summary>라벨의 게임 이벤트(FeverStart 등)가 이 단계 시작 뒤 일어날 때까지 기다린다.</summary>
        WaitForEvent,

        /// <summary>라벨 경로의 에디터 메뉴를 실행한다 (예: NCAI/디버그/단계 +1).</summary>
        MenuItem,

        /// <summary>뒤에서 계속 크리처를 쫓기 시작한다. 버튼 클릭·MoveTo 단계나 StopHunting 에서 멈춘다.</summary>
        StartHunting,

        /// <summary>뒤에서 쫓던 것을 멈춘다.</summary>
        StopHunting,
    }
}
#endif
