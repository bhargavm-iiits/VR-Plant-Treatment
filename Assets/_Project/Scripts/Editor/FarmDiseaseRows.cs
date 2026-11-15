using System.Collections.Generic;
using System.Linq;
using BTP.Farm;
using BTP.Plants;
using BTP.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Editor
{
    /// <summary>
    /// The Farm's disease rows: one straight row per teaching case (58: the 24 PlantVillage classes and the 34
    /// conditions of the ten field and tropical crops), each a single plant type showing that one disease
    /// (DiseaseVariantCatalog), grouped by plant in seven bands along the central aisle, with a cross path at each
    /// band's head where every row has its label (plant, disease, reference photo):
    ///   band 1 (nearest the entrance): the nine tomato rows, staked, on raised beds either side of the aisle;
    ///   band 2: strawberry, bell pepper, potato x2 and corn x3 on the left; the three pergola vineyard rows on the right;
    ///   band 3: the apple x3 orchard rows on the left; the cherry and peach rows on the right;
    ///   band 4: the nine soybean rows on ridges, laid out like the tomato band;
    ///   band 5: rice x5 and wheat x3 on the left; cotton x3 and chilli x3 on the right;
    ///   band 6: sugarcane x4 on the left; the blueberry and squash beds on the right;
    ///   band 7 (far end): the tropical orchard, mango x3 on the left, the fourth mango row and the orange row on the right.
    /// Spacing follows the plants' real sizes: tomatoes 1.3 m apart (1.3 m wide plants), trees 4.6 m apart in rows
    /// 5.2 m apart (canopies about 4 m), pergola modules 3.2 m (they join into one canopy), small crops denser. Rows
    /// run along Z (towards the far end), so walkways between beds lead from the head paths along every row.
    /// Every row is one <see cref="SelectablePlant"/>: each plant has its own collider, and selecting any plant selects
    /// that plant and the row's disease.
    /// </summary>
    static class FarmDiseaseRows
    {
        public enum Kind
        {
            /// <summary>Raised soil bed with a drip line (tomato, pepper, potato, strawberry).</summary>
            Bed,
            /// <summary>Low earthed-up ridge (corn).</summary>
            Ridge,
            /// <summary>Orchard tree row on a worked soil strip.</summary>
            Tree,
            /// <summary>Pergola vine modules (the module brings its own posts, wires, ridge and drip line).</summary>
            Pergola,
        }

        public sealed class Row
        {
            public string PlantId;
            public string DiseaseId;
            public Kind Kind;
            /// <summary>Row centre line X.</summary>
            public float X;
            /// <summary>Where the row's bed (or first module edge) starts, at its head; the row runs towards +Z.</summary>
            public float ZStart;
            public int Count;
            public float Spacing;
            public bool Stakes;

            public float Length => Count * Spacing;
            public float ZEnd => ZStart + Length;
            public Vector3 PlantPosition(int i) => new Vector3(X, BaseHeight(Kind), ZStart + Spacing * (i + 0.5f));
        }

        public const float BedHeight = 0.25f, RidgeHeight = 0.08f, BedWidth = 1.2f;
        public const float AisleHalfWidth = 1.2f;
        public const float PathWidth = 1.6f;

        // Band heads: the cross path runs from Path to Path + PathWidth; rows start RowGap after it.
        const float RowGap = 0.7f;
        public const float Band1Path = -3.2f, Band2Path = 7.0f, Band3Path = 19.8f;
        const float Band1Start = Band1Path + PathWidth + RowGap;   // -0.9
        const float Band2Start = Band2Path + PathWidth + RowGap;   // 9.3
        const float Band3Start = Band3Path + PathWidth + RowGap;   // 22.1
        // The four bands behind the orchard (the field was 46 m long before them; FarmSceneBuilder.FenceMaxZ).
        public const float Band4Path = 37.0f, Band5Path = 46.8f, Band6Path = 56.7f, Band7Path = 66.8f;
        const float Band4Start = Band4Path + PathWidth + RowGap;   // 39.3
        const float Band5Start = Band5Path + PathWidth + RowGap;   // 49.1
        const float Band6Start = Band6Path + PathWidth + RowGap;   // 59.0
        const float Band7Start = Band7Path + PathWidth + RowGap;   // 69.1

        /// <summary>The head path of each band (scene object name, Z of its near edge), entrance first.</summary>
        public static readonly (string name, float z)[] HeadPaths =
        {
            ("HeadPath_TomatoRows", Band1Path), ("HeadPath_FieldCropAndVineyardRows", Band2Path), ("HeadPath_OrchardRows", Band3Path),
            ("HeadPath_SoybeanRows", Band4Path), ("HeadPath_CerealCottonAndChilliRows", Band5Path),
            ("HeadPath_SugarcaneAndShrubRows", Band6Path), ("HeadPath_TropicalOrchardRows", Band7Path),
        };

        public static float BaseHeight(Kind kind) => kind == Kind.Bed ? BedHeight : kind == Kind.Ridge ? RidgeHeight : 0f;

        /// <summary>Viewpoints on the band head paths (position, facing yaw), for teleport anchors.</summary>
        public static readonly (string name, Vector3 position, float yaw)[] Viewpoints =
        {
            ("Viewpoint_TomatoRows", new Vector3(-6.0f, 0f, Band1Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_FieldCropRows", new Vector3(-8.5f, 0f, Band2Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_VineyardRows", new Vector3(8.2f, 0f, Band2Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_AppleRows", new Vector3(-6.6f, 0f, Band3Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_CherryPeachRows", new Vector3(6.5f, 0f, Band3Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_SoybeanRows", new Vector3(-6.0f, 0f, Band4Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_CerealRows", new Vector3(-8.2f, 0f, Band5Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_CottonAndChilliRows", new Vector3(8.2f, 0f, Band5Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_SugarcaneRows", new Vector3(-7.2f, 0f, Band6Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_BlueberryAndSquashRows", new Vector3(4.8f, 0f, Band6Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_MangoRows", new Vector3(-6.6f, 0f, Band7Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_OrangeRow", new Vector3(6.7f, 0f, Band7Path + PathWidth * 0.5f), 0f),
            ("Viewpoint_FieldEnd", new Vector3(0f, 0f, FarmSceneBuilder.FenceMaxZ - 1.5f), 180f),
        };

        /// <summary>The 58 rows, in DiseaseClasses order per plant.</summary>
        public static List<Row> Layout()
        {
            var rows = new List<Row>();
            Row Add(string plant, string disease, Kind kind, float x, float zStart, int count, float spacing, bool stakes = false)
            {
                var row = new Row { PlantId = plant, DiseaseId = disease, Kind = kind, X = x, ZStart = zStart, Count = count, Spacing = spacing, Stakes = stakes };
                rows.Add(row);
                return row;
            }

            // Band 1: tomatoes, 5 plants 1.3 m apart on 6.5 m beds; rows 2.5 m apart (1.3 m wide plants leave a
            // 1.2 m walkway), five left of the aisle and four right, read left to right from the entrance.
            float[] tomatoX = { -12.25f, -9.75f, -7.25f, -4.75f, -2.25f, 2.25f, 4.75f, 7.25f, 9.75f };
            var tomatoes = DiseaseClasses.For("tomato");
            for (var i = 0; i < tomatoes.Count; i++)
                Add("tomato", tomatoes[i].DiseaseId, Kind.Bed, tomatoX[i], Band1Start, 5, 1.3f, true);

            // Band 2, left: low crops nearest the aisle, the 2.2 m corn on the outside, rows 2.1 m apart.
            Add("strawberry", "leaf_scorch", Kind.Bed, -2.15f, Band2Start, 15, 0.44f);
            Add("bell_pepper", "bacterial_spot", Kind.Bed, -4.25f, Band2Start, 10, 0.66f);
            var potato = DiseaseClasses.For("potato");
            Add("potato", potato[0].DiseaseId, Kind.Bed, -6.35f, Band2Start, 13, 0.5f);
            Add("potato", potato[1].DiseaseId, Kind.Bed, -8.45f, Band2Start, 13, 0.5f);
            var corn = DiseaseClasses.For("corn");
            for (var i = 0; i < corn.Count; i++)
                Add("corn", corn[i].DiseaseId, Kind.Ridge, -10.55f - 2.1f * i, Band2Start, 11, 0.6f);

            // Band 2, right: three pergola rows of three 3.2 m modules, 1.4 m walkways between them.
            var grape = DiseaseClasses.For("grape");
            for (var i = 0; i < grape.Count; i++)
                Add("grape", grape[i].DiseaseId, Kind.Pergola, 3.6f + 4.6f * i, Band2Start, 3, BlenderTreeBuilder.PergolaModule_Size);

            // Band 3: orchard rows of three trees 4.6 m apart, rows 5.2 m apart (canopies about 4 m across).
            var apple = DiseaseClasses.For("apple");
            for (var i = 0; i < apple.Count; i++)
                Add("apple", apple[i].DiseaseId, Kind.Tree, -4.1f - 5.2f * i, Band3Start, 3, 4.6f);
            Add("cherry", "powdery_mildew", Kind.Tree, 4.1f, Band3Start, 3, 4.6f);
            Add("peach", "bacterial_spot", Kind.Tree, 9.3f, Band3Start, 3, 4.6f);

            // Band 4: soybean (0.85 m tall, 0.7 m wide), nine plants 0.7 m apart on ridges, at the tomato band's
            // row positions: five rows left of the aisle and four right.
            var soybean = DiseaseClasses.For("soybean");
            for (var i = 0; i < soybean.Count; i++)
                Add("soybean", soybean[i].DiseaseId, Kind.Ridge, tomatoX[i], Band4Start, 9, 0.7f);

            // Band 5, left: the cereals (about 1.1 m tall, 0.85 m wide clumps), eight clumps 0.8 m apart on ridges
            // 2 m apart: rice nearest the aisle, wheat outside it.
            var rice = DiseaseClasses.For("rice");
            for (var i = 0; i < rice.Count; i++)
                Add("rice", rice[i].DiseaseId, Kind.Ridge, -2.2f - 2f * i, Band5Start, 8, 0.8f);
            var wheat = DiseaseClasses.For("wheat");
            for (var i = 0; i < wheat.Count; i++)
                Add("wheat", wheat[i].DiseaseId, Kind.Ridge, -12.2f - 2f * i, Band5Start, 8, 0.8f);

            // Band 5, right: cotton (1.05 m tall, 0.75 m wide) on ridges 2.3 m apart, then chilli (0.75 m) on raised beds.
            var cotton = DiseaseClasses.For("cotton");
            for (var i = 0; i < cotton.Count; i++)
                Add("cotton", cotton[i].DiseaseId, Kind.Ridge, 2.4f + 2.3f * i, Band5Start, 7, 0.9f);
            var chilli = DiseaseClasses.For("chilli");
            for (var i = 0; i < chilli.Count; i++)
                Add("chilli", chilli[i].DiseaseId, Kind.Bed, 9.4f + 2.2f * i, Band5Start, 8, 0.8f);

            // Band 6, left: sugarcane (3.2 m tall clumps up to 1.9 m across), five clumps 1.3 m apart, rows 2.9 m apart.
            var sugarcane = DiseaseClasses.For("sugarcane");
            for (var i = 0; i < sugarcane.Count; i++)
                Add("sugarcane", sugarcane[i].DiseaseId, Kind.Ridge, -2.9f - 2.9f * i, Band6Start, 5, 1.3f);
            // Band 6, right: the blueberry shrubs (1.2 m) and the sprawling squash (1.4 m across) on raised beds.
            Add("blueberry", "rust", Kind.Bed, 3.0f, Band6Start, 5, 1.3f);
            Add("squash", "powdery_mildew", Kind.Bed, 6.6f, Band6Start, 4, 1.6f);

            // Band 7: the tropical orchard. Mango (3.8 m tall, canopy about 4.8 m) and orange (3.6 m, about 4.1 m):
            // rows of three trees 5 m apart, rows 5.2 m apart like the apple orchard.
            var mango = DiseaseClasses.For("mango");
            for (var i = 0; i < mango.Count; i++)
                Add("mango", mango[i].DiseaseId, Kind.Tree, i < 3 ? -4.1f - 5.2f * i : 4.1f, Band7Start, 3, 5.0f);
            Add("orange", "huanglongbing", Kind.Tree, 9.3f, Band7Start, 3, 5.0f);
            return rows;
        }

        public sealed class Materials
        {
            public Material Soil, Dirt, Wood, Pipe, Ring, Stake;
        }

        /// <summary>Builds every row (ground works under <paramref name="beds"/>, plants under <paramref name="crops"/>).</summary>
        public static List<SelectablePlant> Build(Transform crops, Transform beds, DiseaseVariantCatalog variants, DiseaseGalleryCatalog photos,
            Materials m, DiseaseGalleryController gallery)
        {
            var built = new List<SelectablePlant>();
            var zones = new Dictionary<string, Transform>();
            foreach (var row in Layout())
            {
                if (!variants.TryGet(row.PlantId, row.DiseaseId, out var entry))
                    throw new System.InvalidOperationException($"No Farm variant for {row.PlantId}/{row.DiseaseId}; run BTP/Setup/26 - Build Farm Disease Variants.");
                var zone = ZoneName(row.PlantId);
                if (!zones.TryGetValue(zone, out var parent))
                    zones[zone] = parent = EnvironmentKit.Group(zone, crops).transform;
                BuildGround(row, beds, m);
                built.Add(BuildRow(row, parent, entry.farmPrefab, Photo(photos, row), m, gallery));
            }

            return built;
        }

        static string ZoneName(string plantId) => plantId switch
        {
            "tomato" => "TomatoRows",
            "apple" or "cherry" or "peach" => "Orchard",
            "grape" => "Vineyard",
            "soybean" => "SoybeanRows",
            "rice" or "wheat" => "CerealRows",
            "mango" or "orange" => "TropicalOrchard",
            _ => "FieldCropRows",
        };

        static Texture2D Photo(DiseaseGalleryCatalog photos, Row row)
        {
            if (photos == null)
                return null;
            foreach (var disease in photos.GetDiseases(row.PlantId))
            {
                if (disease.id == row.DiseaseId)
                    return disease.leaf;
            }

            return null;
        }

        static void BuildGround(Row row, Transform beds, Materials m)
        {
            var name = $"{row.PlantId}_{row.DiseaseId}";
            var centre = (row.ZStart + row.ZEnd) * 0.5f;
            switch (row.Kind)
            {
                case Kind.Bed:
                    EnvironmentKit.Box($"Bed_{name}", beds, new Vector3(row.X, BedHeight * 0.5f, centre), new Vector3(BedWidth, BedHeight, row.Length + 0.1f), m.Soil);
                    // The bed's drip line is the irrigation branch pipe (FarmIrrigationBuilder).
                    break;
                case Kind.Ridge:
                    EnvironmentKit.Box($"Ridge_{name}", beds, new Vector3(row.X, RidgeHeight * 0.5f, centre), new Vector3(0.7f, RidgeHeight, row.Length + 0.1f), m.Soil);
                    break;
                case Kind.Tree:
                    // A worked strip under the trees: no collider, so it never blocks teleporting.
                    EnvironmentKit.Box($"Strip_{name}", beds, new Vector3(row.X, 0.02f, centre), new Vector3(1.8f, 0.02f, row.Length), m.Dirt, false, 0f, false);
                    break;
            }
        }

        static SelectablePlant BuildRow(Row row, Transform parent, GameObject prefab, Texture2D photo, Materials m, DiseaseGalleryController gallery)
        {
            var root = new GameObject($"Row_{row.PlantId}_{row.DiseaseId}");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(row.X, 0f, row.ZStart);

            // Size of one plant (all of a row's plants are the same model).
            var probe = EnvironmentKit.Place(prefab, root.transform, Vector3.zero, 0f);
            var bounds = NearBounds(probe, root.transform);
            Object.DestroyImmediate(probe);
            var radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 0.25f, 1.7f);
            var height = Mathf.Max(bounds.size.y, 0.25f);

            // Each row gets its own jitter, so changing one row never moves another.
            var random = new System.Random(StableSeed(row.PlantId + "/" + row.DiseaseId));
            float Next() => (float)random.NextDouble();
            var slots = new List<Transform>();
            var jitter = row.Kind == Kind.Pergola ? 0f : row.Kind == Kind.Tree ? 0.15f : 0.04f;
            for (var i = 0; i < row.Count; i++)
            {
                var position = row.PlantPosition(i) - root.transform.localPosition + new Vector3((Next() - 0.5f) * 2f * jitter, 0f, 0f);
                var slot = new GameObject($"Plant_{i + 1:00}");
                slot.transform.SetParent(root.transform, false);
                slot.transform.localPosition = position;
                // Pergola modules stay aligned with the row so neighbouring modules join; everything else turns freely.
                var yaw = row.Kind == Kind.Pergola ? 0f : Next() * 360f;
                var scale = row.Kind == Kind.Pergola ? 1f : row.Kind == Kind.Tree ? 0.92f + Next() * 0.1f : 0.92f + Next() * 0.14f;
                EnvironmentKit.Place(prefab, slot.transform, Vector3.zero, yaw, scale);
                // A cane beside the main stem, up to the ~1.75 m plant's upper truss.
                if (row.Stakes)
                    EnvironmentKit.Cylinder("Stake", slot.transform, new Vector3(0.12f, 0.8f, 0f), 0.012f, 1.6f, m.Stake, false);

                // The plant's own selection volume (the XR ray and the mouse pick it). The pergola's is its canopy
                // slab only, so teleport rays still reach the ground beneath it.
                var collider = slot.AddComponent<BoxCollider>();
                if (row.Kind == Kind.Pergola)
                {
                    collider.center = new Vector3(bounds.center.x, 2.1f, bounds.center.z);
                    collider.size = new Vector3(bounds.size.x * 0.95f, 0.8f, bounds.size.z * 0.95f);
                }
                else
                {
                    var shrink = row.Kind == Kind.Tree ? 0.85f : 1f;
                    collider.center = new Vector3(bounds.center.x, height * 0.5f, bounds.center.z);
                    collider.size = new Vector3(Mathf.Max(bounds.size.x * shrink, 0.35f), height, Mathf.Max(bounds.size.z * shrink, 0.35f));
                }

                slots.Add(slot.transform);
            }

            var first = slots[0].localPosition;

            // One ring and one name card per row; they move to the plant that is pointed at or selected.
            var ringMesh = ProceduralMeshes.Ring($"SelectionRing_{radius:0.00}", radius, radius + 0.06f);
            var ring = EnvironmentKit.MeshObject("SelectionRing", root.transform, ringMesh, m.Ring, first + new Vector3(0f, 0.02f, 0f),
                Quaternion.identity, Vector3.one, false, false);
            var selectionRing = ring.AddComponent<SelectionRing>();
            selectionRing.Bind(ring.GetComponent<MeshRenderer>(), true);
            // Hidden until a plant of the row is pointed at (SelectionRing shows it), in the editor too.
            ring.GetComponent<MeshRenderer>().enabled = false;

            var nameCard = NameCard(root.transform, row, first, radius, height);
            BuildLabel(root.transform, row, photo, m);

            XRSceneWiring.MakeSelectableSpecimen(root, row.PlantId, selectionRing, nameCard, gallery);
            var selectable = root.GetComponent<SelectablePlant>();
            selectable.BindRow(row.DiseaseId, slots.ToArray());
            return selectable;
        }

        /// <summary>The card shown while the row is selected, above its first plant (trees: in front of the canopy).</summary>
        static PlantNameCard NameCard(Transform root, Row row, Vector3 first, float radius, float height)
        {
            var position = row.Kind == Kind.Tree || row.Kind == Kind.Pergola
                ? first + new Vector3(0f, 2.0f, -(radius + 0.3f))
                : first + new Vector3(0f, Mathf.Min(height + 0.35f, 2.1f), 0f);
            var card = UIFactory.WorldCanvas("NameCard", root, new Vector2(620f, 200f), position, Quaternion.identity);
            card.gameObject.AddComponent<FaceCamera>();
            var cardRect = (RectTransform)card.transform;
            var content = UIFactory.Node("Content", cardRect);
            UIFactory.Stretch(content);
            UIFactory.Panel(content);
            var title = UIFactory.Text("Title", content, Title(row), 40f, Color.white, TextAlignmentOptions.Top, FontStyles.Bold);
            title.enableAutoSizing = true;
            title.fontSizeMin = 24f;
            title.fontSizeMax = 40f;
            UIFactory.Stretch(title.rectTransform, 18f);
            title.rectTransform.offsetMin = new Vector2(18f, 60f);
            var subtitle = UIFactory.Text("Subtitle", content, "Selected", 26f, UIPalette.Cyan, TextAlignmentOptions.Bottom);
            UIFactory.Stretch(subtitle.rectTransform, 20f);
            var nameCard = card.gameObject.AddComponent<PlantNameCard>();
            nameCard.Bind(content.gameObject, title, subtitle);
            content.gameObject.SetActive(false);
            return nameCard;
        }

        static string PlantName(string plantId) => plantId switch
        {
            "bell_pepper" => "Bell Pepper",
            "corn" => "Corn (Maize)",
            _ => char.ToUpperInvariant(plantId[0]) + plantId.Substring(1),
        };

        static string DiseaseName(Row row) => DiseaseClasses.For(row.PlantId).First(d => d.DiseaseId == row.DiseaseId).DisplayName;

        static string Title(Row row) => $"{PlantName(row.PlantId)}: {DiseaseName(row)}";

        public const float LabelWidth = 660f, LabelHeight = 210f;
        /// <summary>Labels show within this ground distance (beyond it their text is unreadable; see CanvasDistanceCull).</summary>
        public const float LabelViewDistance = 12f;
        const float LabelCentreHeight = 1.0f;

        /// <summary>
        /// The row's label at its head, facing the head path (and the entrance): the Farm sign style (wooden post,
        /// brown board, white text) with the PlantVillage photo of the disease, the plant name and the disease name.
        /// Named NameSign, so the Treatment step adds the row's treatment status under it.
        /// </summary>
        static void BuildLabel(Transform root, Row row, Texture2D photo, Materials m)
        {
            // Just ahead of the bed (trees and pergolas: of the first canopy), centred on the row.
            const float z = -0.35f;
            var signPosition = new Vector3(0f, LabelCentreHeight, z);
            var post = EnvironmentKit.Box("SignPost", root, new Vector3(0f, (LabelCentreHeight - LabelHeight * 0.0005f) * 0.5f, z + 0.03f),
                new Vector3(0.05f, LabelCentreHeight - LabelHeight * 0.0005f, 0.05f), m.Wood, false);
            GameObjectUtility.SetStaticEditorFlags(post, 0);

            var sign = UIFactory.WorldCanvas("NameSign", root, new Vector2(LabelWidth, LabelHeight), signPosition, Quaternion.identity);
            sign.gameObject.AddComponent<CanvasDistanceCull>().Bind(LabelViewDistance);
            var rect = (RectTransform)sign.transform;
            UIFactory.Panel(rect, new Color(0.24f, 0.17f, 0.10f, 0.95f), false);
            var textLeft = 16f;
            if (photo != null)
            {
                var image = new GameObject("Photo", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                image.transform.SetParent(rect, false);
                image.texture = photo;
                image.raycastTarget = false;
                UIFactory.Place(image.rectTransform, 14f, 14f, LabelHeight - 28f, LabelHeight - 28f);
                textLeft = LabelHeight + 2f;
            }

            var plant = UIFactory.Text("Plant", rect, PlantName(row.PlantId), 34f, new Color(0.93f, 0.88f, 0.8f), TextAlignmentOptions.TopLeft, FontStyles.Bold);
            UIFactory.Place(plant.rectTransform, textLeft, 14f, LabelWidth - textLeft - 14f, 46f);
            var disease = UIFactory.Text("Disease", rect, DiseaseName(row), 40f, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            disease.enableAutoSizing = true;
            disease.fontSizeMin = 24f;
            disease.fontSizeMax = 40f;
            UIFactory.Place(disease.rectTransform, textLeft, 62f, LabelWidth - textLeft - 14f, LabelHeight - 76f);
        }

        /// <summary>Bounds of a plant's near renderers (no billboard) in <paramref name="space"/>.</summary>
        static Bounds NearBounds(GameObject model, Transform space)
        {
            var bounds = new Bounds();
            var first = true;
            var lod = model.GetComponent<LODGroup>();
            var near = lod != null ? lod.GetLODs()[0].renderers : model.GetComponentsInChildren<Renderer>();
            foreach (var renderer in near.Where(r => r != null && r.name != "Pergola"))
            {
                var b = renderer.bounds;
                var local = new Bounds(space.InverseTransformPoint(b.center), Vector3.zero);
                local.Encapsulate(space.InverseTransformPoint(b.min));
                local.Encapsulate(space.InverseTransformPoint(b.max));
                if (first)
                    bounds = local;
                else
                    bounds.Encapsulate(local);
                first = false;
            }

            return bounds;
        }

        static int StableSeed(string text)
        {
            unchecked
            {
                var h = 23;
                foreach (var c in text)
                    h = h * 31 + c;
                return h & 0x7FFFFFFF;
            }
        }
    }
}
