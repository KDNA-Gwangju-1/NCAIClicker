using TMPro;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

namespace NCAIClicker.Targets
{
    /// <summary>
    /// 타격 시 피해량을 표시하고 공중에 떠오르며 사라지는 팝업 연출.
    /// 런당 300회 타격이 발생하므로 풀에서 재사용한다 (docs/PATTERNS.md 7절, 이슈 #35).
    /// </summary>
    public class DamagePopup : MonoBehaviour
    {
        private const int MaxPoolSize = 32;

        private static ObjectPool<DamagePopup> _pool;

        private static ObjectPool<DamagePopup> Pool =>
            _pool ??= new ObjectPool<DamagePopup>(CreatePooled, OnGet, OnRelease, OnDestroyPooled, maxSize: MaxPoolSize);

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

        private TextMeshPro _textMesh;
        private Camera _mainCamera;
        private float _elapsedTime;
        private float _duration = 0.65f;
        private Vector3 _floatVelocity;
        private Color _baseColor;

        /// <summary>풀에서 팝업을 꺼내 표시한다.</summary>
        public static DamagePopup Spawn(Vector3 worldPosition, float damage)
        {
            DamagePopup popup = null;
            try
            {
                popup = Pool.Get();
            }
            catch (MissingReferenceException)
            {
                ClearPool();
                popup = Pool.Get();
            }

            if (popup == null || popup.gameObject == null)
            {
                ClearPool();
                popup = Pool.Get();
            }

            popup.transform.position = worldPosition;
            popup.Setup(damage);
            return popup;
        }

        /// <summary>팝업을 즉시 풀로 되돌린다. 자연 소멸(Update)을 기다리지 않고 정리해야 할 때 쓴다.</summary>
        public void Despawn()
        {
            Pool.Release(this);
        }

        private static DamagePopup CreatePooled()
        {
            var popupObj = new GameObject("DamagePopup (Pooled)");
            return popupObj.AddComponent<DamagePopup>();
        }

        private static void OnGet(DamagePopup popup) => popup.gameObject.SetActive(true);

        private static void OnRelease(DamagePopup popup) => popup.gameObject.SetActive(false);

        private static void OnDestroyPooled(DamagePopup popup)
        {
            if (popup == null)
            {
                return;
            }
            Destroy(popup.gameObject);
        }

        private void Awake()
        {
            _mainCamera = Camera.main;
            _textMesh = gameObject.AddComponent<TextMeshPro>();
            _textMesh.alignment = TextAlignmentOptions.Center;
            _textMesh.fontSize = 4.2f;
            _textMesh.fontStyle = FontStyles.Bold;
            _textMesh.sortingOrder = 100;

            // 주황빛 노란색으로 설정하여 가독성 확보
            _baseColor = new Color(1f, 0.88f, 0.2f, 1f);
            _textMesh.color = _baseColor;

            // 텍스트 외곽선 검은색 설정
            _textMesh.outlineWidth = 0.25f;
            _textMesh.outlineColor = new Color32(0, 0, 0, 255);
        }

        public void Setup(float damage)
        {
            if (_textMesh == null)
            {
                _textMesh = GetComponent<TextMeshPro>();
                if (_textMesh == null)
                {
                    _textMesh = gameObject.AddComponent<TextMeshPro>();
                }
            }

            // 정수면 소수점 없이, 소수점 있으면 첫째자리까지 표시
            bool isInteger = Mathf.Approximately(damage, Mathf.Round(damage));
            _textMesh.text = isInteger ? Mathf.RoundToInt(damage).ToString() : damage.ToString("0.0");

            // 풀에서 재사용될 때 이전 페이드아웃 알파가 남지 않도록 되돌린다.
            _textMesh.color = _baseColor;

            // 위쪽과 살짝 바깥쪽으로 떠오르는 속도
            float randomX = Random.Range(-0.25f, 0.25f);
            _floatVelocity = new Vector3(randomX, 1.3f, 0f);
            _elapsedTime = 0f;
            transform.localScale = Vector3.one * 0.7f;
        }

        private void Update()
        {
            _elapsedTime += Time.deltaTime;
            float progress = Mathf.Clamp01(_elapsedTime / _duration);

            // 위로 이동
            transform.position += _floatVelocity * Time.deltaTime;
            // 서서히 감속
            _floatVelocity.y = Mathf.Max(0.2f, _floatVelocity.y - Time.deltaTime * 1.5f);

            // 스케일 펀치 효과 (초반에 커졌다가 정착)
            if (progress < 0.25f)
            {
                float t = progress / 0.25f;
                transform.localScale = Vector3.Lerp(Vector3.one * 0.7f, Vector3.one * 1.25f, t);
            }
            else
            {
                float t = (progress - 0.25f) / 0.75f;
                transform.localScale = Vector3.Lerp(Vector3.one * 1.25f, Vector3.one, t);
            }

            // 후반부 페이드 아웃
            if (progress > 0.45f)
            {
                float fadeT = (progress - 0.45f) / 0.55f;
                Color c = _baseColor;
                c.a = Mathf.Lerp(1f, 0f, fadeT);
                _textMesh.color = c;
            }

            if (_elapsedTime >= _duration)
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
    }
}
