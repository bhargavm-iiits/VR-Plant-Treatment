using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>The three chamber classes: a compact bench chamber, a tall floor-standing one and a wide walk-in bay.</summary>
    public enum ChamberKind
    {
        Bench,
        Tall,
        WalkIn,
    }

    /// <summary>
    /// A chamber fitted to one specimen. Chamber space: X right, Y up, Z away from the viewer (the front glass faces -Z);
    /// the origin is the interior's floor-plan centre on the hall floor. Heights are above the hall floor unless noted.
    /// </summary>
    public struct ChamberLayout
    {
        public ChamberKind Kind;
        /// <summary>Clear interior width, height and depth (glass to glass, interior floor to ceiling glass).</summary>
        public Vector3 Interior;
        /// <summary>Outer size including frame, plinth and (for a bench chamber) the lab bench it stands on.</summary>
        public Vector3 Exterior;
        /// <summary>Interior floor above the hall floor.</summary>
        public float FloorHeight;
        /// <summary>Soil tray outer width (X) and depth (Z).</summary>
        public Vector2 TraySize;
        public float TrayHeight;
        /// <summary>Height of the pedestal under the tray (0 when the tray stands on the chamber floor).</summary>
        public float PedestalHeight;
        /// <summary>Soil surface above the interior floor (pedestal + tray).</summary>
        public float SoilSurface;
        /// <summary>The stem pivot's XZ offset from the interior centre (the tray is centred under it).</summary>
        public Vector2 PlantPivot;
        /// <summary>The specimen's measured bounds relative to its stem pivot, at Farm scale.</summary>
        public Bounds PlantBounds;
        /// <summary>Bottom of the front glass door above the interior floor: below it the front is open (service hatch).</summary>
        public float HatchTop;
        public float SideClearance;
        public float TopClearance;

        /// <summary>The stem pivot on the soil surface, in chamber space.</summary>
        public Vector3 SpecimenAnchor => new Vector3(PlantPivot.x, FloorHeight + SoilSurface, PlantPivot.y);

        /// <summary>The interior box in chamber space.</summary>
        public Bounds InteriorBounds => new Bounds(new Vector3(0f, FloorHeight + Interior.y * 0.5f, 0f), Interior);

        /// <summary>The front face line (Z) of the exterior, in chamber space.</summary>
        public float FrontZ => -Exterior.z * 0.5f;
    }

    /// <summary>
    /// Sizes a treatment chamber around a specimen's measured bounds (pure; unit tested). The class follows the plant:
    /// walk-in when it is wider than 1.8 m or taller than 2.6 m, bench when it fits 0.95 m high and 0.9 m across, else
    /// tall. Interior width, height and depth are sized independently with per-class clearance, the plant's XZ bounds
    /// are centred and the tray is centred under the stem. Frame, glass and handle sizes are constant.
    /// </summary>
    public static class ChamberFit
    {
        public const float FrameThickness = 0.045f;
        public const float GlassThickness = 0.008f;
        public const float HandleLength = 0.30f;
        /// <summary>Top of the lab bench a bench chamber stands on.</summary>
        public const float BenchTop = 0.86f;
        public const float BenchPlinth = 0.04f;
        public const float FloorPlinth = 0.06f;
        /// <summary>The lab bench is this much larger than the chamber on each side.</summary>
        public const float BenchOverhang = 0.06f;
        public const float WalkInMinInterior = 2.0f, WalkInMinHeight = 2.2f;

        /// <summary>
        /// The largest exterior any Farm plant can produce (every DiseaseVariantCatalog prefab at its largest Farm scale and
        /// any yaw, measured by BTP/Treatment/5 - Verify Specimen Chamber, plus margin). The hall is built around it.
        /// </summary>
        public static readonly Vector3 ReservedExterior = new Vector3(7.9f, 5.1f, 7.9f);

        public static ChamberKind Classify(Vector3 size)
        {
            var footprint = Mathf.Max(size.x, size.z);
            if (footprint > 1.8f || size.y > 2.6f)
                return ChamberKind.WalkIn;
            if (size.y <= 0.95f && footprint <= 0.9f)
                return ChamberKind.Bench;
            return ChamberKind.Tall;
        }

        public static void Clearance(ChamberKind kind, out float side, out float top)
        {
            switch (kind)
            {
                case ChamberKind.Bench: side = 0.08f; top = 0.12f; break;
                case ChamberKind.Tall: side = 0.15f; top = 0.20f; break;
                default: side = 0.30f; top = 0.35f; break;
            }
        }

        /// <param name="plant">The specimen's bounds relative to its stem pivot (chamber orientation, Farm scale).</param>
        public static ChamberLayout Fit(Bounds plant)
        {
            var size = plant.size;
            var height = Mathf.Max(plant.max.y, 0.05f);
            var kind = Classify(new Vector3(size.x, height, size.z));
            Clearance(kind, out var side, out var top);

            // Soil tray (pot) from the footprint; it is wider than deep so its front window shows the roots.
            float trayX, trayZ, trayHeight;
            switch (kind)
            {
                case ChamberKind.Bench:
                    trayX = Mathf.Clamp(size.x * 0.7f, 0.24f, 0.45f);
                    trayZ = Mathf.Clamp(size.z * 0.6f, 0.2f, 0.36f);
                    trayHeight = 0.14f;
                    break;
                case ChamberKind.Tall:
                    trayX = Mathf.Clamp(size.x * 0.62f, 0.42f, 0.75f);
                    trayZ = Mathf.Clamp(size.z * 0.5f, 0.32f, 0.55f);
                    trayHeight = 0.22f;
                    break;
                default:
                    trayX = Mathf.Clamp(size.x * 0.32f, 0.9f, 1.6f);
                    trayZ = Mathf.Clamp(size.z * 0.28f, 0.8f, 1.3f);
                    trayHeight = 0.26f;
                    break;
            }

            // Centre the plant's XZ bounds; the stem pivot (and the tray under it) sits at the opposite offset.
            var pivot = new Vector2(-plant.center.x, -plant.center.z);
            var halfX = Mathf.Max(size.x * 0.5f, Mathf.Abs(pivot.x) + trayX * 0.5f);
            var halfZ = Mathf.Max(size.z * 0.5f, Mathf.Abs(pivot.y) + trayZ * 0.5f);
            var interior = new Vector3(2f * (halfX + side), 0f, 2f * (halfZ + side));

            // Small and medium plants stand on a pedestal so the tray window and the canopy sit near eye level.
            float soil, pedestal;
            switch (kind)
            {
                case ChamberKind.Tall:
                    soil = Mathf.Clamp(1.05f - 0.35f * height, 0.3f, 0.75f);
                    pedestal = soil - trayHeight;
                    break;
                default:
                    soil = trayHeight;
                    pedestal = 0f;
                    break;
            }

            // Anything below the pivot (a sunk root flare) must still be inside the soil.
            if (-plant.min.y > soil - 0.01f)
            {
                pedestal += -plant.min.y - (soil - 0.01f);
                soil = -plant.min.y + 0.01f;
            }

            interior.y = soil + height + top;
            if (kind == ChamberKind.WalkIn)
            {
                interior.x = Mathf.Max(interior.x, WalkInMinInterior);
                interior.z = Mathf.Max(interior.z, WalkInMinInterior);
                interior.y = Mathf.Max(interior.y, WalkInMinHeight);
            }
            else
            {
                var min = kind == ChamberKind.Bench ? 0.36f : 0.8f;
                interior.x = Mathf.Max(interior.x, min);
                interior.z = Mathf.Max(interior.z, min * 0.8f);
            }

            var floor = kind == ChamberKind.Bench ? BenchTop + BenchPlinth : FloorPlinth;
            var hatch = kind == ChamberKind.Bench ? 0.3f : kind == ChamberKind.Tall ? 0.5f : 0.75f;
            var hatchTop = Mathf.Min(soil + hatch, interior.y - 0.12f);

            var outer = new Vector3(interior.x + 2f * FrameThickness, floor + interior.y + FrameThickness, interior.z + 2f * FrameThickness);
            if (kind == ChamberKind.Bench)
            {
                outer.x += 2f * BenchOverhang;
                outer.z += 2f * BenchOverhang;
            }

            return new ChamberLayout
            {
                Kind = kind,
                Interior = interior,
                Exterior = outer,
                FloorHeight = floor,
                TraySize = new Vector2(trayX, trayZ),
                TrayHeight = trayHeight,
                PedestalHeight = Mathf.Max(0f, pedestal),
                SoilSurface = soil,
                PlantPivot = pivot,
                PlantBounds = plant,
                HatchTop = hatchTop,
                SideClearance = side,
                TopClearance = top,
            };
        }

        /// <summary>A tomato-sized default (1.3 x 1.75 x 1.3 m), so a freshly built scene shows a sensible chamber.</summary>
        public static ChamberLayout Default() =>
            Fit(new Bounds(new Vector3(0f, 0.875f, 0f), new Vector3(1.3f, 1.75f, 1.3f)));
    }
}
