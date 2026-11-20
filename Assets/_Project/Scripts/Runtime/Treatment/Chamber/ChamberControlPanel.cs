using System;
using System.Collections.Generic;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Treatment
{
    /// <summary>
    /// The chamber's control panel (uGUI built by TreatmentChamberBuilder): a cyan header band with the page title, the
    /// four tabs "1 Temperature", "2 Fertiliser", "3 Spray", "4 Soil Moisture", two settings per page ([-] value [+], or
    /// [&lt;] option [&gt;] for water delivery) with their target band, the page status, and a footer with the readout,
    /// the status line, Apply and Check Health and the result. It edits a <see cref="TreatmentSettings"/> copy of the loaded
    /// profile; <see cref="ChamberStation"/> does the requests and gates the buttons.
    /// </summary>
    public sealed partial class ChamberControlPanel : MonoBehaviour
    {
        [Serializable]
        public sealed class Row
        {
            public GameObject root;
            public TMP_Text label;
            public Button minus;
            public TMP_Text minusLabel;
            public TMP_Text value;
            public Button plus;
            public TMP_Text plusLabel;
            public TMP_Text target;
            public TMP_Text feedback;
        }

        /// <summary>The Soil Moisture page of the WATER station (Addendum 3): water to add, current moisture, setpoint, target.</summary>
        [Serializable]
        public sealed class SoilPage
        {
            public GameObject root;
            public Button amountMinus;
            public Button amountPlus;
            public TMP_Text amount;
            public TMP_Text current;
            public TMP_Text setpoint;
            public TMP_Text target;
            public TMP_Text status;
            public TMP_Text leafWetness;
            public TMP_Text feedback;
        }

        /// <summary>The touchscreen's frame (Addendum 4): case header, connection indicator, menu, case page and modal.</summary>
        [Serializable]
        public sealed class Screen
        {
            public CanvasGroup body;
            public TMP_Text caseTitle;
            public TMP_Text caseScenario;
            public TMP_Text caseSpecimen;
            public Image connectionDot;
            public TMP_Text connectionLabel;
            public Button connectionButton;
            public Button menuButton;
            public GameObject menu;
            public Button menuCases;
            public Button menuClose;
            public GameObject casePage;
            public Button casePageClose;
            public GameObject pageRoot;
            public CanvasGroup modal;
            public TMP_Text modalTitle;
            public TMP_Text modalBody;
            public Button modalPrimary;
            public Button modalSecondary;
            /// <summary>Addendum 6 "Go to station" menu entries: 0 WATER CONTROL, 1 TREATMENT TOOLS, 2 plant chamber.</summary>
            public List<Button> goButtons = new List<Button>();
        }

        public enum Connection
        {
            Loading,
            Connected,
            Offline,
        }

        public static readonly string[] TabTitles = { "Temperature", "Fertiliser", "Spray", "Soil Moisture" };

        [SerializeField] TMP_Text header;
        [SerializeField] Button scenarioButton;
        [SerializeField] TMP_Text scenarioLabel;
        [SerializeField] List<Button> tabs = new List<Button>();
        [SerializeField] List<SelectedStateVisual> tabStates = new List<SelectedStateVisual>();
        [SerializeField] GameObject controlsRoot;
        [SerializeField] TMP_Text pageInfo;
        [SerializeField] List<Row> rows = new List<Row>();
        [SerializeField] TMP_Text pageNote;
        [SerializeField] TMP_Text pageStatus;
        [SerializeField] TMP_Text message;
        [SerializeField] Button retryButton;
        [SerializeField] TMP_Text readout;
        [SerializeField] TMP_Text statusLine;
        [SerializeField] Button applyButton;
        [SerializeField] Button checkButton;
        [SerializeField] TMP_Text resultHeadline;
        [SerializeField] TMP_Text resultFeedback;
        [SerializeField] SoilPage soil = new SoilPage();
        [SerializeField] Screen screen = new Screen();

        public enum State
        {
            Loading,
            Ready,
            Unavailable,
            Observation,
        }

        TreatmentProfile profile;
        TreatmentSettings settings;
        TreatmentSettings applied;
        TreatmentEvaluation evaluation;
        int page;
        bool canApply;
        bool canCheck;
        string busyText;
        string note;
        double measuredRootZone = double.NaN;
        bool wired;
        RootZoneWateringModel watering;

        public State Current { get; private set; } = State.Loading;
        public TreatmentProfile Profile => profile;
        public TreatmentSettings Settings => settings;
        public int Page => page;
        public bool ApplyInteractable => applyButton != null && applyButton.interactable;
        public bool CheckInteractable => checkButton != null && checkButton.interactable;

        public event Action<TreatmentSettings> ApplyRequested;
        public event Action CheckRequested;
        public event Action RetryRequested;
        public event Action<string> ScenarioRequested;
        /// <summary>Raised after a setting changed (factor ID), so the scene can preview it.</summary>
        public event Action<string> SettingChanged;
        public event Action<int> PageChanged;
        /// <summary>The WATER station's "Water to add" [-] (-1) / [+] (+1).</summary>
        public event Action<int> AmountStepRequested;

        internal void BindSoil(SoilPage page) => soil = page;

        internal void BindScreen(Screen frame) => screen = frame;

        /// <summary>Raised by the header's "Offline - Retry".</summary>
        public event Action ConnectionRetryRequested;

        /// <summary>A "Go to station" menu entry (0 WATER CONTROL, 1 TREATMENT TOOLS, 2 plant chamber).</summary>
        public event Action<int> GoToRequested;

        public IReadOnlyList<Button> GoButtons => screen?.goButtons;

        Action modalPrimaryAction;
        Action modalSecondaryAction;

        public bool ModalOpen => screen?.modal != null && screen.modal.gameObject.activeSelf;
        public bool MenuOpen => screen?.menu != null && screen.menu.activeSelf;
        public bool CasePageOpen => screen?.casePage != null && screen.casePage.activeSelf;
        public Connection ConnectionState { get; private set; } = Connection.Loading;
        public string ConnectionText => screen?.connectionLabel != null ? screen.connectionLabel.text : "";
        public string CaseTitleText => screen?.caseTitle != null ? screen.caseTitle.text : "";
        public string CaseScenarioText => screen?.caseScenario != null ? screen.caseScenario.text : "";
        public string ModalTitleText => screen?.modalTitle != null ? screen.modalTitle.text : "";
        public CanvasGroup Body => screen?.body;
        public Button ModalPrimary => screen?.modalPrimary;
        public Button ModalSecondary => screen?.modalSecondary;
        public Button MenuButton => screen?.menuButton;
        public Button MenuCasesButton => screen?.menuCases;

        /// <summary>The header: "Rice / Brown spot", the scenario line and the specimen ID.</summary>
        public void SetCase(string title, string scenarioLine, string specimen)
        {
            if (screen == null)
                return;
            if (screen.caseTitle != null)
                screen.caseTitle.text = title ?? "";
            if (screen.caseScenario != null)
            {
                screen.caseScenario.text = scenarioLine ?? "";
                screen.caseScenario.gameObject.SetActive(!string.IsNullOrEmpty(scenarioLine));
            }

            specimenLine = specimen;
            if (screen.caseSpecimen != null && handsNotice == null)
                screen.caseSpecimen.text = specimen ?? "";
        }

        /// <summary>The header's connection dot and label: Loading, Connected, or "Offline - Retry" (pressable).</summary>
        public void SetConnection(Connection state)
        {
            ConnectionState = state;
            if (screen == null)
                return;
            var color = state == Connection.Connected ? UIPalette.HealthGood : state == Connection.Offline ? UIPalette.HealthBad : UIPalette.Warning;
            if (screen.connectionDot != null)
                screen.connectionDot.color = color;
            if (screen.connectionLabel != null)
                screen.connectionLabel.text = state == Connection.Connected ? "Connected" : state == Connection.Offline ? "Offline - Retry" : "Loading";
            if (screen.connectionButton != null)
                screen.connectionButton.interactable = state == Connection.Offline;
        }

        string specimenLine;
        string handsNotice;

        /// <summary>A short header notice ("Hands out of view") in place of the specimen line; null restores the line.</summary>
        public void ShowHandsNotice(string notice)
        {
            handsNotice = notice;
            if (screen?.caseSpecimen == null)
                return;
            screen.caseSpecimen.text = notice ?? specimenLine ?? "";
            screen.caseSpecimen.color = notice != null ? UIPalette.Warning : UIPalette.TextSecondary;
        }

        public string HandsNoticeText => handsNotice;

        public void ShowMenu(bool open)
        {
            if (screen?.menu == null)
                return;
            screen.menu.SetActive(open && !ModalOpen);
            ApplyBlocking();
        }

        /// <summary>The "Choose a Case" page (the case selector) in place of the treatment pages.</summary>
        public void ShowCasePage(bool open)
        {
            if (screen?.casePage == null)
                return;
            screen.casePage.SetActive(open);
            if (screen.pageRoot != null)
                screen.pageRoot.SetActive(!open);
            if (open && screen.menu != null)
                screen.menu.SetActive(false);
            ApplyBlocking();
        }

        /// <summary>One modal on the touchscreen; everything behind it stops taking input until it is answered.</summary>
        public void ShowModal(string title, string body, string primary, Action onPrimary, string secondary, Action onSecondary)
        {
            if (screen?.modal == null)
                return;
            modalPrimaryAction = onPrimary;
            modalSecondaryAction = onSecondary;
            screen.modalTitle.text = title ?? "";
            screen.modalBody.text = body ?? "";
            SetLabel(screen.modalPrimary, primary);
            SetLabel(screen.modalSecondary, secondary);
            if (screen.menu != null)
                screen.menu.SetActive(false);
            screen.modal.gameObject.SetActive(true);
            screen.modal.interactable = true;
            screen.modal.blocksRaycasts = true;
            ApplyBlocking();
        }

        public void HideModal()
        {
            if (screen?.modal == null)
                return;
            screen.modal.gameObject.SetActive(false);
            modalPrimaryAction = modalSecondaryAction = null;
            ApplyBlocking();
        }

        static void SetLabel(Button button, string label)
        {
            if (button == null)
                return;
            button.gameObject.SetActive(!string.IsNullOrEmpty(label));
            var text = button.GetComponentInChildren<TMP_Text>();
            if (text != null)
                text.text = label ?? "";
        }

        void ApplyBlocking()
        {
            if (screen?.body == null)
                return;
            var blocked = ModalOpen;
            screen.body.interactable = !blocked;
            screen.body.blocksRaycasts = !blocked;
        }

        /// <summary>Presses the modal's buttons (verification; the buttons call the same).</summary>
        public void PressModal(bool primary)
        {
            var action = primary ? modalPrimaryAction : modalSecondaryAction;
            HideModal();
            action?.Invoke();
        }

        /// <summary>The watering model the Soil Moisture page shows (null: the generic two-row page).</summary>
        public void SetWatering(RootZoneWateringModel model)
        {
            watering = model;
            if (Current == State.Ready)
                Refresh();
        }

        public RootZoneWateringModel Watering => watering;

        internal void Bind(TMP_Text headerText, Button scenario, TMP_Text scenarioText, List<Button> tabButtons, List<SelectedStateVisual> tabVisuals,
            GameObject controls, TMP_Text info, List<Row> settingRows, TMP_Text noteText, TMP_Text statusText, TMP_Text messageText, Button retry,
            TMP_Text readoutText, TMP_Text status, Button apply, Button check, TMP_Text headline, TMP_Text feedback)
        {
            header = headerText; scenarioButton = scenario; scenarioLabel = scenarioText; tabs = tabButtons; tabStates = tabVisuals;
            controlsRoot = controls; pageInfo = info; rows = settingRows; pageNote = noteText; pageStatus = statusText; message = messageText;
            retryButton = retry; readout = readoutText; statusLine = status; applyButton = apply; checkButton = check; resultHeadline = headline;
            resultFeedback = feedback;
        }

        void Awake() => Wire();

        /// <summary>Connects the buttons (Awake; edit-mode tools call it themselves).</summary>
        public void Wire()
        {
            if (wired)
                return;
            wired = true;
            void On(Button button, Action action)
            {
                if (button == null)
                    return;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => Guarded(button, action));
            }

            for (var i = 0; i < tabs.Count; i++)
            {
                var index = i;
                On(tabs[i], () => ShowPage(index));
            }

            for (var r = 0; r < rows.Count; r++)
            {
                var index = r;
                On(rows[r].minus, () => Adjust(index, -1));
                On(rows[r].plus, () => Adjust(index, +1));
            }

            On(screen?.menuButton, () => ShowMenu(!MenuOpen));
            On(screen?.menuClose, () => ShowMenu(false));
            On(screen?.menuCases, () => ShowCasePage(true));
            On(screen?.casePageClose, () => ShowCasePage(false));
            On(screen?.modalPrimary, () => PressModal(true));
            On(screen?.modalSecondary, () => PressModal(false));
            On(screen?.connectionButton, () => ConnectionRetryRequested?.Invoke());
            if (screen?.goButtons != null)
                for (var i = 0; i < screen.goButtons.Count; i++)
                {
                    var index = i;
                    On(screen.goButtons[i], () =>
                    {
                        ShowMenu(false);
                        GoToRequested?.Invoke(index);
                    });
                }

            On(soil?.amountMinus, () => AmountStepRequested?.Invoke(-1));
            On(soil?.amountPlus, () => AmountStepRequested?.Invoke(+1));
            On(applyButton, PressApply);
            On(checkButton, PressCheck);
            On(retryButton, () => RetryRequested?.Invoke());
            On(scenarioButton, CycleScenario);
            // Play mode: the taller screen with the four-control table and physical keys (the saved scene is unchanged).
            if (Application.isPlaying)
                BuildTable();
        }

        /// <summary>
        /// One gesture presses a button once: a poke and the pinch ray reaching the same button in one gesture count once
        /// (<see cref="PressGuard"/>); every press is written to the hand-test log.
        /// </summary>
        static void Guarded(Button button, Action action)
        {
            if (!PressGuard.Shared.TryPress(button, Time.unscaledTime))
            {
                HandTestLog.Log("button:" + button.name, null, "ui", "duplicate ignored");
                return;
            }

            HandTestLog.Log("button:" + button.name, null, "ui", "pressed");
            action();
        }

        // ------------------------------------------------------------------ states

        public void ShowLoading(string caseTitle)
        {
            Current = State.Loading;
            profile = null;
            evaluation = null;
            applied = null;
            measuredRootZone = double.NaN;
            SetHeader("Loading");
            SetControls(false);
            SetMessage($"Loading the treatment profile for {caseTitle}...", UIPalette.TextSecondary, false);
            SetResult(null, null, Color.white);
            SetStatus("", UIPalette.TextSecondary);
            SetReadout("");
            SetGate(false, false, "Loading");
        }

        public void ShowUnavailable(string error)
        {
            Current = State.Unavailable;
            profile = null;
            SetHeader("Treatment unavailable");
            SetControls(false);
            SetMessage("The treatment profile could not be loaded.\n" + (error ?? "") + "\nThe specimen stays in the chamber; press Retry.",
                UIPalette.Warning, true);
            SetResult(null, null, Color.white);
            SetStatus("", UIPalette.TextSecondary);
            SetReadout("");
            SetGate(false, false, null);
        }

        public void ShowObservation(string plant, string disease)
        {
            Current = State.Observation;
            profile = null;
            SetHeader("Observation");
            SetControls(false);
            SetMessage($"Observation only - no treatment profile for {plant} / {disease}. Observe the specimen in the chamber.", UIPalette.TextSecondary, false);
            SetResult(null, null, Color.white);
            SetStatus("", UIPalette.TextSecondary);
            SetReadout("");
            SetGate(false, false, null);
        }

        /// <summary>Shows a loaded profile with <paramref name="current"/> settings (and <paramref name="restoredApplied"/> when this specimen had some).</summary>
        public void ShowProfile(TreatmentProfile loaded, TreatmentSettings current, TreatmentSettings restoredApplied, string restoredNote)
        {
            Current = State.Ready;
            profile = loaded;
            settings = current ?? TreatmentSettings.FromMinimum(loaded);
            applied = restoredApplied?.Clone();
            evaluation = null;
            note = restoredNote;
            measuredRootZone = double.NaN;
            SetControls(true);
            SetMessage("", UIPalette.TextSecondary, false);
            SetResult(null, null, Color.white);
            if (scenarioButton != null)
                scenarioButton.gameObject.SetActive(loaded.Scenarios.Count > 1);
            if (scenarioLabel != null)
                scenarioLabel.text = Title(loaded.Scenario);
            Refresh();
        }

        static string Title(string id) => string.IsNullOrEmpty(id) ? "" : char.ToUpperInvariant(id[0]) + id.Substring(1);

        /// <summary>Which buttons the station allows now (and why not, while a request runs).</summary>
        public void SetGate(bool allowApply, bool allowCheck, string busy)
        {
            canApply = allowApply;
            canCheck = allowCheck;
            busyText = busy;
            if (applyButton != null)
            {
                applyButton.gameObject.SetActive(Current == State.Ready || Current == State.Loading);
                // The Apply gate: the station allows it AND all four controls read their chart targets.
                applyButton.interactable = allowApply && Current == State.Ready && AllControlsDone;
            }

            if (checkButton != null)
            {
                checkButton.gameObject.SetActive(Current == State.Ready || Current == State.Loading);
                checkButton.interactable = allowCheck && Current == State.Ready;
            }

            if (Current == State.Ready)
                RefreshStatusLine();
        }

        /// <summary>The settings were applied (the station captured them): the status line and the page follow.</summary>
        public void ShowApplied(TreatmentSettings appliedSettings, string appliedNote)
        {
            applied = appliedSettings?.Clone();
            evaluation = null;
            note = appliedNote;
            SetResult(null, null, Color.white);
            Refresh();
        }

        public void ShowEvaluation(TreatmentEvaluation result)
        {
            evaluation = result;
            Refresh();
        }

        string recoveryText;
        bool recoveryDone;

        /// <summary>
        /// The chamber specimen's recovery line shown after Apply ("Recovering... 45%", "Plant recovered"); null clears it
        /// (a new specimen). Only the status line is redrawn.
        /// </summary>
        public void SetRecoveryStatus(string text, bool recovered)
        {
            if (text == recoveryText && recovered == recoveryDone)
                return;
            recoveryText = text;
            recoveryDone = recovered;
            if (Current == State.Ready)
                RefreshStatusLine();
        }

        public string RecoveryStatusText => recoveryText ?? "";

        /// <summary>A short message under the controls (the 409 reload request, network errors of Check Health).</summary>
        public void ShowNote(string text, bool warning)
        {
            note = text;
            Refresh();
            if (statusLine != null && warning)
                statusLine.color = UIPalette.Warning;
        }

        /// <summary>The root-zone moisture the soil has now (rises while watering), for the Soil Moisture page status.</summary>
        public void SetMeasuredRootZone(double value)
        {
            measuredRootZone = value;
            if (Current == State.Ready)
            {
                RefreshPage();
                RefreshTable();
                RefreshApplyGate();
            }
        }

        // ------------------------------------------------------------------ interaction

        public void ShowPage(int index)
        {
            page = Mathf.Clamp(index, 0, TabTitles.Length - 1);
            if (Current == State.Ready)
                Refresh();
            PageChanged?.Invoke(page);
        }

        /// <summary>Presses - (-1) or + (+1) on row <paramref name="row"/> of the current page.</summary>
        public bool Adjust(int row, int direction)
        {
            if (profile == null || row < 0 || row > 1)
                return false;
            var (factorId, subs) = TreatmentProfile.Layout[page];
            var changed = settings.Step(profile, factorId, subs[row], direction);
            if (changed)
            {
                Refresh();
                SettingChanged?.Invoke(factorId);
            }

            return changed;
        }

        /// <summary>Sets a value directly (verification and tests); snapped and clamped like the buttons.</summary>
        public void SetValue(string factorId, string subId, double value)
        {
            var sub = profile?.Find(factorId, subId);
            if (sub == null)
                return;
            settings.Set(sub, factorId, value);
            Refresh();
            SettingChanged?.Invoke(factorId);
        }

        public void SetChoice(string factorId, string subId, string value)
        {
            var sub = profile?.Find(factorId, subId);
            if (sub == null || sub.IndexOfOption(value) < 0)
                return;
            settings.SetChoice(factorId, subId, value);
            Refresh();
            SettingChanged?.Invoke(factorId);
        }

        public void PressApply()
        {
            if (Current != State.Ready || !canApply || (!guidedApply && !AllControlsDone))
                return;
            ApplyRequested?.Invoke(settings.Clone());
        }

        public void PressCheck()
        {
            if (Current != State.Ready || !canCheck)
                return;
            CheckRequested?.Invoke();
        }

        void CycleScenario()
        {
            if (profile == null || profile.Scenarios.Count < 2)
                return;
            var index = profile.Scenarios.IndexOf(profile.Scenario);
            ScenarioRequested?.Invoke(profile.Scenarios[(index + 1) % profile.Scenarios.Count]);
        }

        // ------------------------------------------------------------------ rendering

        static string RowLabel(string factorId, TreatmentSubfactor sub)
        {
            switch (factorId + "." + sub.Id)
            {
                case "temperature.day": return "Day";
                case "temperature.night": return "Night";
                case "fertiliser.dose": return "Supplement amount";
                case "fertiliser.applications": return "Number of applications";
                case "spray.dose": return "Spray amount";
                case "spray.targetCoverage": return "Target coverage";
                case "soilMoisture.rootZone": return "Root-zone moisture";
                case "soilMoisture.irrigation": return "Water delivery";
                default: return sub.Label;
            }
        }

        public void Refresh()
        {
            if (profile == null)
                return;
            SyncSoilFromWatering();
            SetHeader(TabTitles[page]);
            for (var i = 0; i < tabs.Count && i < tabStates.Count; i++)
                tabStates[i].Selected = i == page;
            RefreshTabs();
            RefreshPage();
            RefreshStatusLine();
            RefreshResult();
            RefreshTable();
            RefreshApplyGate();
        }

        void RefreshTabs()
        {
            for (var i = 0; i < tabs.Count; i++)
            {
                var text = tabs[i].GetComponentInChildren<TMP_Text>();
                if (text == null)
                    continue;
                // After a check each tab is tinted by its control's score: green on target, amber to review.
                var title = $"{i + 1} {TabTitles[i]}";
                var factorId = TreatmentProfile.Layout[i].factor;
                if (evaluation != null && !double.IsNaN(evaluation.FactorScore(factorId)))
                    title = evaluation.FactorScore(factorId) >= 0.999 ? $"<color=#34D399>{title}</color>" : $"<color=#FBBF24>{title}</color>";
                text.text = title;
            }
        }

        bool SoilPageActive => watering != null && page == 3 && soil != null && soil.root != null;

        void RefreshPage()
        {
            var soilPage = SoilPageActive;
            if (soil != null && soil.root != null)
                soil.root.SetActive(soilPage && Current == State.Ready);
            if (controlsRoot != null)
                controlsRoot.SetActive(!soilPage && Current == State.Ready);
            if (soilPage)
            {
                RefreshSoil();
                RefreshReadout();
                return;
            }

            var (factorId, subs) = TreatmentProfile.Layout[page];
            var factor = profile.Find(factorId);
            for (var r = 0; r < rows.Count && r < subs.Length; r++)
            {
                var row = rows[r];
                var sub = factor?.Find(subs[r]);
                if (sub == null)
                {
                    row.root.SetActive(false);
                    continue;
                }

                row.root.SetActive(true);
                row.label.text = RowLabel(factorId, sub);
                if (sub.IsChoice)
                {
                    row.value.text = sub.ShortLabel(settings.GetChoice(factorId, sub.Id));
                    if (row.minusLabel != null)
                        row.minusLabel.text = "<";
                    if (row.plusLabel != null)
                        row.plusLabel.text = ">";
                    row.target.text = "";
                }
                else
                {
                    row.value.text = sub.Format(settings.Get(factorId, sub.Id));
                    if (row.minusLabel != null)
                        row.minusLabel.text = "−";
                    if (row.plusLabel != null)
                        row.plusLabel.text = "+";
                    row.target.text = sub.TargetText;
                }

                row.minus.interactable = settings.CanStep(profile, factorId, sub.Id, -1);
                row.plus.interactable = settings.CanStep(profile, factorId, sub.Id, +1);
                var verdict = evaluation?.Find(factorId, sub.Id);
                if (row.feedback != null)
                {
                    row.feedback.text = verdict?.Feedback ?? "";
                    row.feedback.color = verdict != null && verdict.OnTarget ? UIPalette.HealthGood : UIPalette.HealthBad;
                }
            }

            if (pageInfo != null)
                pageInfo.text = InfoText(factor);
            var (status, good) = PageStatus(factorId, factor);
            if (pageNote != null)
                pageNote.text = NoteText(factorId, factor);
            if (pageStatus != null)
            {
                pageStatus.text = status;
                pageStatus.color = good ? UIPalette.HealthGood : UIPalette.Warning;
            }

            RefreshReadout();
        }

        /// <summary>Water to add / Current moisture / Setpoint / Target range / status / leaf wetness, from the model.</summary>
        void RefreshSoil()
        {
            var sub = profile.Find(TreatmentProfile.SoilMoisture, "rootZone");
            string Format(double v) => sub != null ? sub.Format(v) : v.ToString("0");
            soil.amount.text = RootZoneWateringModel.AmountText(watering.Amount, Format);
            soil.current.text = Format(watering.Current);
            soil.setpoint.text = Format(watering.Setpoint);
            soil.target.text = sub != null
                ? TreatmentSubfactor.Compact(sub.TargetMin) + "\u2013" + TreatmentSubfactor.Compact(sub.TargetMax) + (sub.Unit ?? "")
                : "";
            soil.status.text = watering.StatusText(Format);
            switch (watering.Status)
            {
                case RootZoneWateringModel.State.Watering:
                case RootZoneWateringModel.State.Draining:
                    soil.status.color = UIPalette.Cyan;
                    break;
                case RootZoneWateringModel.State.ReachedOnTarget:
                    soil.status.color = UIPalette.HealthGood;
                    break;
                case RootZoneWateringModel.State.ReachedOutsideTarget:
                    soil.status.color = UIPalette.Warning;
                    break;
                case RootZoneWateringModel.State.Paused:
                    soil.status.color = Color.white;
                    break;
                default:
                    soil.status.color = UIPalette.TextSecondary;
                    break;
            }

            var running = watering.Running;
            soil.amountMinus.interactable = !running && watering.Amount - watering.Step >= watering.MinAmount - 1e-9;
            soil.amountPlus.interactable = !running && watering.Amount + watering.Step <= watering.MaxAmount + 1e-9;
            soil.leafWetness.text = SoilMoistureRules.LeafWetnessText(LeafWetness);
            if (soil.feedback != null)
            {
                var verdict = evaluation?.Find(TreatmentProfile.SoilMoisture, "rootZone");
                soil.feedback.text = verdict?.Feedback ?? "";
                soil.feedback.color = verdict != null && verdict.OnTarget ? UIPalette.HealthGood : UIPalette.HealthBad;
            }

            if (pageStatus != null)
                pageStatus.text = "";
        }

        string InfoText(TreatmentFactor factor)
        {
            if (factor == null)
                return "";
            switch (factor.Id)
            {
                case TreatmentProfile.Fertiliser:
                    var head = string.IsNullOrWhiteSpace(factor.Nutrient) ? "No supplement for this case." : "Confirmed: " + factor.Nutrient.Trim();
                    return $"<b>{head}</b>\n{factor.Evidence}";
                case TreatmentProfile.Spray:
                    return factor.Indicated && !string.IsNullOrEmpty(factor.Target) ? "Marked target: " + factor.Target : "Not indicated for this case";
                default:
                    return "";
            }
        }

        string NoteText(string factorId, TreatmentFactor factor)
        {
            if (factorId == TreatmentProfile.SoilMoisture)
                return SoilMoistureRules.LeafWetnessText(LeafWetness);
            if (factor == null)
                return "";
            var steps = new List<string>();
            foreach (var sub in factor.Subfactors)
            {
                if (sub.IsChoice)
                    continue;
                var text = sub.StepText;
                if (!steps.Contains(text))
                    steps.Add(text);
            }

            return string.Join("   ", steps);
        }

        /// <summary>Leaf wetness: the server value after a check, else the local preview for the chosen delivery.</summary>
        public double LeafWetness
        {
            get
            {
                if (evaluation != null && !double.IsNaN(evaluation.LeafWetness))
                    return evaluation.LeafWetness;
                var delivery = watering != null ? watering.Irrigation : settings?.GetChoice(TreatmentProfile.SoilMoisture, "irrigation") ?? SoilMoistureRules.Hold;
                return SoilMoistureRules.PreviewLeafWetness(profile != null ? profile.LeafWetnessStart : 0d, delivery);
            }
        }

        /// <summary>The root-zone moisture the soil has: the server's after a check, the watered value, else the start.</summary>
        public double SoilRootZone
        {
            get
            {
                if (evaluation != null && !double.IsNaN(evaluation.RootZoneMoisture))
                    return evaluation.RootZoneMoisture;
                if (watering != null)
                    return watering.Current;
                if (!double.IsNaN(measuredRootZone))
                    return measuredRootZone;
                var sub = profile?.Find(TreatmentProfile.SoilMoisture, "rootZone");
                return sub != null ? sub.Start : 0d;
            }
        }

        (string text, bool good) PageStatus(string factorId, TreatmentFactor factor)
        {
            if (factor == null)
                return ("", false);
            if (factorId == TreatmentProfile.SoilMoisture)
            {
                var rootZone = factor.Find("rootZone");
                var irrigation = factor.Find("irrigation");
                var verdict = SoilMoistureRules.Status(rootZone, irrigation, SoilRootZone, settings.GetChoice(factorId, "irrigation"));
                return (SoilMoistureRules.StatusText(verdict), verdict == SoilMoistureRules.Verdict.OnTarget);
            }

            foreach (var sub in factor.Subfactors)
            {
                if (sub.IsChoice)
                    continue;
                var value = settings.Get(factorId, sub.Id);
                if (sub.OnTarget(value))
                    continue;
                if (sub.TargetMin <= 0d && sub.TargetMax <= 0d)
                    return (factorId == TreatmentProfile.Fertiliser ? "This case calls for no supplement" : "This case calls for none", false);
                return (value < sub.TargetMin ? "Below target" : "Above target", false);
            }

            return ("On target", true);
        }

        void RefreshReadout()
        {
            if (profile == null)
                return;
            var rootZone = profile.Find(TreatmentProfile.SoilMoisture, "rootZone");
            var soil = rootZone != null ? rootZone.Format(SoilRootZone) : "";
            var source = evaluation != null ? "measured" : "preview";
            SetReadout($"{SoilMoistureRules.LeafWetnessText(LeafWetness)} ({source})   Root zone {soil}");
        }

        void RefreshStatusLine()
        {
            if (statusLine == null || profile == null)
                return;
            string text;
            var color = UIPalette.TextSecondary;
            if (!string.IsNullOrEmpty(busyText))
                text = busyText + "...";
            else if ((applied == null || !applied.SameAs(settings)) && !AllControlsDone)
            {
                // The Apply gate's hint: how many controls already read their chart targets.
                text = TreatmentGate.Hint(ControlsDone);
                color = UIPalette.Warning;
            }
            else if (applied == null)
            {
                text = "All 4 controls on target - press Apply";
                color = UIPalette.HealthGood;
            }
            else if (!applied.SameAs(settings))
            {
                text = "Settings changed - press Apply";
                color = UIPalette.Warning;
            }
            else if (!string.IsNullOrEmpty(recoveryText))
            {
                // The chamber specimen's recovery after Apply: where to look, then how far it has got.
                text = recoveryText + (recoveryDone && evaluation == null ? " - press Check Health" : "");
                color = recoveryDone ? UIPalette.HealthGood : UIPalette.Cyan;
            }
            else
            {
                text = evaluation != null ? "Applied and checked" : "Applied - press Check Health";
                color = UIPalette.Cyan;
            }

            if (!string.IsNullOrEmpty(note))
                text += "\n" + note;
            statusLine.text = text;
            statusLine.color = color;
        }

        void RefreshResult()
        {
            if (evaluation == null)
            {
                SetResult(null, null, Color.white);
                return;
            }

            var color = evaluation.Passed ? UIPalette.HealthGood : UIPalette.Warning;
            SetResult($"{evaluation.Headline} - Score {evaluation.ScorePercent}", evaluation.Feedback, color);
        }

        // ------------------------------------------------------------------ small helpers

        void SetHeader(string text)
        {
            if (header != null)
                header.text = text;
        }

        void SetControls(bool visible)
        {
            if (!visible && soil != null && soil.root != null)
                soil.root.SetActive(false);
            if (controlsRoot != null)
                controlsRoot.SetActive(visible);
            foreach (var tab in tabs)
                if (tab != null)
                    tab.gameObject.SetActive(visible);
            if (!visible && scenarioButton != null)
                scenarioButton.gameObject.SetActive(false);
            SetTableVisible(visible);
        }

        void SetMessage(string text, Color color, bool retry)
        {
            if (message != null)
            {
                message.gameObject.SetActive(!string.IsNullOrEmpty(text));
                message.text = text ?? "";
                message.color = color;
            }

            if (retryButton != null)
                retryButton.gameObject.SetActive(retry);
        }

        void SetResult(string headline, string feedback, Color color)
        {
            if (resultHeadline != null)
            {
                resultHeadline.text = headline ?? "";
                resultHeadline.color = color;
            }

            if (resultFeedback != null)
                resultFeedback.text = feedback ?? "";
            // The table layout shares one line between the readout and the result.
            if (tableBuilt && readout != null)
                readout.gameObject.SetActive(string.IsNullOrEmpty(headline));
        }

        void SetStatus(string text, Color color)
        {
            if (statusLine == null)
                return;
            statusLine.text = text;
            statusLine.color = color;
        }

        void SetReadout(string text)
        {
            if (readout != null)
                readout.text = text;
        }

        /// <summary>The visible texts of the current page in reading order (verification of the exact copy).</summary>
        public List<string> VisiblePageTexts()
        {
            var texts = new List<string>();
            if (header != null)
                texts.Add(header.text);
            if (SoilPageActive && soil.root.activeSelf)
            {
                texts.AddRange(new[]
                {
                    "Water to add", soil.amount.text, "Current moisture", soil.current.text, "Setpoint", soil.setpoint.text, "Target range",
                    soil.target.text, soil.status.text, soil.leafWetness.text,
                });
                return texts;
            }

            foreach (var row in rows)
            {
                if (row.root == null || !row.root.activeSelf)
                    continue;
                texts.Add(row.label.text);
                texts.Add(row.value.text);
                if (!string.IsNullOrEmpty(row.target.text))
                    texts.Add(row.target.text);
            }

            if (pageNote != null && !string.IsNullOrEmpty(pageNote.text))
                texts.Add(pageNote.text);
            if (pageStatus != null && !string.IsNullOrEmpty(pageStatus.text))
                texts.Add(pageStatus.text);
            return texts;
        }

        public string PageStatusText => SoilPageActive ? soil.status.text : pageStatus != null ? pageStatus.text : "";
        public Color PageStatusColor => SoilPageActive ? soil.status.color : pageStatus != null ? pageStatus.color : Color.clear;
        public string PageInfoText => pageInfo != null ? pageInfo.text : "";
        public string MessageText => message != null ? message.text : "";
        public string StatusLineText => statusLine != null ? statusLine.text : "";
        public string ResultText => (resultHeadline != null ? resultHeadline.text : "") + " | " + (resultFeedback != null ? resultFeedback.text : "");
    }
}
