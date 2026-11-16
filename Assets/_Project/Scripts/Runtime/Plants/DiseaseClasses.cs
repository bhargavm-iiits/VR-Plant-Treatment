using System;
using System.Collections.Generic;

namespace BTP.Plants
{
    /// <summary>
    /// The teaching cases (healthy leaves excluded). <see cref="All"/> is the 24 PlantVillage classes of the
    /// nine original plants: the connected CropScan XR mapping uses only these. <see cref="LabOnly"/> adds 34
    /// conditions of 10 further crops (leaf specimens in the Lab and the local Treatment lab; the name dates
    /// from when they had no whole-plant model). <see cref="AllCases"/> is both: 58, and every one of them has a
    /// Farm row and a Farm model.
    /// Pure data, so tests and the editor builders share one list. Reference images live in
    /// Assets/_Project/Textures/DiseaseLeaves/&lt;plantId&gt;/&lt;diseaseId&gt;.jpg (PlantVillage photo) or .png
    /// (render of the supplied specimen, for conditions without a PlantVillage photo).
    /// </summary>
    public static class DiseaseClasses
    {
        public readonly struct Info
        {
            public readonly string PlantId;
            public readonly string DiseaseId;
            public readonly string DisplayName;
            /// <summary>A known limitation of the supplied specimen model, shown in the Lab; null when none.</summary>
            public readonly string SpecimenNote;

            public Info(string plantId, string diseaseId, string displayName, string specimenNote = null)
            {
                PlantId = plantId;
                DiseaseId = diseaseId;
                DisplayName = displayName;
                SpecimenNote = specimenNote;
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

        /// <summary>The ten Lab-only crops, in selector order (after the nine Farm crops).</summary>
        public static readonly string[] LabOnlyPlantIds =
            { "blueberry", "chilli", "cotton", "mango", "orange", "rice", "soybean", "squash", "sugarcane", "wheat" };

        static readonly Info[] LabOnlyInfo =
        {
            new Info("blueberry", "rust", "Leaf Rust"),
            new Info("chilli", "anthracnose", "Anthracnose",
                "Specimen also shows strong yellow chlorosis; the small ringed spots are the anthracnose sign."),
            new Info("chilli", "cercospora_leaf_spot", "Cercospora Leaf Spot"),
            new Info("chilli", "leaf_curl", "Leaf Curl Virus"),
            new Info("cotton", "bacterial_blight", "Bacterial Blight"),
            new Info("cotton", "fusarium_wilt", "Fusarium Wilt"),
            new Info("cotton", "leaf_curl_virus", "Leaf Curl Virus"),
            new Info("mango", "anthracnose", "Anthracnose"),
            new Info("mango", "bacterial_canker", "Bacterial Canker (Black Spot)"),
            new Info("mango", "die_back", "Die Back"),
            new Info("mango", "gall_midge", "Leaf Gall Midge"),
            new Info("orange", "huanglongbing", "Huanglongbing (Citrus Greening)"),
            new Info("rice", "bacterial_leaf_blight", "Bacterial Leaf Blight"),
            new Info("rice", "brown_spot", "Brown Spot"),
            new Info("rice", "leaf_blast", "Leaf Blast"),
            new Info("rice", "sheath_blight", "Sheath Blight",
                "Supplied model was exported as rice leaf scald; its banded leaf lesions are not confirmed as sheath blight."),
            new Info("rice", "tungro", "Tungro"),
            new Info("soybean", "bacterial_blight", "Bacterial Blight"),
            new Info("soybean", "cercospora_leaf_blight", "Cercospora Leaf Blight"),
            new Info("soybean", "downy_mildew", "Downy Mildew"),
            new Info("soybean", "frogeye_leaf_spot", "Frogeye Leaf Spot"),
            new Info("soybean", "mosaic_virus", "Mosaic Virus",
                "Specimen mismatch: this model shows white powdery patches (exported as powdery mildew), not mosaic mottling."),
            new Info("soybean", "septoria_brown_spot", "Septoria Brown Spot"),
            new Info("soybean", "soybean_rust", "Soybean Rust"),
            new Info("soybean", "sudden_death_syndrome", "Sudden Death Syndrome"),
            new Info("soybean", "target_spot", "Target Spot"),
            new Info("squash", "powdery_mildew", "Powdery Mildew"),
            new Info("sugarcane", "mosaic", "Mosaic"),
            new Info("sugarcane", "red_rot", "Red Rot"),
            new Info("sugarcane", "rust", "Rust"),
            new Info("sugarcane", "yellow_leaf", "Yellow Leaf"),
            new Info("wheat", "blast", "Wheat Blast",
                "Supplied model was exported under a rice name; the blade and lesions are consistent with wheat blast."),
            new Info("wheat", "leaf_blight", "Leaf Blight (Spot Blotch)",
                "Supplied model was exported under a corn name; the narrow blade and lesions are consistent with wheat."),
            new Info("wheat", "rust", "Leaf Rust",
                "Supplied model was exported under a rice name; the orange pustules are consistent with wheat leaf rust."),
        };

        static readonly Info[] AllCaseInfo = Concat(AllInfo, LabOnlyInfo);

        static Info[] Concat(Info[] a, Info[] b)
        {
            var result = new Info[a.Length + b.Length];
            a.CopyTo(result, 0);
            b.CopyTo(result, a.Length);
            return result;
        }

        static readonly IReadOnlyList<Info> NoInfo = Array.Empty<Info>();

        /// <summary>The 24 PlantVillage classes (CropScan XR). The Farm, Lab and Treatment use <see cref="AllCases"/>.</summary>
        public static IReadOnlyList<Info> All => AllInfo;

        /// <summary>The 34 conditions of the ten field and tropical crops.</summary>
        public static IReadOnlyList<Info> LabOnly => LabOnlyInfo;

        /// <summary>Every Farm, Lab and Treatment case: the 24 PlantVillage classes, then the 34 others (58).</summary>
        public static IReadOnlyList<Info> AllCases => AllCaseInfo;

        /// <summary>True for the ten field and tropical crops (leaf-specimen Lab preview; not in CropScan XR).</summary>
        public static bool IsLabOnlyPlant(string plantId) => Array.IndexOf(LabOnlyPlantIds, plantId) >= 0;

        /// <summary>True for one of the 24 PlantVillage classes (the cases a connected CropScan XR session may open).</summary>
        public static bool IsPlantVillageCase(string plantId, string diseaseId)
        {
            foreach (var info in AllInfo)
                if (info.PlantId == plantId && info.DiseaseId == diseaseId)
                    return true;
            return false;
        }

        /// <summary>The case info, or null-like default with an empty ID when unknown.</summary>
        public static bool TryGet(string plantId, string diseaseId, out Info info)
        {
            foreach (var candidate in AllCaseInfo)
                if (candidate.PlantId == plantId && candidate.DiseaseId == diseaseId)
                {
                    info = candidate;
                    return true;
                }
            info = default;
            return false;
        }

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
                // Lab-only crops: the first listed condition.
                case "blueberry": return "rust";
                case "chilli": return "leaf_curl";
                case "cotton": return "leaf_curl_virus";
                case "mango": return "anthracnose";
                case "orange": return "huanglongbing";
                case "rice": return "leaf_blast";
                case "soybean": return "frogeye_leaf_spot";
                case "squash": return "powdery_mildew";
                case "sugarcane": return "red_rot";
                case "wheat": return "rust";
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
            foreach (var info in AllCaseInfo)
            {
                if (info.PlantId == plantId)
                    list.Add(info);
            }

            return list.Count == 0 ? NoInfo : list;
        }
    }
}
