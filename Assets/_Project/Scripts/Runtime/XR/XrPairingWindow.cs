using UnityEngine;
using UnityEngine.InputSystem;

namespace BTP.XR
{
    /// <summary>
    /// The pairing panel of the Windows player, drawn on the PC monitor (the desktop mirror), where the keyboard is: the
    /// user presses Pair VR on the CropScan website and types the short one-time code here. Shown automatically while
    /// connected mode is unpaired; F8 toggles it. Never shown offline or in fixture mode.
    /// </summary>
    public sealed class XrPairingWindow : MonoBehaviour
    {
        XrSessionCoordinator coordinator;
        string code = "";
        bool visible;
        GUIStyle box;
        GUIStyle label;

        public void Bind(XrSessionCoordinator owner) => coordinator = owner;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
                visible = !visible;
        }

        void OnGUI()
        {
            if (coordinator == null || coordinator.CurrentMode != XrSessionCoordinator.Mode.Connected)
                return;
            if (!visible && coordinator.IsPaired)
                return;

            box ??= new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.UpperLeft, wordWrap = true, padding = new RectOffset(14, 14, 12, 12) };
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };

            var area = new Rect(Screen.width - 452f, 12f, 440f, 300f);
            GUI.Box(area, "CropScan VR pairing", box);
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 40f, area.width - 28f, area.height - 52f));
            GUILayout.Label(coordinator.IsPaired
                ? $"Paired as device {coordinator.DeviceId}. Socket: {coordinator.SocketState}. Headset: {coordinator.ReadinessText}."
                : "On the CropScan website, press Pair VR and type the code shown there.", label);
            GUILayout.Label("API: " + coordinator.ApiBaseUrl, label);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Code", label, GUILayout.Width(48f));
            code = GUILayout.TextField(code ?? "", 16, GUILayout.Width(180f));
            GUI.enabled = !coordinator.IsPairing && !string.IsNullOrWhiteSpace(code);
            if (GUILayout.Button("Pair", GUILayout.Width(80f)))
            {
                coordinator.Pair(code);
                code = "";
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (coordinator.IsPaired && GUILayout.Button("Unpair this PC", GUILayout.Width(160f)))
                coordinator.Unpair();
            if (!string.IsNullOrEmpty(coordinator.PairingMessage))
                GUILayout.Label(coordinator.PairingMessage, label);
            GUILayout.Label("F8 shows or hides this window.", label);
            GUILayout.EndArea();
        }
    }
}
