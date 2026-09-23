using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NCAIClicker.UI
{
    /// <summary>
    /// 크리처 3D 외형을 UI 칸에 띄운다 (#247 결과 화면 "다음 저금통 해금").
    /// 타격 대상 프리팹의 Visual 자식만 복제해 화면 밖 먼 곳에 두고, 전용 카메라로 RenderTexture 에 찍어
    /// RawImage 에 보여 준다. 로직(Target·콜라이더)은 복제하지 않는다.
    /// 전용 레이어를 쓰지 않는 대신(Project Settings 변경 금지) 주 카메라가 보지 못할 만큼 멀리 둔다.
    /// </summary>
    public class CreaturePreview : MonoBehaviour
    {
        [System.Serializable]
        private struct PreviewEntry
        {
            [SerializeField] private string _targetId;
            [SerializeField] private GameObject _prefab;

            public string TargetId => _targetId;
            public GameObject Prefab => _prefab;
        }

        [SerializeField] private RawImage _image;

        [Tooltip("target_id → 타격 대상 프리팹. Managers 프리팹의 목록과 같아야 한다 (UnlockChecks 가 대조한다)")]
        [SerializeField] private List<PreviewEntry> _prefabs = new List<PreviewEntry>();

        [SerializeField] private Vector3 _stageOrigin = new Vector3(0f, -500f, 0f);
        [SerializeField] private int _textureSize = 256;
        [SerializeField] private float _turnSpeedDegPerSec = 40f;

        [Tooltip("모델 높이(0.8유닛) 기준 카메라 거리와 높이")]
        [SerializeField] private Vector3 _cameraOffset = new Vector3(0f, 0.55f, -1.9f);

        private Camera _camera;
        private RenderTexture _texture;
        private GameObject _model;
        private string _shownId;

        public string ShownTargetId => _shownId;

        /// <summary>targetId 의 외형을 띄운다. 없거나 null 이면 칸을 비운다.</summary>
        public void Show(string targetId)
        {
            if (targetId == _shownId && _model != null)
            {
                return;
            }

            Clear();
            var prefab = FindPrefab(targetId);
            var visual = prefab != null ? prefab.transform.Find("Visual") : null;
            if (visual == null)
            {
                SetImageVisible(false);
                return;
            }

            EnsureCamera();
            MatchTextureToImage();
            _model = Instantiate(visual.gameObject, _stageOrigin, Quaternion.Euler(0f, 180f, 0f));
            _model.name = "CreaturePreviewModel";
            _shownId = targetId;
            SetImageVisible(true);
            _camera.enabled = true;
        }

        public void Clear()
        {
            if (_model != null)
            {
                Destroy(_model);
            }
            _model = null;
            _shownId = null;
            if (_camera != null)
            {
                _camera.enabled = false;
            }
            SetImageVisible(false);
        }

        private void Update()
        {
            if (_model != null)
            {
                // 패널이 막 켜진 프레임에는 레이아웃이 아직 안 잡혀 칸 크기가 0 일 수 있다. 크기가 같으면 바로 빠진다.
                MatchTextureToImage();
                _model.transform.Rotate(0f, _turnSpeedDegPerSec * Time.unscaledDeltaTime, 0f, Space.World);
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnDestroy()
        {
            if (_camera != null)
            {
                Destroy(_camera.gameObject);
            }
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }

        private void EnsureCamera()
        {
            if (_camera != null)
            {
                return;
            }

            _texture = new RenderTexture(_textureSize, _textureSize, 16, RenderTextureFormat.ARGB32);
            var cameraGo = new GameObject("CreaturePreviewCamera");
            cameraGo.transform.position = _stageOrigin + _cameraOffset;
            cameraGo.transform.LookAt(_stageOrigin + new Vector3(0f, 0.4f, 0f));
            _camera = cameraGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.fieldOfView = 30f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 10f;
            _camera.targetTexture = _texture;
            _camera.enabled = false;
            if (_image != null)
            {
                _image.texture = _texture;
            }
        }

        /// <summary>
        /// 텍스처 비율을 칸 비율에 맞춘다. 정사각형 텍스처를 가로로 긴 칸에 붙이면 모델이 옆으로 늘어난다.
        /// 세로 해상도를 _textureSize 로 고정하고 가로를 비율만큼 둔다 — 카메라 시야(세로 기준)가 그대로라
        /// 모델은 칸 높이에 맞고 옆은 여백이 된다.
        /// </summary>
        private void MatchTextureToImage()
        {
            if (_image == null)
            {
                return;
            }

            var rect = _image.rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            var width = Mathf.Max(1, Mathf.RoundToInt(_textureSize * rect.width / rect.height));
            if (_texture != null && _texture.width == width && _texture.height == _textureSize)
            {
                return;
            }

            if (_texture != null)
            {
                _camera.targetTexture = null;
                _texture.Release();
                Destroy(_texture);
            }
            _texture = new RenderTexture(width, _textureSize, 16, RenderTextureFormat.ARGB32);
            _camera.targetTexture = _texture;
            _camera.ResetAspect();
            _image.texture = _texture;
        }

        private GameObject FindPrefab(string targetId)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                return null;
            }
            foreach (var entry in _prefabs)
            {
                if (entry.TargetId == targetId && entry.Prefab != null)
                {
                    return entry.Prefab;
                }
            }
            return null;
        }

        private void SetImageVisible(bool isVisible)
        {
            if (_image != null)
            {
                _image.enabled = isVisible;
            }
        }
    }
}
