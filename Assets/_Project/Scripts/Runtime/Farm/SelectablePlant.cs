using BTP.Core;
using BTP.Plants;
using BTP.Treatment;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace BTP.Farm
{
    /// <summary>
    /// A selectable Farm planting: either one labelled specimen, or a disease row (one plant type showing one
    /// disease), where every plant carries its own collider. Controller ray or hand pinch selects it: the plant, and
    /// for a row its disease, become the shared selection, so Enter Lab opens on that disease's leaf. The ground ring
    /// lights up and the name card opens over the plant that was pointed at (a row has one ring and one card, which
    /// move to that plant); hover brightens the ring so users can see what they are pointing at.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class SelectablePlant : MonoBehaviour
    {
        [SerializeField] string plantId;
        [Tooltip("The disease this row shows. Empty: a plant specimen (selecting it picks the plant's default disease).")]
        [SerializeField] string diseaseId;
        [Tooltip("A row's plants, each with its own collider. Empty: this object is the one plant.")]
        [SerializeField] Transform[] plants = new Transform[0];
        [SerializeField] SelectionRing ring;
        [SerializeField] PlantNameCard nameCard;
        [SerializeField] string selectedDetail = "Selected. Press Enter Lab on the notice board.";
        [SerializeField] DiseaseGalleryController gallery;

        XRSimpleInteractable interactable;
        string defaultDetail;
        bool hovered;
        bool selected;
        Transform focus;
        Vector3 ringOffset;
        Vector3 cardOffset;

        public string PlantId => plantId;
        public string DiseaseId => diseaseId;
        public bool IsRow => plants != null && plants.Length > 0;
        /// <summary>A row's plants (empty for a specimen).</summary>
        public Transform[] Plants => plants;

        internal void Bind(string id, SelectionRing selectionRing, PlantNameCard card, DiseaseGalleryController diseaseGallery = null)
        {
            plantId = id;
            ring = selectionRing;
            nameCard = card;
            gallery = diseaseGallery;
        }

        /// <summary>Makes this a disease row of <paramref name="rowPlants"/> (each with a collider).</summary>
        internal void BindRow(string disease, Transform[] rowPlants)
        {
            diseaseId = disease;
            plants = rowPlants;
        }

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            defaultDetail = selectedDetail;
            focus = IsRow ? plants[0] : transform;
            // The ring and card are built around the row's first plant; they keep that offset from the focused plant.
            if (ring != null)
                ringOffset = ring.transform.position - focus.position;
            if (nameCard != null)
            {
                cardOffset = nameCard.transform.position - focus.position;
                // Treat Plant (off by default): carries this exact plant (specimen, case and look) straight into the
                // Treatment lab. The Farm card shows text only; treatment is entered through Enter Lab instead.
                if (showTreatPlantButton)
                    nameCard.SetAction(TreatPlantLabel, TreatFocusedPlant);
            }
        }

        public const string TreatPlantLabel = "Treat Plant";

        [Tooltip("Adds a \"Treat Plant\" button under the selected card that jumps straight to the Treatment lab. Off: the card shows text only.")]
        [SerializeField] bool showTreatPlantButton;

        /// <summary>
        /// Treat Plant on the selected card: selects the plant the card is on (if it is not already the selected specimen)
        /// and switches to the Treatment lab, where the chamber shows this specimen.
        /// </summary>
        public void TreatFocusedPlant()
        {
            var state = SelectionState.Instance;
            if (state != null)
            {
                var specimen = SpecimenAt(IndexOf(focus));
                if (!specimen.Equals(state.SelectedSpecimen))
                    state.SelectSpecimen(specimen);
            }

            var controller = EnvironmentController.Instance;
            if (controller == null)
            {
                Debug.LogError("[BTP] No EnvironmentController is loaded; start from XRBootstrap.", this);
                return;
            }

            controller.EnterTreatment();
        }

        void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.firstHoverEntered.AddListener(OnFirstHoverEntered);
            interactable.lastHoverExited.AddListener(OnLastHoverExited);

            // Treatment results saved for this planting's specimens show on those plants only.
            SpecimenTreatmentResults.ApplyToFarm(this);

            if (SelectionState.Instance == null)
                return;
            SelectionState.Instance.CaseChanged += OnCaseChanged;
            SelectionState.Instance.SpecimenChanged += OnSpecimenChanged;
            OnCaseChanged(SelectionState.Instance.Current?.PlantId, SelectionState.Instance.CurrentDiseaseId);
            // Back from the Treatment lab: the plant that was treated is still the selected one.
            OnSpecimenChanged(SelectionState.Instance.SelectedSpecimen);
            // After the Lab's six weeks: the treated plant is shown healed (and highlighted the first time).
            RecoveredPlantHighlight.ApplyTo(this);
        }

        void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.firstHoverEntered.RemoveListener(OnFirstHoverEntered);
            interactable.lastHoverExited.RemoveListener(OnLastHoverExited);
            if (SelectionState.Instance != null)
            {
                SelectionState.Instance.CaseChanged -= OnCaseChanged;
                SelectionState.Instance.SpecimenChanged -= OnSpecimenChanged;
            }
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            MoveTo(PlantFor(args.interactorObject));
            var state = SelectionState.Instance;
            if (state != null)
            {
                // The exact plant is remembered, so the Treatment lab shows this specimen, not just this crop.
                if (!state.SelectSpecimen(SpecimenAt(IndexOf(focus))) && (string.IsNullOrEmpty(diseaseId) || !state.SelectCase(plantId, diseaseId)))
                    state.SelectPlant(plantId);
            }

            // Selecting also opens the disease ring around the plant when the viewer is near; the selection is unchanged.
            if (gallery == null)
                return;
            var bounds = focus.GetComponent<Collider>();
            var extent = bounds != null ? Mathf.Max(bounds.bounds.extents.x, bounds.bounds.extents.z) : 0.5f;
            if (!gallery.HandlePlantClicked(plantId, focus, extent))
                ShowHint();
        }

        void Update()
        {
            // While a row is pointed at (and not selected), the hover ring follows the pointed-at plant.
            if (!hovered || selected || !IsRow || interactable.interactorsHovering.Count == 0)
                return;
            var plant = PlantFor(interactable.interactorsHovering[0]);
            if (plant != focus)
                MoveTo(plant);
        }

        /// <summary>The plant the ring and card are on.</summary>
        public Transform Focus => focus;

        /// <summary>The index of <paramref name="plant"/> in this planting (0 for a specimen; -1 when it is not one of its plants).</summary>
        public int IndexOf(Transform plant)
        {
            if (!IsRow)
                return plant == transform ? 0 : -1;
            for (var i = 0; i < plants.Length; i++)
                if (plants[i] == plant)
                    return i;
            return -1;
        }

        /// <summary>The specimen identity of this planting's plant at <paramref name="index"/>.</summary>
        public SpecimenRef SpecimenAt(int index) => FarmSpecimenIds.Ref(plantId, diseaseId, Mathf.Max(0, index));

        /// <summary>Whether <paramref name="specimen"/> is one of this planting's plants.</summary>
        public bool Owns(SpecimenRef specimen) =>
            specimen != null && specimen.PlantId == plantId && (string.IsNullOrEmpty(diseaseId) || specimen.DiseaseId == diseaseId) &&
            PlantAt(specimen.PlantIndex) != null;

        void OnSpecimenChanged(SpecimenRef specimen)
        {
            if (!Owns(specimen))
                return;
            if (IsRow)
                FocusPlant(specimen.PlantIndex);
            var result = SpecimenTreatmentResults.Describe(specimen);
            SetSelectedDetail(result != null ? result + "\n" + defaultDetail : null);
        }

        /// <summary>A row's plant by index (the specimen itself for index 0), or null.</summary>
        public Transform PlantAt(int index) => !IsRow ? (index == 0 ? transform : null) : index >= 0 && index < plants.Length ? plants[index] : null;

        /// <summary>Moves the ring and card to one plant of the row (a connected session's specimen) and refreshes them.</summary>
        public bool FocusPlant(int index)
        {
            var plant = PlantAt(index);
            if (plant == null)
                return false;
            MoveTo(plant);
            Refresh();
            return true;
        }

        /// <summary>Replaces the selected card's second line (null restores the default "Press Enter Lab" hint).</summary>
        public void SetSelectedDetail(string detail)
        {
            selectedDetail = string.IsNullOrEmpty(detail) ? defaultDetail : detail;
            Refresh();
        }

        /// <summary>The row plant an interactor points at: its ray's hit collider, else the plant nearest its ray end.</summary>
        Transform PlantFor(IXRInteractor interactor)
        {
            if (!IsRow)
                return transform;
            if (interactor is XRRayInteractor ray && ray.TryGetCurrent3DRaycastHit(out var hit) && hit.collider != null)
            {
                foreach (var plant in plants)
                {
                    if (plant != null && hit.collider.transform.IsChildOf(plant))
                        return plant;
                }
            }

            var point = interactor is ICurveInteractionDataProvider curve && curve.TryGetCurveEndPoint(out var end) != EndPointType.None
                ? end
                : interactor.transform.position;
            return Nearest(point);
        }

        Transform Nearest(Vector3 point)
        {
            Transform best = focus;
            var bestDistance = float.MaxValue;
            foreach (var plant in plants)
            {
                if (plant == null)
                    continue;
                var offset = plant.position - point;
                offset.y = 0f;
                if (offset.sqrMagnitude < bestDistance)
                {
                    bestDistance = offset.sqrMagnitude;
                    best = plant;
                }
            }

            return best;
        }

        void MoveTo(Transform plant)
        {
            if (plant == null || !IsRow)
                return;
            focus = plant;
            if (ring != null)
                ring.transform.position = plant.position + ringOffset;
            if (nameCard != null)
                nameCard.transform.position = plant.position + cardOffset;
        }

        void ShowHint()
        {
            if (nameCard == null)
                return;
            nameCard.Show(Title(), DiseaseGalleryController.HintText);
            CancelInvoke(nameof(Refresh));
            Invoke(nameof(Refresh), 2.5f);
        }

        void OnFirstHoverEntered(HoverEnterEventArgs args)
        {
            hovered = true;
            if (!selected)
                MoveTo(PlantFor(args.interactorObject));
            Refresh();
        }

        void OnLastHoverExited(HoverExitEventArgs args)
        {
            hovered = false;
            Refresh();
        }

        void OnCaseChanged(string selectedPlant, string selectedDisease)
        {
            selected = Matches(selectedPlant, selectedDisease);
            Refresh();
        }

        /// <summary>Whether a selected (plant, disease) is this planting: a row matches its disease, a specimen any.</summary>
        internal bool Matches(string selectedPlant, string selectedDisease) =>
            !string.IsNullOrEmpty(selectedPlant) && selectedPlant == plantId &&
            (string.IsNullOrEmpty(diseaseId) || selectedDisease == diseaseId);

        /// <summary>"Tomato", or for a row "Tomato: Early Blight".</summary>
        string Title()
        {
            var plantName = plantId;
            var state = SelectionState.Instance;
            if (state != null && state.Catalog != null && state.Catalog.TryGet(plantId, out var plant))
                plantName = plant.DisplayName;
            if (string.IsNullOrEmpty(diseaseId))
                return plantName;
            foreach (var info in DiseaseClasses.For(plantId))
            {
                if (info.DiseaseId == diseaseId)
                    return $"{plantName}: {info.DisplayName}";
            }

            return plantName;
        }

        void Refresh()
        {
            if (ring != null)
                ring.SetState(selected ? SelectionRing.State.Selected : hovered ? SelectionRing.State.Hover : SelectionRing.State.Idle);
            if (nameCard == null)
                return;
            if (selected)
                nameCard.Show(Title(), selectedDetail);
            else
                nameCard.Hide();
        }
    }
}
