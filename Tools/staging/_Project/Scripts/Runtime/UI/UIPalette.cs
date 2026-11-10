using UnityEngine;

namespace BTP.UI
{
    /// <summary>
    /// Colours from the lab reference: charcoal translucent panels, white text, cyan selection
    /// states and restrained red/green health cues.
    /// </summary>
    public static class UIPalette
    {
        public static readonly Color Panel = new Color(0.07f, 0.10f, 0.13f, 0.86f);
        public static readonly Color PanelBorder = new Color(0.13f, 0.83f, 0.93f, 0.55f);
        public static readonly Color Header = new Color(0.10f, 0.15f, 0.19f, 0.95f);
        public static readonly Color Cyan = new Color(0.13f, 0.83f, 0.93f, 1f);
        public static readonly Color TextPrimary = Color.white;
        public static readonly Color TextSecondary = new Color(0.72f, 0.78f, 0.83f, 1f);
        public static readonly Color ButtonNormal = new Color(0.14f, 0.19f, 0.24f, 0.96f);
        public static readonly Color ButtonHover = new Color(0.19f, 0.31f, 0.39f, 1f);
        public static readonly Color ButtonPressed = new Color(0.08f, 0.55f, 0.64f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.12f, 0.14f, 0.16f, 0.6f);
        public static readonly Color ButtonSelected = new Color(0.09f, 0.42f, 0.50f, 1f);
        public static readonly Color HealthGood = new Color(0.20f, 0.83f, 0.60f, 1f);
        public static readonly Color HealthBad = new Color(0.97f, 0.44f, 0.44f, 1f);
        public static readonly Color Warning = new Color(0.98f, 0.75f, 0.14f, 1f);
        public static readonly Color TrackBackground = new Color(1f, 1f, 1f, 0.12f);
    }
}
