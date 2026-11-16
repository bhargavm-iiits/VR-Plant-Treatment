using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BTP.Tests
{
    /// <summary>
    /// The baked per-leaf wind (LeafWindBaker, replayed by LeafWind.hlsl): for the cherry tree
    /// (BlenderTreeBuilder.ApplyCherry), the bell pepper (BellPepperPlantBuilder.Apply) and each of the nine tomato
    /// plants (TomatoPlantBuilder.Apply), the texture
    /// matches its Blender export and holds sane keys, and the Farm prefab's leaves actually use it.
    /// Each case is skipped until that plant has been exported and applied.
    /// </summary>
    public class LeafWindTests
    {
        public readonly struct WindCase
        {
            public readonly string Name;
            public readonly string Texture;
            public readonly string Report;
            public readonly string Prefab;
            public readonly string Material;

            public WindCase(string name, string texture, string report, string prefab, string material)
            {
                Name = name;
                Texture = texture;
                Report = report;
                Prefab = prefab;
                Material = material;
            }

            public override string ToString() => Name;
        }

        const string Models = "Assets/_Project/Models";
        const string Prefabs = "Assets/_Project/Prefabs/Plants";

        public static readonly string[] TomatoDiseases =
        {
            "bacterial_spot", "early_blight", "late_blight", "leaf_mold", "mosaic_virus",
            "septoria_leaf_spot", "target_spot", "spider_mites", "yellow_leaf_curl_virus",
        };

        public static IEnumerable<WindCase> Cases()
        {
            yield return new WindCase("cherry", $"{Models}/CherryTree/CherryTree_Wind.asset", $"{Models}/CherryTree/CherryTree_export.json",
                $"{Prefabs}/Cherry_Farm.prefab", "CherryLeaf");
            yield return new WindCase("bell pepper", $"{Models}/BellPepperPlant/BellPepper_Wind.asset", $"{Models}/BellPepperPlant/BellPepper_export.json",
                $"{Prefabs}/BellPepper_Farm.prefab", "BellPepperLeaf");
            foreach (var id in TomatoDiseases)
                yield return new WindCase($"tomato {id}", $"{Models}/TomatoPlant/Tomato_{id}_Wind.asset", $"{Models}/TomatoPlant/Tomato_{id}_export.json",
                    $"{Prefabs}/Tomato/Tomato_{id}_Farm.prefab", $"TomatoLeaf_{id}");
        }

        static Texture2D LoadWind(WindCase c)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(c.Texture);
            if (texture == null)
                Assert.Ignore($"{c.Texture} not built yet (run the BTP/Setup/Replace ... (Wind) menu item for {c.Name}).");
            return texture;
        }

        static string ReportValue(WindCase c, string key)
        {
            Assume.That(File.Exists(c.Report), Is.True, $"{c.Report} missing");
            var match = Regex.Match(File.ReadAllText(c.Report), $"\"{key}\"\\s*:\\s*([0-9.]+)");
            Assert.That(match.Success, Is.True, $"{key} not in {c.Report}");
            return match.Groups[1].Value;
        }

        [TestCaseSource(nameof(Cases))]
        public void WindTexture_MatchesExportedLeavesAndKeys(WindCase c)
        {
            var texture = LoadWind(c);
            var leaves = int.Parse(ReportValue(c, "leafCount"), CultureInfo.InvariantCulture);
            var keys = int.Parse(ReportValue(c, "keyCount"), CultureInfo.InvariantCulture);
            Assert.That(texture.width, Is.EqualTo(keys + 1), "width = keys + pivot column");
            Assert.That(texture.height, Is.EqualTo(leaves));
            Assert.That(texture.format, Is.EqualTo(TextureFormat.RGBAFloat));
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Point));
        }

        [TestCaseSource(nameof(Cases))]
        public void WindKeys_AreUnitQuaternions_StartingAtIdentity(WindCase c)
        {
            var texture = LoadWind(c);
            Assume.That(texture.isReadable, Is.True, "wind texture is not readable");
            var data = texture.GetPixelData<Vector4>(0);
            var keys = texture.width - 1;
            for (var leaf = 0; leaf < texture.height; leaf++)
            {
                var first = data[leaf * texture.width];
                Assert.That(Vector4.Distance(first, new Vector4(0f, 0f, 0f, 1f)), Is.LessThan(1e-4f), $"leaf {leaf}: key 0 is not identity");
                for (var k = 0; k < keys; k++)
                {
                    var q = data[leaf * texture.width + k];
                    Assert.That(q.magnitude, Is.EqualTo(1f).Within(1e-3f), $"leaf {leaf} key {k} is not unit length");
                    // Neighbouring keys share a hemisphere, so the shader's nlerp never takes the long way round.
                    if (k > 0)
                        Assert.That(Vector4.Dot(q, data[leaf * texture.width + k - 1]), Is.GreaterThan(0f), $"leaf {leaf} key {k} flips hemisphere");
                }

                Assert.That(data[leaf * texture.width + keys].w, Is.EqualTo(1f), $"leaf {leaf}: pivot column");
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void FarmPrefabLeaves_UseLeafWind(WindCase c)
        {
            var texture = LoadWind(c);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.Prefab);
            Assume.That(prefab, Is.Not.Null, $"{c.Prefab} missing");
            var leaf = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .FirstOrDefault(m => m != null && m.name == c.Material);
            Assert.That(leaf, Is.Not.Null, $"{c.Prefab} has no {c.Material} material (not applied?)");
            Assert.That(leaf.IsKeywordEnabled("_LEAF_WIND"), Is.True);
            Assert.That(leaf.GetTexture("_WindTex"), Is.EqualTo(texture));
            var seconds = float.Parse(ReportValue(c, "seconds"), CultureInfo.InvariantCulture);
            Assert.That(leaf.GetFloat("_WindLoopSeconds"), Is.EqualTo(seconds).Within(1e-4f));
        }
    }
}
