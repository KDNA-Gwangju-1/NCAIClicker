# 배포 전 세팅 체크리스트

작업 8.3 최종 빌드 직전에 몰아서 하면 반드시 사고가 난다. **오늘 미리 잡을 수 있는 것**과 **빌드 직전에만 가능한 것**을 나눠 적는다.

체크박스가 비어 있으면 아직 안 된 것이다. 완료하면 이 문서에서 체크한다.

---

## 1. 폰트 — 오늘 확정

한글 게임에서 폰트는 "예쁜 걸 고르는" 문제가 아니라 **라이선스 문제**다. 무료로 보이는 폰트 중에도 게임 내장(임베딩)을 금지하는 것이 많고, 배포 후에 걸리면 빌드를 다시 만들어야 한다.

### 결정: 본문 나눔고딕, 숫자 강조는 나눔스퀘어 Bold

- **나눔고딕** — SIL Open Font License 1.1. 임베딩·상업적 이용·재배포 모두 허용. 네이버 배포판.
- 대안으로 **Pretendard**(OFL 1.1)도 안전하며 숫자 가독성이 더 좋다. 둘 중 무엇을 쓰든 라이선스 조건은 같다.
- **쓰면 안 되는 것**: 맑은 고딕(Windows 번들, 임베딩 불가), 애플 산돌고딕네오, 상용 폰트의 "개인 무료" 버전.

> OFL의 유일한 의무는 **라이선스 전문을 함께 배포**하는 것이다. 폰트 파일만 넣고 라이선스를 빼면 위반이다. 4절에서 처리한다.

### 체크리스트

- [ ] 폰트 파일을 `Assets/Fonts/` 에 배치 (`.ttf`/`.otf`)
- [ ] TextMeshPro Font Asset 생성 — **Atlas Population Mode 를 `Dynamic` 으로**
  - 한글은 완성형만 11,172자다. Static 아틀라스로 전부 구우면 텍스처가 수십 MB가 되고 빌드가 무거워진다. Dynamic은 실제로 쓰인 글자만 런타임에 채운다.
  - 단, Dynamic은 첫 등장 시 아틀라스를 갱신하므로 **인게임 중 처음 뜨는 문구에서 한 프레임 튈 수 있다.** HUD에 쓰는 고정 문구("스태미나", "코인", 숫자 0~9)는 Static 아틀라스로 미리 구워두고, 나머지를 Dynamic으로 두는 혼합이 가장 안전하다.
- [ ] 숫자 글립이 **고정폭(tabular)** 인지 확인 — `34/120` 처럼 매 프레임 바뀌는 숫자가 폭이 들쭉날쭉하면 HUD가 떨린다
- [ ] TMP Settings 의 Default Font Asset 을 교체 (안 하면 새로 만든 텍스트가 영문 기본 폰트로 뜬다)

---

## 2. Git 설정 — 오늘, 팀원 각자 1회

`.gitattributes` 에 씬·프리팹 스마트 병합을 걸어뒀지만, **병합 도구 경로는 각자 로컬에 등록해야** 작동한다. 등록하지 않으면 조용히 일반 텍스트 병합으로 떨어져서 씬이 깨진다.

Windows 기준 (Unity 6000.3):

```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver '"C:/Program Files/Unity/Hub/Editor/6000.3.21f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p %O %B %A %A'
```

- [ ] 팀원 5명 전원 위 명령 실행 (에디터 설치 경로는 각자 확인)
- [ ] 씬 충돌을 일부러 하나 만들어 병합이 실제로 되는지 1회 검증

---

## 3. Player Settings — 오늘 잡고 작업 8.2 에서 재확인

- [x] Product Name = `NCAIClicker`, Company Name = `KDNA-Gwangju-1` 설정 완료
  - 저장 경로가 `%USERPROFILE%/AppData/LocalLow/KDNA-Gwangju-1/NCAIClicker/` 로 정해졌다. **바꾸려면 세이브가 생기기 전인 지금뿐이다** — 저장 기능이 붙은 뒤에 바꾸면 기존 세이브를 못 읽는다
- [x] 게임명 확정 — `NCAIClicker`
- [ ] 아이콘 (Default Icon)
- [ ] 해상도: Fullscreen Mode = `Windowed` 또는 `Fullscreen Window`, 기본 해상도 1920×1080
- [ ] Resizable Window 허용 여부 결정 — 16:9 고정이라면 꺼두는 편이 HUD 깨짐을 막는다
- [ ] Scripting Backend: Mono (기본). IL2CPP는 빌드가 훨씬 오래 걸려 7일 일정에 불리하다. 성능 문제가 실측으로 확인되기 전에는 바꾸지 않는다
- [ ] Unity Personal 라이선스면 스플래시 화면은 끌 수 없다 — 시연 영상 길이 계산에 포함할 것

### URP 렌더러

- [x] 3D 확정이므로 URP 기본 렌더러를 그대로 쓴다. 2D Renderer 는 도입하지 않는다
- [ ] 쓰지 않는 Mobile 렌더러 에셋(`Assets/Settings/Mobile_*`) 정리 여부 결정 (PC 전용 빌드라 불필요)

### 씬

- [x] 씬 2개 생성 및 빌드 순서 등록: `MainMenu`(0) → `Game`(1)
- [x] URP 템플릿 잔여물 제거 (`Assets/TutorialInfo`, `Assets/Readme.asset`)
- [ ] 빌드에서 매니저 자동 생성이 실제로 되는지 확인 (작업 1.2.3 이후)

---

## 4. 서드파티 라이선스 고지 — 오늘

폰트, 효과음, 스프라이트 중 외부에서 가져온 것은 전부 출처와 라이선스를 기록한다. **에셋스토어 무료 에셋도 예외가 아니다.**

- [ ] `LICENSES/` 폴더에 각 라이선스 전문 파일 배치 (예: `LICENSES/NanumGothic-OFL.txt`)
- [ ] `docs/THIRD_PARTY.md` 에 표로 정리: 이름 / 출처 URL / 라이선스 / 용도
- [ ] 빌드 산출물에 `LICENSES/` 폴더를 함께 동봉하거나, 게임 내 크레딧 화면에 표기
- [ ] 효과음·이미지를 추가할 때마다 **그 PR에서 같이** 기록 (나중에 몰아서 하면 출처를 잊는다)

---

## 5. 저장 데이터 — Day 3 전까지

- [ ] 저장 경로는 `Application.persistentDataPath` 사용 (`Application.dataPath` 는 설치 폴더라 쓰기 권한이 없을 수 있다)
- [ ] `SaveData.Version` 필드로 마이그레이션 경로 확보 ([ARCHITECTURE.md](ARCHITECTURE.md) 2절)
- [ ] 세이브가 깨졌을 때 크래시 대신 초기화되고 경고를 남기는지 확인
- [ ] 빌드에서 실제로 저장·불러오기가 되는지 검증 — 에디터에서만 되는 경우가 흔하다

---

## 6. 빌드 — Day 6 배포 후보, Day 7 최종

- [ ] Development Build **해제** 확인 (켜진 채로 배포하면 프로파일러 오버헤드가 남는다)
- [ ] 빌드 폴더는 `.gitignore` 에 이미 제외됨 — 저장소에 올리지 말 것
- [ ] 콘솔 에러 0건 상태에서 빌드
- [ ] 빌드한 exe를 **다른 PC에서 1회 실행** — 내 PC에만 있는 파일에 의존하는 실수를 여기서 잡는다
- [ ] 압축 후 배포 파일명에 버전 표기 (`NCAIClicker_v1.0.zip`)

---

## 7. 최종 검증 (Day 7)

[EXECUTION_PLAN.md](EXECUTION_PLAN.md) 완료 조건을 빌드에서 그대로 재현한다.

- [ ] 런 시작 → 정산 → 재도전이 빌드에서 반복 가능
- [ ] 마감일 미납 시 파산으로 회차가 끝나고 결과 화면이 구분되어 표시됨
- [ ] 대출을 쓰면 상환 전까지 매일 수입이 징수되는 것이 빌드에서 확인됨
- [ ] 게임 재실행 후 성장 데이터 유지
- [ ] 1920×1080 외 해상도(1280×720, 2560×1440)에서 HUD 안 깨짐
- [ ] 치명적 런타임 오류 없음
