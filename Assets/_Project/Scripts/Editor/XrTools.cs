using System.Collections.Generic;
using System.IO;
using BTP.Treatment;
using BTP.XR;
using SQLite;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Editor helpers for the CropScan XR path: play the bundled fixture without command-line flags, forget saved fixture
    /// results, and export the mapped cases' recipes as the backend's seed (from treatment_content.sql).
    /// </summary>
    static class XrTools
    {
        const string FixtureMenu = "BTP/XR/Fixture Mode (Play)";
        const string SqlPath = "Assets/_Project/Data/Treatment/treatment_content.sql";
        const string SeedPath = "Docs/xr_recipe_seed.json";

        [MenuItem(FixtureMenu)]
        static void ToggleFixture()
        {
            var on = PlayerPrefs.GetString(XrSessionCoordinator.EditorFixturePref, "") != "";
            PlayerPrefs.SetString(XrSessionCoordinator.EditorFixturePref, on ? "" : XrLaunchArgs.DefaultFixture);
            PlayerPrefs.Save();
            Debug.Log(on ? "[BTP][XR] Fixture mode off: Play runs the offline demo." : "[BTP][XR] Fixture mode on: Play opens the bundled Tomato Early Blight fixture.");
        }

        [MenuItem(FixtureMenu, true)]
        static bool ToggleFixtureCheck()
        {
            Menu.SetChecked(FixtureMenu, PlayerPrefs.GetString(XrSessionCoordinator.EditorFixturePref, "") != "");
            return true;
        }

        [MenuItem("BTP/XR/Reset Fixture Results")]
        static void ResetFixture()
        {
            var path = Path.Combine(Application.persistentDataPath, "xr_fixture_store.json");
            if (File.Exists(path))
                File.Delete(path);
            Debug.Log("[BTP][XR] Fixture results forgotten: " + path);
        }

        [System.Serializable]
        sealed class Seed
        {
            public int mapVersion;
            public List<XrRecipe> recipes = new List<XrRecipe>();
        }

        [MenuItem("BTP/XR/Export Recipe Seed for Backend")]
        static void ExportSeed()
        {
            var map = XrCaseMap.LoadDefault(out var problems);
            foreach (var problem in problems)
                Debug.LogError("[BTP][XR] Case map: " + problem);
            var repository = new SqliteTreatmentRepository(() =>
            {
                var db = new SQLiteConnection(":memory:");
                db.ExecuteScript(File.ReadAllText(SqlPath));
                return db;
            });
            repository.InitializeAsync().Wait();
            var seed = new Seed { mapVersion = map.Version };
            foreach (var row in map.Rows)
            {
                var c = repository.GetCase(new CaseKey(row.plantId, row.diseaseId), out var error);
                var recipe = c != null ? XrRecipeAdapter.FromCase(c, row.recipeId, 1, "backend") : null;
                if (recipe == null)
                    Debug.LogError($"[BTP][XR] No manual treatment content for {row.plantId}/{row.diseaseId}: {error ?? c?.ManualError}");
                else
                    seed.recipes.Add(recipe);
            }

            repository.Dispose();
            Directory.CreateDirectory(Path.GetDirectoryName(SeedPath));
            File.WriteAllText(SeedPath, JsonUtility.ToJson(seed, true));
            Debug.Log($"[BTP][XR] Wrote {seed.recipes.Count} recipe(s) to {SeedPath}.");
        }
    }
}
