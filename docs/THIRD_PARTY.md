# 서드파티 에셋 및 라이선스

외부에서 가져온 폰트·효과음·이미지·코드는 **가져온 PR에서 바로 이 표에 추가한다.** 나중에 몰아서 정리하면 출처를 잊는다.

| 이름 | 종류 | 출처 | 라이선스 | 라이선스 전문 | 용도 |
|---|---|---|---|---|---|
| 나눔고딕 Regular·Bold | 폰트 | https://github.com/google/fonts/tree/main/ofl/nanumgothic (네이버 배포판) | SIL OFL 1.1 | `LICENSES/NanumGothic-OFL.txt` | UI 본문 (`Assets/ThirdParty/Fonts/NanumGothic-*.ttf`) |
| 나눔스퀘어 Bold | 폰트 | https://hangeul.naver.com/fonts/search?f=nanum (nanum-square.zip) | SIL OFL 1.1 | `LICENSES/NanumSquare-OFL.txt` (배포 zip 에 전문이 없어 OFL 1.1 표준 전문 사본. 이대로 유지하기로 결정, #78) | HUD 숫자 강조 (`Assets/ThirdParty/Fonts/NanumSquareB.ttf`) |
| LiberationSans | 폰트 | Unity TMP 패키지 필수 리소스 (`Assets/TextMesh Pro/Fonts/`) | SIL OFL 1.1 | `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt` | TMP 기본 폴백 폰트. 직접 쓰지 않는다 |
| Impact Sounds (Kenney) | 효과음 | https://kenney.nl/assets/impact-sounds | CC0 1.0 | 고지 의무 없음 (CC0) | 타격음 (`Assets/Audio/HitImpact.ogg`) |
| Casino Audio (Kenney) | 효과음 | https://kenney.nl/assets/casino-audio | CC0 1.0 | 고지 의무 없음 (CC0) | 코인 획득음 (`Assets/Audio/CoinPickup.ogg`) |
| Interface Sounds (Kenney) | 효과음 | https://kenney.nl/assets/interface-sounds | CC0 1.0 | 고지 의무 없음 (CC0) | 피버 시작음·고지서 발행음·고지서 납부음 (`Assets/Audio/FeverStart.ogg`, `BillIssued.ogg`, `BillPaid.ogg`) |
| 저금통 일반형 3D 모델 | 3D 모델 | VARCO 3D (https://3d.varco.ai, NC AI 생성형 AI 도구) | 개인/비상업 라이선스 (일반 약관 기준). NC AI 교육 프로그램으로 제공된 팀 계정으로, 본 수업 팀 프로젝트 용도의 생성·사용은 허용됨. 공개 배포·상업적 이용 가능 여부는 별도 확인 필요 | https://terms.varco.ai (전문 미인용, 요약만 기록) | 저금통 일반형 시각 에셋 (`Assets/Models/PiggyNormal.glb`) |
| 저금통 파편 01·02 3D 모델 | 3D 모델 | VARCO 3D (https://3d.varco.ai, NC AI 생성형 AI 도구) | 위와 동일 (개인/비상업 라이선스, 팀 계정 교육 용도 허용) | https://terms.varco.ai (전문 미인용, 요약만 기록) | 저금통 파괴 파편 시각 에셋 (`Assets/Models/PiggyFragment01.glb`, `PiggyFragment02.glb`). 파편 03~08은 `PiggyNormal.glb` 메시를 Blender로 절차적으로 분할한 것으로 신규 생성물이 아니라 별도 항목 없음 |
| 광물 크리처 4종 3D 모델 (구리·은·금·다이아몬드) | 3D 모델 | VARCO 3D (https://3d.varco.ai, NC AI 생성형 AI 도구) | 위와 동일 (개인/비상업 라이선스, 팀 계정 교육 용도 허용) | https://terms.varco.ai (전문 미인용, 요약만 기록) | 타격 대상 4종 시각 에셋, 저금통 테마를 대체 (`Assets/Models/MineCreatureCopper.glb`, `MineCreatureSilver.glb`, `MineCreatureGold.glb`, `MineCreatureDiamond.glb`). 광산 작업장 컨셉아트에 맞춰 2026-09-22 재생성(#234) — 구 `MineralCreature*.glb` 세트를 대체, 자세한 경위는 [mineral-creature-assets.md](TECH_NOTES/mineral-creature-assets.md) 참고 |
| 광석 덩이 4종 3D 모델 (철·구리·은·금) | 3D 모델 | VARCO 3D (https://3d.varco.ai, NC AI 생성형 AI 도구) | 위와 동일 (개인/비상업 라이선스, 팀 계정 교육 용도 허용) | https://terms.varco.ai (전문 미인용, 요약만 기록) | `Coin.prefab`의 액면별 시각 에셋 (`Assets/Models/OreChunkIron.glb`, `OreChunkCopper.glb`, `OreChunkSilver.glb`, `OreChunkGold.glb`). 2026-09-22 생성(#235), 자세한 경위는 [coin-economy.md](TECH_NOTES/coin-economy.md) "시각 매핑" 절 참고 |
| 소형 소품 7종 3D 모델 (랜턴·곡괭이·삽·경고 표지판·나무 팻말·밧줄·양동이) | 3D 모델 | VARCO 3D (https://3d.varco.ai, NC AI 생성형 AI 도구) | 위와 동일 (개인/비상업 라이선스, 팀 계정 교육 용도 허용) | https://terms.varco.ai (전문 미인용, 요약만 기록) | 씬 가장자리 소품 시각 에셋 (`Assets/Models/Lantern.glb`, `Pickaxe.glb`, `Shovel.glb`, `WarningSign.glb`, `Signboard.glb`, `RopeCoil.glb`, `Bucket.glb`). 2026-09-22 생성(#238) — 양동이는 사용자가 VARCO 3D에서 직접 수정(손잡이 제거)한 버전을 별도로 받아 사용. 자세한 경위는 [mine-props.md](TECH_NOTES/mine-props.md) 참고 |

## 기록 규칙

- **에셋스토어 무료 에셋도 반드시 기록한다.** 무료와 "고지 불필요"는 다르다.
- SIL OFL, CC BY 계열은 **라이선스 전문 또는 저작자 표기를 배포물에 포함해야 한다.** `LICENSES/` 폴더에 원문을 그대로 넣고 빌드와 함께 배포한다.
- CC0 / Public Domain 은 고지 의무가 없지만, 나중에 출처를 추적할 수 있도록 표에는 남긴다.
- 라이선스를 확인할 수 없는 에셋은 쓰지 않는다. "검색해서 나온 이미지"는 전부 여기에 해당한다.
