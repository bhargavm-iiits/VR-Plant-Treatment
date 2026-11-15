using BTP.Farm;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BTP.XR
{
    /// <summary>
    /// Finds a session's specimen in the loaded Farm (the disease row's <see cref="SelectablePlant"/> and one of its plants)
    /// and a standing point beside it: on the floor, outside every collider and facing the plant.
    /// </summary>
    public static class FarmSpecimenLocator
    {
        const float BodyRadius = 0.25f;

        public static bool TryLocate(Scene scene, XrSpecimenCatalog.Specimen specimen, out SelectablePlant row, out Transform plant, out string error)
        {
            row = null;
            plant = null;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                error = "The Farm is not loaded.";
                return false;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<SelectablePlant>(true))
                {
                    if (candidate.PlantId != specimen.plantId || candidate.DiseaseId != specimen.diseaseId)
                        continue;
                    row = candidate;
                    plant = candidate.PlantAt(specimen.plantIndex);
                    if (plant != null)
                    {
                        error = null;
                        return true;
                    }

                    error = $"The Farm row for {specimen.plantId}/{specimen.diseaseId} has no plant {specimen.plantIndex + 1}.";
                    return false;
                }
            }

            error = $"The Farm has no row for {specimen.plantId}/{specimen.diseaseId} (specimen '{specimen.specimenId}').";
            return false;
        }

        /// <summary>The first clear standing point of <see cref="XrSpecimenCatalog.StandCandidates"/>, dropped onto the floor.</summary>
        public static bool TryStandingPose(SelectablePlant row, Transform plant, float distance, out Vector3 position, out Quaternion rotation,
            float side = 0f)
        {
            var forward = row != null ? row.transform.forward : Vector3.forward;
            foreach (var candidate in XrSpecimenCatalog.StandCandidates(plant.position, forward, distance, side))
            {
                var floor = FloorAt(candidate, plant.position.y);
                var point = new Vector3(candidate.x, floor, candidate.z);
                if (!IsClear(point))
                    continue;
                position = point;
                rotation = XrSpecimenCatalog.Facing(point, plant.position);
                return true;
            }

            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        static float FloorAt(Vector3 point, float fallback)
        {
            if (Physics.Raycast(point + Vector3.up * 2.5f, Vector3.down, out var hit, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                hit.normal.y > 0.7f)
                return hit.point.y;
            return Mathf.Min(fallback, 0f);
        }

        /// <summary>No solid collider where a standing person's body would be (ankle to head height).</summary>
        static bool IsClear(Vector3 floor) =>
            !Physics.CheckCapsule(floor + Vector3.up * (0.3f + BodyRadius), floor + Vector3.up * 1.8f, BodyRadius, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
    }

    /// <summary>Whether an OpenXR display is actually running (Quest Link active and the player rendering to it).</summary>
    public static class XrReadiness
    {
        static readonly System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem> Displays =
            new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();

        public static bool DisplayRunning(out string state)
        {
            SubsystemManager.GetSubsystems(Displays);
            if (Displays.Count == 0)
            {
                state = "no XR display (start Quest Link, then the player)";
                return false;
            }

            foreach (var display in Displays)
            {
                if (display.running)
                {
                    state = "running";
                    return true;
                }
            }

            state = "XR display stopped (activate Quest Link)";
            return false;
        }
    }
}
