using BTP.Farm;
using BTP.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BTP.Editor
{
    /// <summary>XR Interaction Toolkit components for the environment scenes.</summary>
    static class XRSceneWiring
    {
        public const string TeleportLayerName = "Teleport";
        public const int TeleportLayerIndex = 31;

        static InteractionLayerMask TeleportLayers
        {
            get
            {
                var mask = InteractionLayerMask.GetMask(TeleportLayerName);
                return mask != 0 ? mask : 1 << TeleportLayerIndex;
            }
        }

        /// <summary>Makes a walkable surface a teleport destination (controller teleport ray).</summary>
        public static void MakeTeleportArea(GameObject surface)
        {
            var area = surface.GetComponent<TeleportationArea>();
            if (area == null)
                area = surface.AddComponent<TeleportationArea>();
            area.interactionLayers = TeleportLayers;
            area.matchOrientation = MatchOrientation.WorldSpaceUp;
        }

        /// <summary>A viewing point: a cyan pad that places the user facing <paramref name="yaw"/>.</summary>
        public static GameObject CreateTeleportAnchor(Transform parent, string name, Vector3 position, float yaw)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Pad";
            pad.transform.SetParent(anchor.transform, false);
            pad.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            pad.transform.localScale = new Vector3(0.9f, 0.01f, 0.9f);
            var padRenderer = pad.GetComponent<MeshRenderer>();
            padRenderer.sharedMaterial = MaterialFactory.Glow(ProjectPaths.EnvironmentMaterials + "/TeleportPad.mat",
                new Color(0.13f, 0.83f, 0.93f, 0.22f), 1.2f);
            padRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var ring = EnvironmentKit.MeshObject("Ring", anchor.transform, ProceduralMeshes.Ring("TeleportPadRing", 0.4f, 0.46f),
                MaterialFactory.Glow(ProjectPaths.EnvironmentMaterials + "/TeleportPadRing.mat", UIPalette.Cyan, 1.8f),
                new Vector3(0f, 0.052f, 0f), Quaternion.identity, Vector3.one, false, false);

            // Arrow showing the facing direction after teleporting.
            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrow.name = "FacingArrow";
            Object.DestroyImmediate(arrow.GetComponent<Collider>());
            arrow.transform.SetParent(anchor.transform, false);
            arrow.transform.localPosition = new Vector3(0f, 0.055f, 0.3f);
            arrow.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            arrow.transform.localScale = new Vector3(0.12f, 0.005f, 0.12f);
            arrow.GetComponent<MeshRenderer>().sharedMaterial = ring.GetComponent<MeshRenderer>().sharedMaterial;
            arrow.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var teleport = anchor.AddComponent<TeleportationAnchor>();
            teleport.interactionLayers = TeleportLayers;
            teleport.teleportAnchorTransform = anchor.transform;
            teleport.matchOrientation = MatchOrientation.TargetUpAndForward;
            return anchor;
        }

        /// <summary>Ray (trigger) or pinch selects the specimen; see <see cref="SelectablePlant"/>.</summary>
        public static void MakeSelectableSpecimen(GameObject root, string plantId, SelectionRing ring, PlantNameCard card)
        {
            if (root.GetComponent<XRSimpleInteractable>() == null)
                root.AddComponent<XRSimpleInteractable>();
            var selectable = root.GetComponent<SelectablePlant>();
            if (selectable == null)
                selectable = root.AddComponent<SelectablePlant>();
            selectable.Bind(plantId, ring, card);
        }

        /// <summary>World-space canvases need the tracked-device raycaster for XR rays and pokes.</summary>
        public static void MakeCanvasInteractive(Canvas canvas)
        {
            if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }
    }
}
