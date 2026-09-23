# 프로파일링 측정 방법

성능 예산과 측정 시점은 [ARCHITECTURE.md](ARCHITECTURE.md) 5절이 정본이다. 이 문서는 **어떻게 재는가**만 적는다.

## 무엇을 재나

| 값 | 어디서 | 예산 |
|---|---|---|
| Batches (드로우콜) | Game 창 Stats / Profiler Rendering | 200 이하 |
| Triangles | 같은 곳 | 15만 이하 |
| Shadow Casters | 같은 곳 | 타격 대상만 |
| 프레임 시간 | Stats / Profiler CPU | 16.6ms 이하 (60fps) |
| GC Alloc | Profiler CPU·Memory | 코인·이펙트 폭주 때 스파이크가 없어야 함 |

**Game 씬 런 중**에 잰다. MainMenu 값은 의미가 없다. 로드 직후 첫 프레임의 시간은 버린다.

## 방법 A — 에디터 Play (빠름, 참고값)

1. `MainMenu` 씬에서 Play → 이어하기 또는 새 게임으로 런에 들어간다
2. Game 창 오른쪽 위 **Stats** 를 켠다
3. 저금통이 많고 이펙트가 겹칠 때의 Batches·Tris·Shadow casters 를 적는다

- **Game 창이 화면에 보이고 포커스가 있어야 한다.** 가려져 있으면 렌더링이 멈춰 모든 값이 0 으로 나온다
- 에디터 값은 에디터 오버헤드가 섞여 프레임 시간이 빌드보다 나쁘다. 드로우콜·삼각형 수는 빌드와 거의 같다

### 에이전트가 MCP 로 잴 때

`execute_code` 로 Stats 창과 같은 값을 읽는다.

```csharp
// Play 중, Game 창을 먼저 연다 (가려져 있으면 0)
UnityEditor.EditorWindow.GetWindow(System.Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
return "batches=" + UnityEditor.UnityStats.batches + " tris=" + UnityEditor.UnityStats.triangles
     + " shadowCasters=" + UnityEditor.UnityStats.shadowCasters + " frameMs=" + UnityEditor.UnityStats.frameTime * 1000f;
```

GC 는 `manage_profiler get_counters` (category `Memory`, `GC Allocated In Frame`).

- **새 게임(`StartNewRun`)은 저장 파일을 지운다.** 측정 전에
  `%USERPROFILE%\AppData\LocalLow\NCAITeamTwo\NCAIClicker\save.json` 을 복사해 두고 끝나면 되돌린다
- 이어하기는 저장이 "납부 후 흐름 진행 중"이면 런을 시작하지 않는다. 그때는 위 백업 후 새 게임으로 잰다

## 방법 B — Development 빌드 + Profiler (정식값)

7.4 처럼 판정에 쓰는 값은 이것으로 잰다.

1. `File > Build Profiles` 에서 **Development Build** 를 켜고 빌드한다 (출력은 `Builds/` — git 에 올라가지 않는다)
2. 빌드 exe 를 실행하고 런에 들어간다
3. Unity 에서 `Window > Analysis > Profiler` (Ctrl+7) → 위쪽 **Play Mode** 드롭다운에서 실행 중인 플레이어를 고른다
4. Rendering 모듈에서 Batches·SetPass·Triangles, CPU 모듈에서 GC Alloc 스파이크를 본다

빌드하면 Unity 가 `ProjectSettings`·URP 설정·폰트 SDF 를 다시 쓰는 경우가 있다. **커밋하지 말고 되돌린다.**

## 결과를 남기는 곳

해당 프로파일링 이슈의 코멘트에 표로 남긴다. 예산을 넘은 항목은 원인과 함께 별도 이슈로 세운다.
