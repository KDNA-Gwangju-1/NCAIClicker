# 서드파티 에셋 및 라이선스

외부에서 가져온 폰트·효과음·이미지·코드는 **가져온 PR에서 바로 이 표에 추가한다.** 나중에 몰아서 정리하면 출처를 잊는다.

| 이름 | 종류 | 출처 | 라이선스 | 라이선스 전문 | 용도 |
|---|---|---|---|---|---|
| 나눔고딕 Regular·Bold | 폰트 | https://github.com/google/fonts/tree/main/ofl/nanumgothic (네이버 배포판) | SIL OFL 1.1 | `LICENSES/NanumGothic-OFL.txt` | UI 본문 (`Assets/ThirdParty/Fonts/NanumGothic-*.ttf`) |
| 나눔스퀘어 Bold | 폰트 | https://hangeul.naver.com/fonts/search?f=nanum (nanum-square.zip) | SIL OFL 1.1 | `LICENSES/NanumSquare-OFL.txt` (배포 zip 에 전문이 없어 OFL 1.1 표준 전문 사본. 이대로 유지하기로 결정, #78) | HUD 숫자 강조 (`Assets/ThirdParty/Fonts/NanumSquareB.ttf`) |
| LiberationSans | 폰트 | Unity TMP 패키지 필수 리소스 (`Assets/TextMesh Pro/Fonts/`) | SIL OFL 1.1 | `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt` | TMP 기본 폴백 폰트. 직접 쓰지 않는다 |

## 기록 규칙

- **에셋스토어 무료 에셋도 반드시 기록한다.** 무료와 "고지 불필요"는 다르다.
- SIL OFL, CC BY 계열은 **라이선스 전문 또는 저작자 표기를 배포물에 포함해야 한다.** `LICENSES/` 폴더에 원문을 그대로 넣고 빌드와 함께 배포한다.
- CC0 / Public Domain 은 고지 의무가 없지만, 나중에 출처를 추적할 수 있도록 표에는 남긴다.
- 라이선스를 확인할 수 없는 에셋은 쓰지 않는다. "검색해서 나온 이미지"는 전부 여기에 해당한다.
