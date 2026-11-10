using UnityEngine;
using UnityEngine.UI;

namespace BTP.UI
{
    /// <summary>
    /// Persistent "selected" look for a button (the current plant or week). Hover and press
    /// feedback come from the Button's colour transition; this adds a cyan outline and fill.
    /// </summary>
    public sealed class SelectedStateVisual : MonoBehaviour
    {
        [SerializeField] Graphic outline;
        [SerializeField] Button button;

        bool selected;

        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                Refresh();
            }
        }

        internal void Bind(Graphic outlineGraphic, Button target)
        {
            outline = outlineGraphic;
            button = target;
            Refresh();
        }

        void OnEnable() => Refresh();

        void Refresh()
        {
            if (outline != null)
                outline.enabled = selected;
            if (button == null)
                return;

            var colors = button.colors;
            colors.normalColor = selected ? UIPalette.ButtonSelected : UIPalette.ButtonNormal;
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
        }
    }
}
