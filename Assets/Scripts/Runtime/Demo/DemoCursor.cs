#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NCAIClicker.Targets;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace NCAIClicker.Demo
{
    /// <summary>
    /// 제출 영상 녹화용 자동 시연 커서 (#45). 에디터 전용이며 빌드에 포함되지 않는다.
    /// <para>
    /// 게임 코드를 건드리지 않고 <b>가상 마우스 장치</b>를 추가해 입력을 넣는다. 망치 판정과 UI 가 모두
    /// <c>Mouse.current</c> 를 읽으므로, 위치와 왼쪽 버튼 상태를 넣으면 실제 조작과 똑같이 동작한다.
    /// 시연 동안 실제 마우스는 꺼 둔다 — 켜 두면 OS 커서 이벤트가 가상 입력을 덮어쓴다.
    /// 기존 커서 점은 플레이 중에만 보이므로 메뉴에서도 보이는 점을 따로 그린다.
    /// </para>
    /// </summary>
    public class DemoCursor : MonoBehaviour
    {
        [SerializeField] private float _dotSize = 14f;
        [SerializeField] private Color _dotColor = Color.white;
        [SerializeField] private Color _rippleColor = new Color(1f, 0.85f, 0.35f, 0.9f);
        [SerializeField] private float _rippleSize = 64f;
        [SerializeField] private float _rippleDuration = 0.35f;

        [Tooltip("초당 이동 픽셀. 클수록 빠르다.")]
        [SerializeField] private float _moveSpeed = 900f;
        [SerializeField] private float _minMoveDuration = 0.35f;

        [Tooltip("크리처를 따라갈 때 반응 지연(초). 클수록 굼뜨다.")]
        [SerializeField] private float _followSmoothTime = 0.18f;

        [Tooltip("클릭 전 목표 위에 머무는 시간(초).")]
        [SerializeField] private float _clickHoverDelay = 0.25f;

        private const float ButtonPollInterval = 0.1f;

        private RectTransform _canvasRect;
        private RectTransform _dot;
        private Image _ripple;
        private Vector2 _position;
        private Vector2 _sentPosition;
        private bool _isButtonDown;
        private Coroutine _scenarioRoutine;
        private Mouse _virtualMouse;
        private readonly List<InputDevice> _disabledDevices = new List<InputDevice>();
        private bool _wasRunInBackground;
        private InputSettings.BackgroundBehavior _previousBackgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode _previousEditorBehavior;

        public static DemoCursor Instance { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsRunning => _scenarioRoutine != null;

        /// <summary>시연 커서를 만들고 시나리오를 실행한다. 이미 있으면 새 시나리오로 다시 시작한다.</summary>
        public static DemoCursor StartDemo(DemoScenario scenario)
        {
            if (Instance == null)
            {
                var go = new GameObject("DemoCursor");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<DemoCursor>();
            }
            Instance.RunScenario(scenario);
            return Instance;
        }

        /// <summary>시연 커서를 없애고 실제 마우스를 되살린다.</summary>
        public static void StopDemo()
        {
            if (Instance != null)
            {
                Destroy(Instance.gameObject);
            }
        }

        public void RunScenario(DemoScenario scenario)
        {
            if (_scenarioRoutine != null)
            {
                StopCoroutine(_scenarioRoutine);
            }
            IsPaused = false;
            _scenarioRoutine = StartCoroutine(RunSteps(scenario));
        }

        /// <summary>시나리오 진행을 멈춘다. 커서는 제자리에 남는다.</summary>
        public void Pause()
        {
            IsPaused = true;
        }

        public void Resume()
        {
            IsPaused = false;
        }

        private void Awake()
        {
            // 에디터 창이 포커스를 잃으면 Play Mode 프레임이 멈춘다. 녹화 중 다른 창을 봐도 이어지게 한다.
            // 런타임 값이라 Project Settings 에는 저장되지 않는다.
            _wasRunInBackground = Application.runInBackground;
            Application.runInBackground = true;

            // 에디터는 Game 뷰·앱 포커스가 없으면 입력을 게임에 넘기지 않는다. 시연 동안만 포커스를 무시하고
            // OnDestroy 에서 되돌린다. 이 프로젝트엔 InputSettings 에셋이 없어 메모리 값만 바뀐다.
            var settings = InputSystem.settings;
            _previousBackgroundBehavior = settings.backgroundBehavior;
            _previousEditorBehavior = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            var realMouse = Mouse.current;
            var screenRect = new Rect(0f, 0f, Screen.width, Screen.height);
            _position = realMouse != null && screenRect.Contains(realMouse.position.ReadValue())
                ? realMouse.position.ReadValue()
                : screenRect.center;

            foreach (var device in InputSystem.devices)
            {
                if (device is Mouse && device.enabled)
                {
                    InputSystem.DisableDevice(device);
                    _disabledDevices.Add(device);
                }
            }
            _virtualMouse = InputSystem.AddDevice<Mouse>("DemoMouse");
            _virtualMouse.MakeCurrent();
            _sentPosition = _position;

            BuildVisuals();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            Cursor.visible = true;
            Application.runInBackground = _wasRunInBackground;
            if (_virtualMouse != null && _virtualMouse.added)
            {
                InputSystem.RemoveDevice(_virtualMouse);
            }
            foreach (var device in _disabledDevices)
            {
                if (device.added)
                {
                    InputSystem.EnableDevice(device);
                    (device as Mouse)?.MakeCurrent();
                }
            }
            InputSystem.settings.backgroundBehavior = _previousBackgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = _previousEditorBehavior;
        }

        private void Update()
        {
            SendMouseState();
        }

        private void LateUpdate()
        {
            // 결과창 등이 OS 커서를 다시 켜므로 매 프레임 숨긴다.
            Cursor.visible = false;
            _dot.anchoredPosition = ScreenToCanvas(_position);
        }

        private void SendMouseState()
        {
            if (_virtualMouse == null)
            {
                return;
            }
            var state = new MouseState
            {
                position = _position,
                delta = _position - _sentPosition,
            }.WithButton(MouseButton.Left, _isButtonDown);
            InputSystem.QueueStateEvent(_virtualMouse, state);
            _sentPosition = _position;
        }

        private IEnumerator RunSteps(DemoScenario scenario)
        {
            for (var i = 0; i < scenario.Steps.Count; i++)
            {
                var step = scenario.Steps[i];
                yield return WaitWhilePaused();
                Debug.Log($"[DemoCursor] {i + 1}/{scenario.Steps.Count} {step.Kind} {step.Label}");
                switch (step.Kind)
                {
                    case DemoStepKind.ClickButton:
                        yield return ClickButton(step.Label, 0, step.Seconds);
                        break;
                    case DemoStepKind.ClickIndex:
                        yield return ClickButton(step.Label, step.Index, step.Seconds);
                        break;
                    case DemoStepKind.HuntCreatures:
                        yield return HuntCreatures(step.Seconds, step.Label);
                        break;
                    case DemoStepKind.WaitUntilButton:
                        yield return WaitForButton(step.Label, 0, step.Seconds, null);
                        break;
                    case DemoStepKind.Wait:
                        yield return WaitUnpaused(step.Seconds);
                        break;
                    case DemoStepKind.MoveTo:
                        var target = Vector2.Scale(step.ViewportPoint, new Vector2(Screen.width, Screen.height));
                        yield return MoveTo(target);
                        break;
                }
            }
            Debug.Log("[DemoCursor] 시나리오를 끝까지 실행했습니다.");
            _scenarioRoutine = null;
        }

        private IEnumerator ClickButton(string label, int index, float timeout)
        {
            Button found = null;
            yield return WaitForButton(label, index, timeout, b => found = b);
            if (found == null)
            {
                Debug.Log($"[DemoCursor] '{label}' 버튼이 {timeout}초 안에 나타나지 않아 건너뜁니다.");
                yield break;
            }
            yield return MoveTo(GetScreenCenter(found));
            yield return WaitUnpaused(_clickHoverDelay);
            yield return Click();
        }

        private IEnumerator WaitForButton(string label, int index, float timeout, System.Action<Button> onFound)
        {
            var elapsed = 0f;
            while (elapsed < timeout)
            {
                var button = FindButton(label, index);
                if (button != null)
                {
                    onFound?.Invoke(button);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(ButtonPollInterval);
                if (!IsPaused)
                {
                    elapsed += ButtonPollInterval;
                }
            }
        }

        private IEnumerator HuntCreatures(float duration, string stopLabel)
        {
            var velocity = Vector2.zero;
            var elapsed = 0f;
            var nextPoll = 0f;
            while (elapsed < duration)
            {
                if (!IsPaused)
                {
                    elapsed += Time.unscaledDeltaTime;
                    if (!string.IsNullOrEmpty(stopLabel) && elapsed >= nextPoll)
                    {
                        nextPoll = elapsed + ButtonPollInterval;
                        if (FindButton(stopLabel, 0) != null)
                        {
                            yield break;
                        }
                    }
                    if (TryGetNearestCreature(out var target))
                    {
                        _position = Vector2.SmoothDamp(_position, target, ref velocity, _followSmoothTime,
                            Mathf.Infinity, Time.unscaledDeltaTime);
                    }
                }
                yield return null;
            }
        }

        /// <summary>
        /// 사람 손처럼 움직인다: 약간 휜 경로(2차 베지어) + 가속·감속(최소 저크 곡선) + 미세한 떨림.
        /// </summary>
        private IEnumerator MoveTo(Vector2 target)
        {
            var start = _position;
            var distance = Vector2.Distance(start, target);
            if (distance < 1f)
            {
                yield break;
            }
            var duration = Mathf.Max(_minMoveDuration, distance / _moveSpeed);
            var normal = new Vector2(-(target - start).y, (target - start).x).normalized;
            var control = (start + target) * 0.5f + normal * (distance * Random.Range(-0.15f, 0.15f));
            var seed = Random.value * 100f;

            var t = 0f;
            while (t < 1f)
            {
                if (!IsPaused)
                {
                    t = Mathf.Min(1f, t + Time.unscaledDeltaTime / duration);
                    var s = t * t * t * (10f + t * (-15f + 6f * t));
                    var a = Vector2.Lerp(start, control, s);
                    var b = Vector2.Lerp(control, target, s);
                    var tremble = (1f - s) * 1.5f;
                    var noise = new Vector2(Mathf.PerlinNoise(seed, t * 8f) - 0.5f, Mathf.PerlinNoise(t * 8f, seed) - 0.5f);
                    _position = Vector2.Lerp(a, b, s) + noise * tremble;
                }
                yield return null;
            }
            _position = target;
        }

        private IEnumerator Click()
        {
            _isButtonDown = true;
            StartCoroutine(PlayRipple());
            yield return WaitUnpaused(0.08f);
            _isButtonDown = false;
            yield return null;
        }

        private IEnumerator PlayRipple()
        {
            var rect = _ripple.rectTransform;
            rect.anchoredPosition = ScreenToCanvas(_position);
            _ripple.gameObject.SetActive(true);
            var t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / _rippleDuration;
                var size = Mathf.Lerp(_dotSize, _rippleSize, t);
                rect.sizeDelta = new Vector2(size, size);
                var color = _rippleColor;
                color.a *= 1f - t;
                _ripple.color = color;
                yield return null;
            }
            _ripple.gameObject.SetActive(false);
        }

        private IEnumerator WaitWhilePaused()
        {
            while (IsPaused)
            {
                yield return null;
            }
        }

        private IEnumerator WaitUnpaused(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                if (!IsPaused)
                {
                    elapsed += Time.unscaledDeltaTime;
                }
                yield return null;
            }
        }

        private bool TryGetNearestCreature(out Vector2 screenPoint)
        {
            screenPoint = default;
            var cam = Camera.main;
            if (cam == null)
            {
                return false;
            }
            var bestDistance = float.MaxValue;
            var isFound = false;
            foreach (var target in FindObjectsByType<Target>(FindObjectsSortMode.None))
            {
                if (!target.IsAlive)
                {
                    continue;
                }
                var p = cam.WorldToScreenPoint(target.transform.position);
                if (p.z <= 0f || p.x < 0f || p.y < 0f || p.x > Screen.width || p.y > Screen.height)
                {
                    continue;
                }
                var distance = Vector2.Distance(_position, p);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    screenPoint = p;
                    isFound = true;
                }
            }
            return isFound;
        }

        /// <summary>
        /// 화면에 실제로 보이고 눌리는 버튼 중 라벨이 일치하는 것을 왼쪽→오른쪽, 위→아래 순서로 골라
        /// index 번째를 돌려준다. 다른 UI 에 가려진 버튼은 제외한다.
        /// </summary>
        private Button FindButton(string label, int index)
        {
            var candidates = new List<Button>();
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                if (button.isActiveAndEnabled && button.IsInteractable() && IsMatch(button, label) && IsOnTop(button))
                {
                    candidates.Add(button);
                }
            }
            candidates.Sort((a, b) =>
            {
                var pa = GetScreenCenter(a);
                var pb = GetScreenCenter(b);
                return Mathf.Abs(pa.y - pb.y) > 20f ? pb.y.CompareTo(pa.y) : pa.x.CompareTo(pb.x);
            });
            return index < candidates.Count ? candidates[index] : null;
        }

        private static bool IsMatch(Button button, string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return true;
            }
            var text = button.GetComponentInChildren<TMP_Text>();
            var parentName = button.transform.parent != null ? button.transform.parent.name : string.Empty;
            foreach (var token in label.Split('|'))
            {
                var key = token.Trim();
                if (key.Length == 0)
                {
                    continue;
                }
                if (button.name.Contains(key) || parentName.Contains(key) || (text != null && text.text.Contains(key)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsOnTop(Button button)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return true;
            }
            var data = new PointerEventData(eventSystem) { position = GetScreenCenter(button) };
            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(data, hits);
            return hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(button.transform);
        }

        private static Vector2 GetScreenCenter(Button button)
        {
            var rect = (RectTransform)button.transform;
            var canvas = button.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(cam, rect.TransformPoint(rect.rect.center));
        }

        private Vector2 ScreenToCanvas(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local);
            return local;
        }

        private void BuildVisuals()
        {
            var canvasGo = new GameObject("DemoCursorCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            _canvasRect = (RectTransform)canvasGo.transform;

            var sprite = CreateCircleSprite(false);
            _ripple = CreateImage("Ripple", CreateCircleSprite(true), _rippleColor, _rippleSize);
            _ripple.gameObject.SetActive(false);
            _dot = CreateImage("Dot", sprite, _dotColor, _dotSize).rectTransform;

            var outline = _dot.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            outline.effectDistance = new Vector2(1f, -1f);
        }

        private Image CreateImage(string name, Sprite sprite, Color color, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvasRect, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = new Vector2(size, size);
            return image;
        }

        private static Sprite CreateCircleSprite(bool isRing)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var center = (size - 1) * 0.5f;
            var radius = size * 0.5f;
            const float ringWidth = 5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    var alpha = Mathf.Clamp01(radius - dist);
                    if (isRing)
                    {
                        alpha *= Mathf.Clamp01(dist - (radius - ringWidth));
                    }
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
#endif
