using System;
using System.Collections.Generic;

namespace BTP.Plants
{
    /// <summary>
    /// The PlantVillage disease classes (healthy leaves excluded) for the nine Farm plants: 24 in total.
    /// Pure data, so tests and the editor builder share one list. Leaf images live in
    /// Assets/_Project/Textures/DiseaseLeaves/&lt;plantId&gt;/&lt;diseaseId&gt;.jpg.
    /// </summary>
    public static class DiseaseClasses
    {
        public readonly struct Info
        {
            public readonly string PlantId;
            public readonly string DiseaseId;
            public readonly string DisplayName;

            public Info(string plantId, string diseaseId, string displayName)
            {
                PlantId = plantId;
                DiseaseId = diseaseId;
                DisplayName = displayName;
            }
        }

        public const string Credit = "Leaf images: PlantVillage dataset (Hughes, Salathé et al.), CC BY-SA 3.0";
        public const string SourceUrl = "https://github.com/spMohanty/PlantVillage-Dataset";

        static readonly Info[] AllInfo =
        {
            new Info("apple", "apple_scab", "Apple Scab"),
            new Info("apple", "black_rot", "Black Rot"),
            new Info("apple", "cedar_apple_rust", "Cedar Apple Rust"),
            new Info("bell_pepper", "bacterial_spot", "Bacterial Spot"),
            new Info("cherry", "powdery_mildew", "Powdery Mildew"),
            new Info("corn", "gray_leaf_spot", "Gray Leaf Spot (Cercospora)"),
            new Info("corn", "common_rust", "Common Rust"),
            new Info("corn", "northern_leaf_blight", "Northern Leaf Blight"),
            new Info("grape", "black_rot", "Black Rot"),
            new Info("grape", "esca", "Esca (Black Measles)"),
            new Info("grape", "leaf_blight", "Leaf Blight (Isariopsis Leaf Spot)"),
            new Info("peach", "bacterial_spot", "Bacterial Spot"),
            new Info("potato", "early_blight", "Early Blight"),
            new Info("potato", "late_blight", "Late Blight"),
            new Info("strawberry", "leaf_scorch", "Leaf Scorch"),
            new Info("tomato", "bacterial_spot", "Bacterial Spot"),
            new Info("tomato", "early_blight", "Early Blight"),
            new Info("tomato", "late_blight", "Late Blight"),
            new Info("tomato", "leaf_mold", "Leaf Mold"),
            new Info("tomato", "septoria_leaf_spot", "Septoria Leaf Spot"),
            new Info("tomato", "spider_mites", "Two-Spotted Spider Mite"),
            new Info("tomato", "target_spot", "Target Spot"),
            new Info("tomato", "yellow_leaf_curl_virus", "Yellow Leaf Curl Virus"),
            new Info("tomato", "mosaic_virus", "Mosaic Virus"),
        };

        static readonly IReadOnlyList<Info> NoInfo = Array.Empty<Info>();

        public static IReadOnlyList<Info> All => AllInfo;

        /// <summary>The disease the Lab recovery demo shows first for each plant (illustrative, not a diagnosis).</summary>
        public static string DefaultDiseaseId(string plantId)
        {
            switch (plantId)
            {
                case "tomato": return "early_blight";
                case "apple": return "apple_scab";
                case "bell_pepper": return "bacterial_spot";
                case "cherry": return "powdery_mildew";
                case "corn": return "common_rust";
                case "grape": return "black_rot";
                case "peach": return "bacterial_spot";
                case "potato": return "early_blight";
                case "strawberry": return "leaf_scorch";
                default: return null;
            }
        }

        /// <summary>Index of the default disease within <see cref="For"/>; 0 when the plant is unknown.</summary>
        public static int DefaultIndex(string plantId)
        {
            var id = DefaultDiseaseId(plantId);
            var list = For(plantId);
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].DiseaseId == id)
                    return i;
            }

            return 0;
        }

        /// <summary>Moves <paramref name="index"/> by <paramref name="delta"/> inside <paramref name="count"/> entries, wrapping around.</summary>
        public static int Step(int index, int count, int delta)
        {
            if (count <= 0)
                return 0;
            return ((index + delta) % count + count) % count;
        }

        /// <summary>The classes of one plant in display order; empty for an unknown ID.</summary>
        public static IReadOnlyList<Info> For(string plantId)
        {
            if (string.IsNullOrEmpty(plantId))
                return NoInfo;
            var list = new List<Info>();
            foreach (var info in AllInfo)
            {
                if (info.PlantId == plantId)
                    list.Add(info);
            }

            return list.Count == 0 ? NoInfo : list;
        }
    }
}
