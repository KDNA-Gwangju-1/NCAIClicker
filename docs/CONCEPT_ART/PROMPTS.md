# VARCO 이미지 프롬프트 대장

> 이 문서의 프롬프트를 **번호로 참조**한다. 이슈·코멘트에 프롬프트 본문을 복사하지 않는다 —
> 여기서 고치면 모든 참조가 같이 고쳐진다. 새 프롬프트는 끝에 번호를 이어 붙이고, 기존 번호는 바꾸지 않는다.
>
> 모델: 별도 표기 없으면 **`gpt-image-2.5-flare`**, `aspectRatio 1:1`, `count 1`.
> 크리처 변형(P-30~)은 `nano-banana-2` 로 만든 것도 있다 — 표기해 뒀다. 새로 만들 때는 gpt-2.5 를 쓴다.

## 0. 공통 규칙

- 이미지 한 장 = 20 크레딧. 돌리기 전에 프롬프트를 이 문서에 먼저 적는다.
- **소품·광석은 P-01 템플릿을 그대로** 쓴다. 이미 뽑힌 21장이 이 템플릿으로 나왔고 스타일이 맞으니 손대지 않는다.
- **크리처는 P-20 → P-2x 체인**을 거친 결과다. 다시 만들 일이 있으면 체인의 마지막 단계(v6)를 source 로 P-30 계열 색 변형만 돌린다. 실루엣을 바꾸려면 구리부터 다시 체인을 타고, 나머지 셋을 재생성한다.
- source 이미지 URL 은 [README.md](README.md) 표에서 가져온다.

## 1. 씬

### P-00 씬 컨셉 (GenerateImage, 16:9, reference 3장: 게임플레이 스크린샷 · 크리처 레퍼런스 · 원작 스크린샷)

```
Create a polished stylized 3D game concept screenshot for NCAIClicker. Preserve the fixed high-angle perspective, camera framing, and large open central playable rectangle from the gameplay reference. Transform the bare brown platform into an underground fantasy mine worksite with a readable packed-earth floor, cave walls around the perimeter, a dark tunnel entrance, timber supports, short mine rails, a small ore cart, lanterns, crates, ore sacks, rope, bucket, shovel, spare pickaxes, warning signs, and a few glowing crystal clusters. Keep most props around the screen edges so the center stays clear for gameplay. The creatures must follow the character reference: cute soft pale-blue round-bodied four-legged creatures, tiny simple mouth, large warm golden oval eyes, pink cheek blush, short rounded legs. Simplify their back minerals into only 3 to 5 low, chunky, rounded crystal or ore lobes plus at most one small smooth spiral fossil ornament. Make them soft, friendly, toy-like, and easy to read at gameplay scale. No sharp spike forest, no dense crystal crown, no exposed eyeballs, no monster face, no teeth, no horror anatomy. Use copper, gold, silver, and amethyst variants while keeping the same simple creature silhouette. Scatter small collectible ore chunks on the ground with distinct values: dark iron, orange copper, bright silver, and rich gold, with visual density similar to the coins in the original-game reference. Show a player hand holding a mining pickax
```

## 2. 에셋 분리 템플릿 (EditImage, source = P-00 결과)

### P-01 단일 에셋 분리 — 공통 템플릿

`{ASSET}` 자리에 아래 표의 설명을 넣는다. 나머지 문장은 바꾸지 않는다.

```
Extract only the {ASSET} from this scene as a single isolated game asset. Center it on a plain flat light gray background, three-quarter front view, whole object fully visible, nothing else in frame: no other props, no UI, no text, no ground shadow. Keep the exact stylized painterly look, colors and materials of the scene. This image will be used as input for image-to-3D generation.
```

| 번호 | 파일 | `{ASSET}` |
|---|---|---|
| P-01a | OreCart | `ORE CART (wooden mine cart on small iron wheels, loaded with dark ore)` |
| P-01b | Lantern | `HANGING LANTERN (brass mine lantern with warm glowing flame and a hook)` |
| P-01c | WoodenCrate | `WOODEN CRATE (plank crate with iron corner brackets)` |
| P-01d | OreSack | `ORE SACK (burlap sack tied with rope, open top showing ore and gold nuggets)` |
| P-01e | Barrel | `WOODEN BARREL (plank barrel with iron hoops)` |
| P-01f | Pickaxe | `MINING PICKAXE (wooden handle with leather grip, dark iron double-pointed head)` — 뒤에 `, without the hand` 추가, `nothing else in frame:` 뒤에 `no hand,` 추가 |
| P-01g | Shovel | `SHOVEL (wooden handle, dark iron blade)` |
| P-01h | WarningSign | `YELLOW WARNING SIGN (diamond-shaped yellow sign with black exclamation mark on a wooden post)` — `no text` 를 `no extra text` 로 |
| P-01i | Signboard | `WOODEN SIGNBOARD with crossed pickaxe-and-hammer emblem (plank sign on a wooden post)` — `no text` 를 `no extra text` 로 |
| P-01j | AmethystCluster | `one GLOWING AMETHYST CRYSTAL CLUSTER (several chunky purple crystal shards growing from a small rock base, softly glowing)` |
| P-01k | RopeCoil | `COILED ROPE (thick hemp rope coiled in a loose ring)` — `three-quarter front view` 를 `three-quarter top-down view` 로 |
| P-01l | Bucket | `WOODEN BUCKET (plank bucket with iron hoops and a handle, filled with water)` |
| P-01m | FenceSection | `one straight section of the WOODEN RAIL FENCE (two horizontal log rails on three vertical posts, about 3 posts wide)` — `game asset` 을 `modular game asset` 으로 |
| P-01n | TunnelEntrance | `MINE TUNNEL ENTRANCE (timber support frame: two thick vertical wooden posts with a horizontal beam and cross brace, plus a short length of iron mine rails on wooden sleepers running through it, dark opening inside)` — `whole object fully visible,` 뒤에 `no cave rock around it,` 추가 |

### P-01o~p 컨테이너 소품 4종 재보정 (#237)

작업 6.24(#237)에서 광차·나무상자 image-to-3D 결과에 반복적으로 결함이 나와 P-01 템플릿을 벗어난
전용 프롬프트로 다시 만들었다. 자루는 P-01d 원본 이미지에 엠블럼이 있었을 뿐이라 `EditImage` 로만
고쳤다. 반복 경위는 [mine-props.md](../TECH_NOTES/mine-props.md) "반복 보정 경위" 절 참고.

| 번호 | 파일 | 방식 | 내용 |
|---|---|---|---|
| P-01o | OreCart (최종) | `GenerateImage` (P-01 형식 이탈, 처음부터) | `ORE CART (wooden mine cart on small iron wheels, loaded with dark ore chunks)` + 배경·구도 지시는 P-01 템플릿과 동일 + "all four wheels must be identical and consistently oriented — same size, same spoke/hub pattern, same rotation angle, all facing the same forward direction and aligned on the same two axle lines. No mismatched, tilted, or randomly rotated wheels." |
| P-01p | WoodenCrate (최종) | `GenerateImage` (P-01 형식 이탈, 처음부터) | `WOODEN ORE CRATE WITH A HINGED LID (rugged mine storage crate used for hauling ore, thick weathered wood planks with dark grimy dirt-stained grain, sturdy rusty dark-iron corner plates, one rusty iron strap band running across the top of the lid, a couple of small dark ore chunks resting on top of the lid)` + "keep the overall shape a simple, perfectly rectangular cuboid box with flat sides" + "exactly 4 evenly spaced horizontal wood planks ... ONE clean continuous straight seam line ... unbroken from edge to edge" + "LID ... visibly THICK, like a solid slab about as tall as one wood plank" + "exactly TWO hinges: both hinges must be IDENTICAL" |
| — | OreSack (최종) | `EditImage` (source = P-01d 원본) | `Remove the crossed pickaxe emblem/logo printed on the front of the burlap sack. Keep the sack plain burlap texture with no symbol, no marking, no design on the front — same sack shape, same rope tie, same ore/gold nuggets on top, same lighting and background.` |

### P-02 광석 덩어리 분리 — 템플릿

P-01 과 같되 `Center it` → `Center it large` (작은 물체라 크게), `three-quarter front view` → `three-quarter view`.

| 번호 | 파일 | `{ASSET}` |
|---|---|---|
| P-02a | OreChunkIron | `one small IRON ORE CHUNK (dark gray-black faceted rock nugget, the lowest-value collectible on the ground)` |
| P-02b | OreChunkCopper | `one small COPPER ORE CHUNK (orange-red faceted rock nugget, a collectible on the ground)` |
| P-02c | OreChunkSilver | `one small SILVER ORE CHUNK (bright pale silver faceted rock nugget, a collectible on the ground)` |
| P-02d | OreChunkGold | `one small GOLD ORE CHUNK (rich shiny yellow-gold faceted nugget, the highest-value collectible on the ground)` |

## 3. 크리처 — 구리 원형 체인

씬의 크리처를 바로 3D 에 넣지 않았다. 아래 순서로 5번 고쳐 **v6 를 원형**으로 확정했다.
각 단계는 **직전 단계 결과를 source** 로 넣는다 (씬이 아니라).

### P-20 씬에서 구리 크리처 분리 + 단순화 (source = P-00, gpt-2.5)

```
Extract only the COPPER creature from this scene as a single isolated game character, and SIMPLIFY it: cute soft pale-blue round-bodied four-legged creature, large warm golden oval eyes, tiny simple mouth, pink cheek blush, short rounded legs. On its back put only 3 low, chunky, rounded, smooth copper-orange ore lobes (like soft pebbles, not crystals) and one small smooth spiral shell. No sharp spikes, no crystal crown, no dense detail, no fine facets. Clean toy-like silhouette that reads at small size. Center it on a plain flat light gray background, three-quarter front view, whole body fully visible, standing on all four legs, nothing else in frame: no number, no UI, no text, no ground shadow. Keep the stylized painterly look and colors of the scene. This image will be used as input for image-to-3D generation.
```

> gpt-2.5 가 이 프롬프트에서 간헐적으로 태스크 실패를 냈다 (3회). 실패하면 그냥 다시 돌린다.

### P-21 광석 덩어리 확대 (source = P-20 결과, nano-banana-2)

```
Keep this creature exactly as it is (body, face, eyes, legs, colors, pose, plain light gray background) but make the copper ore lobes on its back MUCH BIGGER: each lobe about the size of the creature's head, 3 to 4 lobes total, so together they cover the whole back from the head to the rump and look heavy and voluminous. Keep the small spiral shell. All lobes stay smooth rounded pebble shapes with fully rounded edges: no sharp spikes, no pointed crystals, no facets, no added detail. Same painterly style. Nothing else in frame, no text, no shadow.
```

### P-22 광맥을 위로 솟게 (source = P-20 결과, nano-banana-2)

```
Keep this creature exactly as it is (body, face, eyes, legs, colors, pose, plain light gray background) but change the copper ore on its back: make the lobes big AND tall, rising upward from the back like a rounded crest or mound about as tall as the creature's body height, 3 to 4 chunky lobes stacked and leaning upward, with subtle low facets so it still reads as ore. Keep the small spiral shell at the side. Tops are rounded domes, not points: no sharp spikes, no needle crystals, no thin edges. Same painterly style. Nothing else in frame, no text, no shadow.
```

### P-23 껍질 베이스 추가 (source = P-22 결과, reference = 거북 껍질 참고 이미지 1장, nano-banana-2)

```
Keep this creature exactly as it is (body, face, eyes, legs, pale-blue color, pose, plain light gray background, and the tall copper ore lobes and spiral shell on its back). ADD a smooth rounded base shell that covers its back like the turtle-style shell in the reference image: a single domed carapace wrapping over the back from behind the head to the rump, with a soft scalloped rim, in a muted dusty copper-brown tone slightly darker than the ore. The existing tall copper ore lobes and the spiral shell now sit ON TOP of this shell, growing up out of it. Keep everything rounded: no sharp spikes, no pointed crystals. Same painterly style. Nothing else in frame, no text, no shadow.
```

### P-24 이마 중앙 덩어리 추가 (source = P-23 결과, nano-banana-2)

```
Keep this creature exactly as it is (body, face, eyes, legs, colors, pose, background, shell base, and all existing copper ore lobes and the spiral shell). Only ADD one more copper ore lobe in the empty spot at the center of the forehead, between the two front lobes, growing forward out of the shell rim just above the eyes, same rounded chunky style, size and color as the other lobes, so the crest is continuous across the front with no gap. Keep everything rounded: no sharp spikes. Same painterly style. Nothing else in frame, no text, no shadow.
```

### P-25 가운데 확대 + 나선 껍질 제거 → **v6 원형 확정** (source = P-24 결과, nano-banana-2)

```
Keep this creature exactly as it is (body, face, eyes, legs, colors, pose, background, shell base, and the copper ore lobes) with two changes only: 1) make the CENTER forehead ore lobe noticeably BIGGER and taller, the largest lobe of the crest, rising up between the two side lobes; 2) REMOVE the spiral snail-shell ornament on the side completely and fill that spot with plain shell surface matching the surrounding carapace. Keep everything rounded: no sharp spikes. Same painterly style. Nothing else in frame, no text, no shadow.
```

결과 = `CreatureCopper` (README 표 URL). **이후 시도(v7~v9: 이마 광석 확대 변형)는 폐기했다.**

## 4. 크리처 — 색 변형 (source = P-25 결과 v6)

공통 골격:

```
Keep this creature exactly as it is: same body, face, eyes, legs, pale-blue skin, pose, background, shell shape and every ore lobe in the same size and position. Change ONLY the material colors: {MATERIAL}. Same painterly style. Nothing else in frame, no text, no shadow.
```

| 번호 | 파일 | 모델 | `{MATERIAL}` |
|---|---|---|---|
| P-30 | CreatureSilver | nano-banana-2 | `the ore lobes become bright polished SILVER metal ore (pale silver-gray with soft white highlights and light gray facets), and the shell base becomes a muted cool gray-slate tone slightly darker than the ore` |
| P-31 | CreatureGold | nano-banana-2 | `the ore lobes become rich shiny GOLD ore (warm yellow-gold with bright highlights and amber facets), and the shell base becomes a muted dark golden-brown tone slightly darker than the ore` |
| P-32 | (폐기) Amethyst | nano-banana-2 | `the ore lobes become AMETHYST (vivid violet-purple with soft lavender highlights and low facets, slightly translucent glow), and the shell base becomes a muted dusky purple-gray tone slightly darker than the ore` — 4번째 종을 다이아몬드로 확정하며 폐기 |
| P-33 | (폐기) Diamond 1차 | nano-banana-2 | `... clear icy white with pale cyan and light-blue tints ...` — **얼음처럼 나와서 폐기.** 다이아몬드에 blue/icy 를 쓰지 않는다 |
| P-34 | CreatureDiamond | **gpt-2.5** | `the ore lobes become cut DIAMOND gemstones — brilliant clear white, NOT blue and NOT icy: crisp geometric facet planes with sharp bright specular highlights, tiny rainbow prismatic sparkles (pink, yellow, green flashes) inside, and a faint warm-white glow. Facets are flat and clean like a jeweler's cut but the overall lobe silhouettes stay the same rounded shapes. The shell base becomes a muted warm silver-gray (champagne platinum) tone` |
| P-35 | CreatureIron (#282) | nano-banana-2 | `the ore lobes become raw black IRON ore (matte dark charcoal-black rock with subtle blue-gray metallic glints on the low facets, almost no rust, at most a faint trace of warm brown in a crevice), and the shell base becomes a muted dark slate-gray tone slightly lighter than the ore so the lobes still read against it` — 1회 생성으로 채택. 로브와 껍질을 같은 검정으로 두면 로브가 묻히므로 껍질을 한 톤 **밝게** 지정했다 |

## 5. 배운 것

- **"simplify" 만으로는 부족하다.** 크기·높이·위치를 한 번에 한 가지씩 지시하는 편이 안정적이다 (P-21~P-25 가 그 예).
- **"center" 는 모호하다.** 위쪽 큰 덩어리로 해석됐다. 위치를 말할 때는 `on the FOREHEAD, directly above the eyes` 처럼 기준점을 준다.
- **"grow bigger" 는 옆으로도 퍼진다.** 위로만 키우려면 `width and footprint stay exactly the same` + `must NOT extend past the shell rim` 을 같이 쓴다.
- 색 변형은 `Change ONLY the material colors` 로 실루엣이 잘 유지된다. 4종 일관성은 이 방법이 가장 좋다.
