# 컨셉 아트 — VARCO 3D 입력용 레퍼런스

> 여기 있는 이미지는 **우리가 VARCO 3D 이미지 노드로 생성한 것**이다. 원작 프레스킷 같은 타인 저작물은
> 여기 넣지 않는다 — 그건 `research/reference/` (gitignore) 에 둔다. 이용 조건은 [THIRD_PARTY.md](../THIRD_PARTY.md).

## MineWorksite/ — 지하 광산 작업장 테마

`SceneConcept_MineWorksite.jpg` 가 씬 전체 컨셉이고, 나머지 22장은 그 씬에서 에셋별로 분리한
**image-to-3D 입력 이미지**다 (1:1, 회색 무배경, 3/4 뷰). `ContactSheet.jpg` 로 한눈에 본다.

저장소에는 JPEG(q92)로 넣었다. **Generate3D 에 넣을 때는 아래 VARCO 원본(PNG 1024²) URL 을 그대로 쓴다** —
`ImageInput` 노드의 `value` 에 URL 을 넣으면 재업로드 없이 바로 쓸 수 있다.

### 원본 URL (VARCO 3D 오브젝트 스토어)

| 파일 | 대상 | 원본 URL |
|---|---|---|
| `CreatureCopper` | `TargetRunner` Visual | https://3d.varco.ai/api/objects/8a1a61e3bcc826b969a20c826a7c1bc2.png |
| `CreatureSilver` | `TargetNormal` Visual | https://3d.varco.ai/api/objects/25cb563314407b2ecd82a75e90c70f1d.png |
| `CreatureGold` | `TargetTourist` Visual | https://3d.varco.ai/api/objects/2549ae040906d4e05afda3e41d39ddb8.png |
| `CreatureDiamond` | `TargetAnchor` Visual | https://3d.varco.ai/api/objects/4d12b2c6c6e7ad90d1d89ddefb0da8cb.png |
| `OreChunkIron` | 코인(획득물) 액면 1 | https://3d.varco.ai/api/objects/a16419c5d76867ed468040fd47055977.png |
| `OreChunkCopper` | 코인 액면 2 | https://3d.varco.ai/api/objects/f86c725abbe3a0eefd59593621d671b0.png |
| `OreChunkSilver` | 코인 액면 3 | https://3d.varco.ai/api/objects/96230014542ffc5605d3e2d2930e3b52.png |
| `OreChunkGold` | 코인 액면 4 | https://3d.varco.ai/api/objects/2fe9549fe9d8f8ce5a82c2f9220fa3b1.png |
| `TunnelEntrance` | 환경 — 갱도 입구 (지지대+레일) | https://3d.varco.ai/api/objects/f9ecb0ded00e4fbfd6b864eb97009ed6.png |
| `FenceSection` | 환경 — 목책 1칸 (모듈) | https://3d.varco.ai/api/objects/288989f6984494909bb73c8209db5f67.png |
| `AmethystCluster` | 환경 — 자수정 군락 | https://3d.varco.ai/api/objects/50cc277bc1c94f89762b9668f6197a6d.png |
| `OreCart` | 소품 — 광차 | https://3d.varco.ai/api/objects/21d3127c5f5a3fae8501586dcc70dcf3.png |
| `WoodenCrate` | 소품 — 나무상자 | https://3d.varco.ai/api/objects/b364981d2a2c937e25914a3a7c736c49.png |
| `OreSack` | 소품 — 광석 자루 | https://3d.varco.ai/api/objects/906ab95a1b4ef9bc64f8e003daab4b6c.png |
| `Barrel` | 소품 — 나무통 | https://3d.varco.ai/api/objects/f07d66cf4b28228650318357efd60745.png |
| `Lantern` | 소품 — 랜턴 | https://3d.varco.ai/api/objects/fa427f0e5b3f5ed38721b1aec1a5ca8c.png |
| `Pickaxe` | 소품 — 곡괭이 (플레이어 도구 후보) | https://3d.varco.ai/api/objects/6b590259b48e44ebc0de74d8de493131.png |
| `Shovel` | 소품 — 삽 | https://3d.varco.ai/api/objects/d0fe82e2036c98907fef349bbc6f3077.png |
| `WarningSign` | 소품 — 경고 표지판 | https://3d.varco.ai/api/objects/01311ddaee46c35ca86335bf3a2d6493.png |
| `Signboard` | 소품 — 나무 팻말 | https://3d.varco.ai/api/objects/80e8a99356bf9f1fa86d025629af77af.png |
| `RopeCoil` | 소품 — 밧줄 | https://3d.varco.ai/api/objects/ff61f33374f0e4d9b321b38b5d3c5bdd.png |
| `Bucket` | 소품 — 양동이 | https://3d.varco.ai/api/objects/48769d2c26935b191412f23f94030d3b.png |
| `SceneConcept_MineWorksite` | 씬 전체 컨셉 (16:9) | https://3d.varco.ai/api/objects/5ba1a257f7deae4f79f8ba3399326f07.png |

### Generate3D 노드 설정 (팀 공통)

[ASSET_PIPELINE.md](../ASSET_PIPELINE.md) 2절 킥오프 확정값을 그대로 쓴다. 바꾸지 않는다.

| 파라미터 | 값 | 이유 |
|---|---|---|
| `polygonCount` | **1500** (소형 소품은 1000) | 타격 대상 1,000~2,000 삼각형 기준 |
| `topology` | `tri` | |
| `useAlpha` | `0` | 회색 배경이라 배경 제거를 VARCO 에 맡긴다 |
| `usePbrTexture` | `1` | 기존 크리처와 동일 |
| `textureSize` | `1024` | 실측 결과 그대로 채택 |
| `tPose` | `0` | 리깅 안 함 |
| 내보내기 | `.glb`, **pivotToBottom = true** | glTFast 임포트, 바닥 피벗이면 `localPosition.y` 계산이 단순해진다 |

1개당 **200 크레딧, 약 4분** (길면 10분+). 결과 `.glb` 는 `Assets/Models/` 에 PascalCase 로 넣고,
`Visual` 프리팹 분리 → 루트 프리팹 `Visual` 자식 교체 순서는
[mineral-creature-assets.md](../TECH_NOTES/mineral-creature-assets.md) "구조" 절과 동일하다.

### 이미지가 마음에 안 들면

VARCO 워크플로에서 해당 `EditImage` 노드의 `referenceText` 만 바꿔 재실행한다 (20 크레딧).
새 이미지를 여기 넣을 때는 **같은 파일명으로 덮어쓰고** 위 표의 URL 을 갱신한다.
크리처는 4종 실루엣이 같아야 하므로 **한 종만 따로 바꾸지 않는다** — 구리를 고치고 나머지 셋을 색 변형으로 다시 뽑는다.
