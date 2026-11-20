using System.Collections.Generic;
using BTP.Core;
using BTP.UI;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Back in the Farm after a guided treatment: one popup in the player's view per treated plant, confirming that plant's
    /// recovery and recapping exactly what was applied (temperature, scoops of which product, sprays, water %), with OK
    /// (Next while more plants wait). OK marks that recap shown (<see cref="GuidedTreatmentResults.Acknowledge"/>), so it is
    /// never repeated. Installs itself with the XR bootstrap (no scene change) and lives across environment switches.
    /// </summary>
    public sealed class TreatmentRecapPresenter : MonoBehaviour
    {
        const float ShowDelay = 0.8f;

        static TreatmentRecapPresenter instance;
        EnvironmentController environment;
        GuidedHud hud;
        readonly List<string> queue = new List<string>();
        int total;
        float showAt = -1f;
        string showing;

        public static TreatmentRecapPresenter Instance => instance;
        public GuidedHud Hud => hud;
        public string Showing => showing;
        public int Remaining => queue.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (instance != null)
                return;
            var go = new GameObject("Treatment Recap");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<TreatmentRecapPresenter>();
        }

        void Update()
        {
            if (environment == null)
            {
                environment = EnvironmentController.Instance;
                if (environment == null)
                    return;
                environment.EnvironmentLoaded += OnEnvironmentLoaded;
                if (environment.Current == EnvironmentId.Farm && !environment.IsSwitching)
                    OnEnvironmentLoaded(EnvironmentId.Farm);
            }

            if (showAt >= 0f && Time.unscaledTime >= showAt)
            {
                showAt = -1f;
                ShowNext();
            }
        }

        void OnDestroy()
        {
            if (environment != null)
                environment.EnvironmentLoaded -= OnEnvironmentLoaded;
            if (instance == this)
                instance = null;
        }

        void OnEnvironmentLoaded(EnvironmentId id)
        {
            if (id != EnvironmentId.Farm)
            {
                queue.Clear();
                showing = null;
                hud?.HideCard();
                return;
            }

            queue.Clear();
            queue.AddRange(GuidedTreatmentResults.Pending());
            total = queue.Count;
            if (total > 0)
                showAt = Time.unscaledTime + ShowDelay;
        }

        /// <summary>Re-reads the pending recaps and shows the first now (verification).</summary>
        public void ShowPendingNow()
        {
            queue.Clear();
            queue.AddRange(GuidedTreatmentResults.Pending());
            total = queue.Count;
            ShowNext();
        }

        void ShowNext()
        {
            if (hud == null)
                hud = GuidedHud.Create(transform);
            while (queue.Count > 0)
            {
                var id = queue[0];
                var record = GuidedTreatmentResults.Load(id);
                if (record == null)
                {
                    queue.RemoveAt(0);
                    GuidedTreatmentResults.Acknowledge(id);
                    continue;
                }

                showing = id;
                var index = total - queue.Count + 1;
                var plant = string.IsNullOrEmpty(record.plantName) ? record.plantId : record.plantName;
                var disease = string.IsNullOrEmpty(record.diseaseName) ? record.diseaseId : record.diseaseName;
                var body = string.Join("\n", record.Lines()) + $"\n<size=80%><color=#B8C7D4>{disease} treated · specimen {id}</color></size>";
                hud.Recentre();
                hud.ShowCard(total > 1 ? $"{index}/{total}" : "", $"{plant} recovered", "Healthy again", body,
                    new GuidedHud.CardButton(queue.Count > 1 ? "Next" : "OK", _ => Acknowledge()));
                Debug.Log($"[BTP] Farm recap shown for {id}: {string.Join(" | ", record.Lines())}");
                return;
            }

            showing = null;
            hud.HideCard();
        }

        /// <summary>OK / Next: this plant's recap is done; the next treated plant's follows.</summary>
        public void Acknowledge()
        {
            if (showing == null)
                return;
            GuidedTreatmentResults.Acknowledge(showing);
            queue.Remove(showing);
            showing = null;
            hud.HideCard();
            if (queue.Count > 0)
                ShowNext();
        }
    }
}
