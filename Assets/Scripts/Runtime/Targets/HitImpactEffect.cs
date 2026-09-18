using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

namespace NCAIClicker.Targets
{
    /// <summary>
    /// 타격 지점에 짧게 튀는 스파크·크랙 버스트. 런당 300회 타격이 발생하므로 풀에서 재사용한다
    /// (docs/PATTERNS.md 7절, 이슈 #35).
    /// </summary>
    public class HitImpactEffect : MonoBehaviour
    {
        private const float Duration = 0.18f;
        private const int MaxPoolSize = 32;
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private static ObjectPool<HitImpactEffect> _pool;

        private static ObjectPool<HitImpactEffect> Pool =>
            _pool ??= new ObjectPool<HitImpactEffect>(Create, OnGet, OnRelease, OnDestroyPooled, maxSize: MaxPoolSize);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneCleanup()
        {
            ClearPool();
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ClearPool();
        }

        /// <summary>풀의 정적 참조를 비워 씬 전환 시 파괴된 오브젝트 접근을 방지한다 (이슈 #197).</summary>
        public static void ClearPool()
        {
            if (_pool != null)
            {
                _pool.Clear();
                _pool = null;
            }
        }

        private MeshRenderer _renderer;
        private Material _material;
        private Camera _mainCamera;
        private float _elapsed;

        /// <summary>풀에서 이펙트를 꺼내 타격 지점에 재생한다.</summary>
        public static void Spawn(Vector3 worldPosition)
        {
            HitImpactEffect fx = null;
            try
            {
                fx = Pool.Get();
            }
            catch (MissingReferenceException)
            {
                ClearPool();
                fx = Pool.Get();
            }

            if (fx == null || fx.gameObject == null)
            {
                ClearPool();
                fx = Pool.Get();
            }

            fx.transform.position = worldPosition;
            fx.ResetState();
        }

        private void ResetState()
        {
            _elapsed = 0f;
            transform.localScale = Vector3.one * 0.35f;

            if (_material == null)
            {
                return;
            }
            var c = _material.GetColor(_baseColorId);
            c.a = 1f;
            _material.SetColor(_baseColorId, c);
        }

        private static HitImpactEffect Create()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError("[HitImpactEffect] 'Universal Render Pipeline/Unlit' 셰이더를 찾지 못했다. " +
                               "Project Settings > Graphics > Always Included Shaders 를 확인하라.");
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "HitImpactEffect (Pooled)";
            Destroy(go.GetComponent<Collider>());

            var fx = go.AddComponent<HitImpactEffect>();
            fx._renderer = go.GetComponent<MeshRenderer>();
            fx._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fx._renderer.receiveShadows = false;

            if (shader != null)
            {
                var mat = new Material(shader);
                mat.SetFloat("_Surface", 1);
                mat.SetFloat("_Blend", 0);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                mat.mainTexture = GenerateBurstTexture();
                mat.SetColor(_baseColorId, new Color(1f, 0.95f, 0.75f, 1f));
                fx._renderer.material = mat;
                fx._material = mat;
            }

            fx._mainCamera = Camera.main;
            return fx;
        }

        private static void OnGet(HitImpactEffect fx) => fx.gameObject.SetActive(true);

        private static void OnRelease(HitImpactEffect fx) => fx.gameObject.SetActive(false);

        private static void OnDestroyPooled(HitImpactEffect fx)
        {
            if (fx == null)
            {
                return;
            }
            Destroy(fx.gameObject);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            var progress = Mathf.Clamp01(_elapsed / Duration);

            transform.localScale = Vector3.one * Mathf.Lerp(0.35f, 0.9f, progress);

            if (_material != null)
            {
                var c = _material.GetColor(_baseColorId);
                c.a = Mathf.Lerp(1f, 0f, progress);
                _material.SetColor(_baseColorId, c);
            }

            if (_elapsed >= Duration)
            {
                Pool.Release(this);
            }
        }

        private void LateUpdate()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera != null)
            {
                transform.rotation = _mainCamera.transform.rotation;
            }
        }

        /// <summary>중심에서 방사형으로 뻗는 균열·스파크 모양의 단순 텍스처를 만든다.</summary>
        private static Texture2D GenerateBurstTexture()
        {
            const int size = 64;
            const int rayCount = 6;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var center = size * 0.5f;
            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy) / center;
                    var angle = Mathf.Atan2(dy, dx);

                    var col = Color.clear;

                    if (dist < 0.18f)
                    {
                        col = new Color(1f, 1f, 1f, 1f - dist / 0.18f);
                    }

                    var raySharpness = Mathf.Abs(Mathf.Repeat(angle / (Mathf.PI * 2f / rayCount), 1f) - 0.5f) * 2f;
                    if (dist < 0.9f && raySharpness > 0.82f)
                    {
                        var rayAlpha = (1f - dist) * ((raySharpness - 0.82f) / 0.18f);
                        col = Color.Lerp(col, new Color(1f, 0.9f, 0.6f, 1f), Mathf.Clamp01(rayAlpha));
                    }

                    pixels[y * size + x] = col;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
