using NCAIClicker.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 망치의 예상 타격 위치 바닥 마커와 허공의 망치 스윙 비주얼을 제공한다.
    /// 바닥 원형 마커의 12개 세그먼트가 12시 방향부터 시계방향으로 차오르며 스윙 쿨타임을 표시한다.
    /// 게이지가 100% 완충되면 망치가 바닥을 강하게 내려찍는다.
    /// </summary>
    public class HammerSwingVisual : MonoBehaviour
    {
        private const int SegmentCount = 12;

        /// <summary>
        /// 베이스 텍스처에서 금색 실선 링이 그려진 위치. 쿼드 절반 크기를 1.0 으로 본 비율이다
        /// (GenerateBaseReticleTexture 의 ringDist 기준). 링이 판정 반경과 겹치도록 쿼드를 이 비율로 역산한다.
        /// </summary>
        private const float RingTextureRadiusRatio = 0.74f;

        /// <summary>쿨타임 세그먼트를 판정 링 바로 바깥에 두는 비율. 링과 겹쳐 읽히지 않게 띄운다.</summary>
        private const float SegmentRingRatio = 1.12f;

        private Camera _aimCamera;
        private Plane _deskPlane;
        private Transform _reticleRoot;
        private Transform _hammerPivot;
        private Renderer[] _segmentRenderers;
        private Transform _baseQuadTransform;

        /// <summary>
        /// 조준 반경 출처. 판정 범위 확대 퍼크(hit_radius_boost)로 HitRadius 가 바뀌면 Update 에서
        /// 매 프레임 비교해 레티클을 다시 그린다 (팀장 승인, #188) — BuildVisuals 는 시작할 때
        /// 한 번만 크기를 굳히므로 그것만으로는 퍼크로 인한 변화가 반영되지 않는다.
        /// </summary>
        private HammerSwingController _controller;

        /// <summary>
        /// 조준 판정 반경. 출처는 HammerSwingController 하나뿐이다 — 여기서 따로 정하면
        /// "보이는 원 밖인데 맞는다"가 다시 생긴다 (이슈 #109, #132).
        /// </summary>
        private float _hitRadius;

        /// <summary>
        /// 쿨타임 게이지 한 바퀴에 걸리는 시간. 컨트롤러가 CSV 에서 읽은 스윙 주기를 그대로 받는다
        /// (AGENTS.md 데이터 규칙 — 같은 숫자를 코드와 CSV 양쪽에 두지 않는다).
        /// </summary>
        private float _swingInterval;

        private float _timer;
        private bool _isVisible;

        public bool IsVisible => _isVisible;

        private void Awake()
        {
            _aimCamera = Camera.main;
            _deskPlane = new Plane(Vector3.up, Vector3.zero);
        }

        private void Start()
        {
            // 반경과 스윙 주기는 컨트롤러가 CSV 에서 읽어 둔 값을 그대로 쓴다. 컨트롤러의 Awake 가
            // 먼저 끝나야 하므로 Awake 가 아니라 Start 에서 만든다.
            var controller = FindFirstObjectByType<HammerSwingController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[HammerSwingVisual] HammerSwingController 가 없어 조준 반경과 스윙 주기를 알 수 없다. 레티클을 그리지 않는다.");
            }
            else
            {
                _controller = controller;
                _hitRadius = controller.HitRadius;
                _swingInterval = controller.SwingIntervalSec;
            }

            BuildVisuals();
            SetVisible(_isVisible);
        }

        /// <summary>
        /// 셰이더를 이름으로 찾는다. 없으면 조용히 다른 셰이더로 갈아타지 않고 알린다 (#151).
        ///
        /// Shader.Find 는 **빌드에 포함된** 셰이더만 찾는다. 어느 에셋도 참조하지 않는 셰이더는
        /// 빌드에서 스트립되므로, 런타임에만 쓰는 셰이더는 Graphics 설정의 Always Included
        /// Shaders 에 등록해 두어야 한다. 빌트인 셰이더로 폴백하던 코드가 있었는데, 그것들은
        /// URP 패스가 없어 마젠타로 렌더된다 — 고장을 감추는 쪽이 더 나쁘다.
        /// </summary>
        private static Shader FindRequiredShader(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[HammerSwingVisual] 셰이더 '{shaderName}' 를 찾지 못했다. " +
                               "Project Settings > Graphics > Always Included Shaders 에 등록돼 있는지 확인한다.");
            }
            return shader;
        }

        private void BuildVisuals()
        {
            var transparentShader = FindRequiredShader("Universal Render Pipeline/Unlit");
            if (transparentShader == null)
            {
                return;
            }

            // 1. 레티클 루트
            var reticleRootGo = new GameObject("ReticleRoot");
            reticleRootGo.transform.SetParent(transform);
            _reticleRoot = reticleRootGo.transform;

            if (_hitRadius <= 0f)
            {
                // 반경을 모르면 판정과 어긋난 원을 그리느니 아무것도 그리지 않는다.
                BuildHammer();
                return;
            }

            // 2. 바닥 기본 베이스 링 (중심점 + 내부 원형 음영 및 실선 테두리)
            //    금색 실선 링이 판정 반경과 겹치도록 쿼드 크기를 역산한다.
            var baseQuadScale = _hitRadius * 2f / RingTextureRadiusRatio;
            var baseQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            baseQuad.name = "BaseReticle";
            baseQuad.transform.SetParent(_reticleRoot);
            baseQuad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            baseQuad.transform.localScale = new Vector3(baseQuadScale, baseQuadScale, 1f);
            baseQuad.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            _baseQuadTransform = baseQuad.transform;
            Destroy(baseQuad.GetComponent<Collider>());

            var baseRenderer = baseQuad.GetComponent<Renderer>();
            baseRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            baseRenderer.receiveShadows = false;

            var baseMat = CreateTransparentMaterial(transparentShader);
            baseMat.mainTexture = GenerateBaseReticleTexture();
            baseRenderer.material = baseMat;

            // 3. 바깥쪽 12개 쿨타임 세그먼트 게이지 생성 (12시 방향부터 시계방향)
            _segmentRenderers = new Renderer[SegmentCount];
            for (var i = 0; i < SegmentCount; i++)
            {
                var segGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                segGo.name = "Segment_" + i;
                segGo.transform.SetParent(_reticleRoot);
                Destroy(segGo.GetComponent<Collider>());

                // 12시 방향(0도)부터 시계방향 배치
                var angleDeg = (i / (float)SegmentCount) * 360f;
                var angleRad = (90f - angleDeg) * Mathf.Deg2Rad; // 12시 기준 시계방향
                var segmentRingRadius = _hitRadius * SegmentRingRatio;
                var posX = Mathf.Cos(angleRad) * segmentRingRadius;
                var posZ = Mathf.Sin(angleRad) * segmentRingRadius;

                segGo.transform.localPosition = new Vector3(posX, 0.016f, posZ);
                segGo.transform.localRotation = Quaternion.Euler(90f, angleDeg, 0f);
                segGo.transform.localScale = new Vector3(0.12f, 0.04f, 1f);

                var segRenderer = segGo.GetComponent<Renderer>();
                segRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                segRenderer.receiveShadows = false;

                var segMat = CreateTransparentMaterial(transparentShader);
                segRenderer.material = segMat;
                _segmentRenderers[i] = segRenderer;
            }

            BuildHammer();
        }

        /// <summary>허공의 스윙 망치. 조준 반경과 무관하므로 레티클을 못 그릴 때도 만든다.</summary>
        private void BuildHammer()
        {
            // 모델·배색·치수는 HammerRig 가 단일 출처다. 자동 망치(AutoHammerVisual)도 같은 것을 쓴다.
            _hammerPivot = HammerRig.Build(transform, "HammerPivot");
            SetVisible(_isVisible);
        }

        private static Material CreateTransparentMaterial(Shader shader)
        {
            var mat = new Material(shader);
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            return mat;
        }

        private static Texture2D GenerateBaseReticleTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var center = size * 0.5f;
            var maxR = size * 0.5f;
            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    var col = Color.clear;

                    // 중심 하얀 점 (조준점)
                    if (dist < 0.08f)
                    {
                        var t = 1f - (dist / 0.08f);
                        col = new Color(1f, 1f, 1f, Mathf.Clamp01(t * 1.5f));
                    }
                    // 내부 어두운 그림자 영역
                    else if (dist < 0.72f)
                    {
                        var shadowAlpha = Mathf.Lerp(0.35f, 0.08f, dist / 0.72f);
                        col = new Color(0.08f, 0.04f, 0.02f, shadowAlpha);
                    }

                    // 내부 실선 링 (갈색 금색 테두리)
                    var ringDist = Mathf.Abs(dist - 0.74f);
                    if (ringDist < 0.035f)
                    {
                        var a = 1f - (ringDist / 0.035f);
                        col = Color.Lerp(col, new Color(0.88f, 0.7f, 0.48f, 0.95f), a);
                    }

                    pixels[y * size + x] = col;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// 레티클과 망치 비주얼의 가시성을 설정한다.
        /// </summary>
        public void SetVisible(bool isVisible)
        {
            _isVisible = isVisible;
            if (_reticleRoot != null)
            {
                _reticleRoot.gameObject.SetActive(isVisible);
            }
            if (_hammerPivot != null)
            {
                _hammerPivot.gameObject.SetActive(isVisible);
            }
        }

        /// <summary>
        /// 판정 범위 확대 퍼크(hit_radius_boost)로 조준 반경이 바뀌면 레티클도 함께 다시 그려야
        /// 체감이 된다 (팀장 승인, #188). BuildVisuals 는 시작할 때 한 번만 크기를 굳히므로,
        /// 반경이 바뀔 때마다 Update 가 이 메서드를 불러 베이스 링과 세그먼트 위치를 갱신한다.
        /// </summary>
        private void RebuildReticleScale()
        {
            if (_baseQuadTransform != null)
            {
                var baseQuadScale = _hitRadius * 2f / RingTextureRadiusRatio;
                _baseQuadTransform.localScale = new Vector3(baseQuadScale, baseQuadScale, 1f);
            }

            if (_segmentRenderers == null)
            {
                return;
            }

            var segmentRingRadius = _hitRadius * SegmentRingRatio;
            for (var i = 0; i < SegmentCount; i++)
            {
                var r = _segmentRenderers[i];
                if (r == null)
                {
                    continue;
                }

                var angleDeg = (i / (float)SegmentCount) * 360f;
                var angleRad = (90f - angleDeg) * Mathf.Deg2Rad;
                var posX = Mathf.Cos(angleRad) * segmentRingRadius;
                var posZ = Mathf.Sin(angleRad) * segmentRingRadius;
                var segTransform = r.transform;
                var pos = segTransform.localPosition;
                segTransform.localPosition = new Vector3(posX, pos.y, posZ);
            }
        }

        private void Update()
        {
            if (_controller != null)
            {
                var currentRadius = _controller.HitRadius;
                if (!Mathf.Approximately(currentRadius, _hitRadius))
                {
                    _hitRadius = currentRadius;
                    RebuildReticleScale();
                }
            }

            if (!_isVisible)
            {
                return;
            }

            if (_aimCamera == null)
            {
                _aimCamera = Camera.main;
                if (_aimCamera == null)
                {
                    return;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                var screenPos = mouse.position.ReadValue();
                var ray = _aimCamera.ScreenPointToRay(screenPos);
                if (_deskPlane.Raycast(ray, out var enter))
                {
                    var hitPoint = ray.GetPoint(enter);
                    UpdateCursorTransform(hitPoint);
                }
            }

            // 쿨타임 타이머 갱신. 주기는 economy.csv 의 hover_swing_interval_sec 다.
            if (_swingInterval <= 0f)
            {
                return;
            }

            _timer += Time.deltaTime;
            while (_timer >= _swingInterval)
            {
                _timer -= _swingInterval;
            }

            var progress = Mathf.Clamp01(_timer / _swingInterval);

            // 1. 바닥 원형 12개 세그먼트 게이지 갱신 (12시 방향부터 시계방향)
            UpdateSegmentGauge(progress);

            // 2. 망치 스윙 모션 연동 (게이지 차오를 때 장전, 100% 도달 시 강타)
            AnimateHammerSwing(progress);
        }

        private void UpdateSegmentGauge(float progress)
        {
            if (_segmentRenderers == null)
            {
                return;
            }

            // progress 비율에 따라 켜질 세그먼트 수 계산
            var filledCount = Mathf.FloorToInt(progress * SegmentCount);

            for (var i = 0; i < SegmentCount; i++)
            {
                var r = _segmentRenderers[i];
                if (r == null)
                {
                    continue;
                }

                if (i <= filledCount)
                {
                    // 차오른 세그먼트: 밝은 아이보리 화이트 점등
                    r.material.color = new Color(0.98f, 0.95f, 0.9f, 0.95f);
                }
                else
                {
                    // 아직 안 찬 세그먼트: 어두운 반투명 색상 대기
                    r.material.color = new Color(0.25f, 0.18f, 0.12f, 0.22f);
                }
            }
        }

        private void UpdateCursorTransform(Vector3 groundPos)
        {
            if (_reticleRoot != null)
            {
                _reticleRoot.position = new Vector3(groundPos.x, 0.015f, groundPos.z);
            }

            if (_hammerPivot != null)
            {
                _hammerPivot.position = new Vector3(groundPos.x, 0.1f, groundPos.z);
            }
        }

        private void AnimateHammerSwing(float progress)
        {
            if (_hammerPivot == null)
            {
                return;
            }

            // 장전·강타·반동 구간 계산은 HammerRig 가 단일 출처다 — 자동 망치와 같은 박자를 쓴다.
            HammerRig.EvaluateSwing(progress, out var angleX, out var heightY);

            _hammerPivot.localRotation = Quaternion.Euler(angleX, -15f, 0f);
            var pos = _hammerPivot.position;
            pos.y = heightY;
            _hammerPivot.position = pos;
        }
    }
}
