using UnityEngine;

namespace NCAIClicker.Core
{
    /// <summary>
    /// 망치 모델 조립과 스윙 포즈 계산을 한 곳에 둔다.
    ///
    /// 호버 망치(<see cref="HammerSwingVisual"/>)와 자동 망치(<see cref="AutoHammerVisual"/>)가
    /// 같은 망치를 쓴다. 치수·배색·모션 구간을 양쪽에 적어 두면 한쪽만 고쳐졌을 때 화면에 두
    /// 종류의 망치가 나온다 — 같은 값을 두 곳에 두지 않는다는 규칙(AGENTS.md)을 연출에도 적용했다.
    ///
    /// MonoBehaviour 가 아니다. 씬에 사는 것은 이것을 부르는 쪽이고, 여기는 만들기와 계산만 한다.
    /// </summary>
    public static class HammerRig
    {
        /// <summary>손잡이 배색. 원작 배색을 따른다 (빨간 손잡이 바).</summary>
        private static readonly Color HandleColor = new Color(0.82f, 0.22f, 0.16f);

        /// <summary>망치 머리 배색 (짙은 네이비).</summary>
        private static readonly Color HeadColor = new Color(0.18f, 0.22f, 0.28f);

        /// <summary>
        /// 망치 하나를 조립해 피벗 Transform 을 돌려준다. 셰이더를 찾지 못하면 null 이다 —
        /// 부르는 쪽은 그때 연출을 포기하고, 게임 자체는 계속 돌아가야 한다.
        /// </summary>
        public static Transform Build(Transform parent, string pivotName)
        {
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("[HammerRig] 셰이더 Universal Render Pipeline/Lit 를 찾지 못했다. " +
                               "Project Settings > Graphics > Always Included Shaders 에 등록돼 있는지 확인한다.");
                return null;
            }

            var pivotGo = new GameObject(pivotName);
            pivotGo.transform.SetParent(parent);
            var pivot = pivotGo.transform;

            AddPart(pivot, PrimitiveType.Cylinder, "HammerHandle", litShader, HandleColor,
                    new Vector3(0.22f, 0.35f, -0.18f), new Vector3(0.06f, 0.35f, 0.06f));

            AddPart(pivot, PrimitiveType.Cube, "HammerHead", litShader, HeadColor,
                    new Vector3(0.22f, 0.6f, 0.03f), new Vector3(0.18f, 0.16f, 0.32f));

            return pivot;
        }

        /// <summary>
        /// 스윙 포즈를 계산한다. 적용은 부르는 쪽이 한다 — 호버 망치는 커서 위치에, 자동 망치는
        /// 대상 주변에 서므로 높이와 각도만 공유하고 좌표는 각자 정한다.
        ///
        /// progress 0.0~0.8 장전(뒤로 높이 들림) / 0.8~0.92 강타(바닥으로) / 0.92~1.0 반동.
        /// </summary>
        public static void EvaluateSwing(float progress, out float angleX, out float heightY)
        {
            if (progress < 0.8f)
            {
                var t = progress / 0.8f;
                angleX = Mathf.Lerp(15f, 65f, t);
                heightY = Mathf.Lerp(0.2f, 0.75f, t);
            }
            else if (progress < 0.92f)
            {
                var t = (progress - 0.8f) / 0.12f;
                angleX = Mathf.Lerp(65f, -22f, t);
                heightY = Mathf.Lerp(0.75f, 0.02f, t);
            }
            else
            {
                var t = (progress - 0.92f) / 0.08f;
                angleX = Mathf.Lerp(-22f, 15f, t);
                heightY = Mathf.Lerp(0.02f, 0.2f, t);
            }
        }

        private static void AddPart(Transform pivot, PrimitiveType shape, string name, Shader shader,
                                    Color color, Vector3 localPosition, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.SetParent(pivot);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(38f, -18f, 0f);
            go.transform.localScale = localScale;

            // 콜라이더는 연출에 필요 없고, 남으면 조준 판정과 부딪힌다.
            // Edit Mode 검증에서도 이 조립이 돌 수 있어 재생 여부로 갈라 부른다 — Destroy 는
            // 에디터에서 다음 프레임을 기다리다 예외를 낸다.
            var partCollider = go.GetComponent<Collider>();
            if (Application.isPlaying)
            {
                Object.Destroy(partCollider);
            }
            else
            {
                Object.DestroyImmediate(partCollider);
            }

            var material = new Material(shader) { color = color };
            go.GetComponent<Renderer>().material = material;
        }
    }
}
