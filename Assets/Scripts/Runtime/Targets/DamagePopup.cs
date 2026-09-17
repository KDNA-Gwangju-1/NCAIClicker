using TMPro;
using UnityEngine;

namespace NCAIClicker.Targets
{
    /// <summary>
    /// 타격 시 피해량을 표시하고 공중에 떠오르며 사라지는 팝업 연출.
    /// </summary>
    public class DamagePopup : MonoBehaviour
    {
        private TextMeshPro _textMesh;
        private Camera _mainCamera;
        private float _elapsedTime;
        private float _duration = 0.65f;
        private Vector3 _floatVelocity;
        private Color _baseColor;

        public static DamagePopup Create(Vector3 worldPosition, float damage)
        {
            var popupObj = new GameObject("DamagePopup");
            popupObj.transform.position = worldPosition;
            var popup = popupObj.AddComponent<DamagePopup>();
            popup.Setup(damage);
            return popup;
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
                Destroy(gameObject);
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
