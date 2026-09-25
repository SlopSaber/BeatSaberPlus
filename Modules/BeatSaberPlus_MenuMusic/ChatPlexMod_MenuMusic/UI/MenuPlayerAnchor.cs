using CP_SDK.UI.Components;
using HMUI;
using UnityEngine;

namespace ChatPlexMod_MenuMusic.UI
{
    /// <summary>
    /// Keeps the player above the game's left screen in the same curved canvas.
    /// </summary>
    internal sealed class MenuPlayerAnchor : MonoBehaviour
    {
        private CFloatingPanel m_Panel;
        private RectTransform m_LeftScreen;
        private CurvedCanvasSettings m_MenuCurve;
        private float m_Clearance;
        private float m_LastRadius = float.NaN;

        internal void Init(CFloatingPanel p_Panel, RectTransform p_LeftScreen, float p_Clearance)
        {
            m_Panel = p_Panel;
            m_LeftScreen = p_LeftScreen;
            m_Clearance = p_Clearance;
            m_MenuCurve = p_LeftScreen.GetComponent<Canvas>().rootCanvas.GetComponent<CurvedCanvasSettings>();

            // Remain a sibling: HMUI disables LeftScreen when a flow has no left
            // view. The music player should still be visible in that menu.
            var l_Transform = m_Panel.RTransform;
            l_Transform.anchorMin = l_Transform.anchorMax = new Vector2(0.5f, 0.5f);
            l_Transform.pivot = new Vector2(0.5f, 0.5f);
            m_Panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (!m_Panel || !m_LeftScreen)
                return;

            var l_Transform = m_Panel.RTransform;
            var l_LeftRect = m_LeftScreen.rect;
            var l_Position = m_LeftScreen.localPosition + m_LeftScreen.localRotation * Vector3.Scale(
                new Vector3(l_LeftRect.center.x, l_LeftRect.yMax + m_Clearance + l_Transform.rect.height * 0.5f, 0.0f),
                m_LeftScreen.localScale);

            // Both screens share a parent, so menu movement, scaling and room
            // adjustment are inherited without world-space offsets.
            if (l_Transform.localPosition != l_Position)
                l_Transform.localPosition = l_Position;
            if (l_Transform.localRotation != m_LeftScreen.localRotation)
                l_Transform.localRotation = m_LeftScreen.localRotation;
            if (l_Transform.localScale != m_LeftScreen.localScale)
                l_Transform.localScale = m_LeftScreen.localScale;

            // ScreenModeController changes the game's curvature at runtime.
            var l_Radius = m_MenuCurve ? m_MenuCurve.radius : 0.0f;
            if (l_Radius != m_LastRadius)
            {
                m_Panel.SetRadius(l_Radius);
                m_LastRadius = l_Radius;
            }
        }
    }
}
