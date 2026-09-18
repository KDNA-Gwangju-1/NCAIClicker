using System.Collections;
using TMPro;
using UnityEngine;

namespace NCAIClicker.Targets
{
    /// <summary>
    /// 크리처 머리 위에 현재 체력(HP)을 숫자로 표시하고 피격 시 데미지 팝업을 연계한다.
    /// </summary>
    [RequireComponent(typeof(Target))]
    public class CreatureHpDisplay : MonoBehaviour
    {
        [SerializeField] private Vector3 _offset = new Vector3(0f, 0.55f, 0f);
        [SerializeField] private float _fontSize = 3.6f;

        private Target _target;
        private GameObject _hpTextObject;
        private TextMeshPro _hpTextMesh;
        private Camera _mainCamera;
        private Coroutine _punchScaleCoroutine;

        private void Awake()
        {
            _target = GetComponent<Target>();
            _mainCamera = Camera.main;
            CreateHpText();
        }

        private void OnEnable()
        {
            if (_target != null)
            {
                _target.HitReceived += HandleHitReceived;
            }
            UpdateHpDisplay();
        }

        private void OnDisable()
        {
            if (_target != null)
            {
                _target.HitReceived -= HandleHitReceived;
            }
        }

        private void CreateHpText()
        {
            _hpTextObject = new GameObject("HpText");
            _hpTextObject.transform.SetParent(transform, false);
            _hpTextObject.transform.localPosition = _offset;

            _hpTextMesh = _hpTextObject.AddComponent<TextMeshPro>();
            _hpTextMesh.alignment = TextAlignmentOptions.Center;
            _hpTextMesh.fontSize = _fontSize;
            _hpTextMesh.fontStyle = FontStyles.Bold;
            _hpTextMesh.color = Color.white;
            _hpTextMesh.sortingOrder = 90;

            // 검은색 외곽선으로 배경과 대비 확보
            _hpTextMesh.outlineWidth = 0.25f;
            _hpTextMesh.outlineColor = new Color32(0, 0, 0, 255);
        }

        public void UpdateHpDisplay()
        {
            if (_target == null || _hpTextMesh == null)
            {
                return;
            }

            int displayHp = Mathf.Max(0, Mathf.CeilToInt(_target.CurrentHp));
            _hpTextMesh.text = displayHp.ToString();

            // HP 잔여량에 따른 시각적 강조 (남은 피 1일 때 살짝 붉은빛 경고)
            if (displayHp <= 1)
            {
                _hpTextMesh.color = new Color(1f, 0.35f, 0.35f, 1f);
            }
            else
            {
                _hpTextMesh.color = Color.white;
            }
        }

        private void HandleHitReceived(Data.HitInfo hitInfo)
        {
            UpdateHpDisplay();

            // 피격 시 머리 위 텍스트 펀치 스케일 연출
            if (gameObject.activeInHierarchy)
            {
                if (_punchScaleCoroutine != null)
                {
                    StopCoroutine(_punchScaleCoroutine);
                }
                _punchScaleCoroutine = StartCoroutine(PunchScaleRoutine());
            }

            // 데미지 팝업 스폰
            Vector3 spawnPos = transform.position + Vector3.up * 0.5f;
            Vector3 randomOffset = new Vector3(
                Random.Range(-0.15f, 0.15f),
                Random.Range(0f, 0.1f),
                Random.Range(-0.15f, 0.15f)
            );
            DamagePopup.Create(spawnPos + randomOffset, hitInfo.Damage);
        }

        private IEnumerator PunchScaleRoutine()
        {
            if (_hpTextObject == null)
            {
                yield break;
            }

            Vector3 baseScale = Vector3.one;
            Vector3 punchScale = Vector3.one * 1.4f;
            float elapsed = 0f;
            float duration = 0.18f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                _hpTextObject.transform.localScale = Vector3.Lerp(punchScale, baseScale, t);
                yield return null;
            }

            _hpTextObject.transform.localScale = baseScale;
            _punchScaleCoroutine = null;
        }

        private void LateUpdate()
        {
            if (_hpTextObject == null)
            {
                return;
            }

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera != null)
            {
                _hpTextObject.transform.rotation = _mainCamera.transform.rotation;
            }
        }
    }
}
