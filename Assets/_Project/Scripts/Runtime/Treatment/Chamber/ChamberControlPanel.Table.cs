using System.Collections.Generic;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Treatment
{
    /// <summary>
    /// The kiosk's control table (built at runtime in Play mode by <see cref="Wire"/>; the saved scene keeps the page layout
    /// the builder made). The screen grows by <see cref="TableGrow"/> canvas units and the treatment page becomes one table
    /// of all four controls: Control (the existing tab buttons, each spanning its two rows, with a done tick) | Setting |
    /// [−] value [+] | Target | Status (a green tick or an amber dot). Rows are 88 units with 76-unit (about 6 cm) keys.
    /// The Soil Moisture rows follow the WATER station: [−]/[+] step "Water to add" and the delivery row is read-only.
    /// Below the table: the status line (with the Apply gate's hint), the readout or the result, Apply and Check Health.
    /// The builder's two-row page and Soil Moisture page stay in the scene, invisible and without input, so every existing
    /// reader (verification, tests) still finds them up to date.
    /// </summary>
    public sealed partial class ChamberControlPanel
    {
        /// <summary>Extra canvas height of the table layout (0.16 m at the kiosk's 0.8 mm per unit).</summary>
        public const float TableGrow = 200f;
        const float RowHeight = 88f, KeySize = 76f, TableTop = 56f, PageWidth = 1280f, PageHeight = 664f + TableGrow;
        static readonly Color Ink = new Color(0.04f, 0.1f, 0.13f);

        sealed class Cell
        {
            public int Control, Sub;
            public GameObject Root;
            public Image Band;
            public TMP_Text Label, Value, Target, Status;
            public Button Minus, Plus;
            public TMP_Text MinusLabel, PlusLabel;
            public GameObject Tick, Dot;
        }

        readonly List<Cell> cells = new List<Cell>();
        readonly List<GameObject> tabTicks = new List<GameObject>();
        RectTransform tableRoot;
        GameObject columnTitles;
        bool tableBuilt;

        /// <summary>True once the runtime table replaced the page layout (Play mode).</summary>
        public bool TableBuilt => tableBuilt;

        /// <summary>How many of the four controls read their chart target now.</summary>
        public int ControlsDone => profile == null ? 0 : TreatmentGate.DoneCount(profile, settings, watering);

        /// <summary>All four controls on target: the Apply gate's own condition.</summary>
        public bool AllControlsDone => profile != null && TreatmentGate.AllDone(profile, settings, watering);

        /// <summary>Whether control <paramref name="index"/> (0..3) reads its target (the tab's tick).</summary>
        public bool ControlDone(int index) => TreatmentGate.ControlDone(profile, settings, index, watering);

        static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static void Place(Component component, float x, float y, float width, float height)
        {
            if (component != null)
                Place((RectTransform)component.transform, x, y, width, height);
        }

        static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        TMP_Text NewText(Transform parent, string name, float size, float minSize, Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            var font = statusLine != null ? statusLine.font : header != null ? header.font : null;
            if (font != null)
                text.font = font;
            text.fontSize = size;
            text.enableAutoSizing = minSize < size;
            text.fontSizeMin = minSize;
            text.fontSizeMax = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.text = "";
            return text;
        }

        Image NewImage(Transform parent, string name, Color color, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1.4f;
            }

            return image;
        }

        /// <summary>A tick drawn with two rotated bars (the kiosk font has no check-mark glyph), in a square of <paramref name="size"/>.</summary>
        GameObject TickMark(RectTransform parent, float x, float y, float size, Color color)
        {
            var node = Place(Node("Tick", parent), x, y, size, size);
            void Arm(string name, Vector2 centre, float length, float angle)
            {
                var bar = NewImage(node, name, color, null);
                var rect = bar.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = centre * size;
                rect.sizeDelta = new Vector2(length * size, 0.16f * size);
                rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            Arm("Short", new Vector2(-0.215f, -0.085f), 0.42f, -45f);
            Arm("Long", new Vector2(0.15f, 0.04f), 0.72f, 48.5f);
            return node.gameObject;
        }

        Button CloneKey(Button template, Transform parent, string name, string label)
        {
            var go = Instantiate(template.gameObject, parent, false);
            go.name = name;
            go.SetActive(true);
            var button = go.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.interactable = true;
            var text = go.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.enableAutoSizing = true;
                text.fontSizeMin = 28f;
                text.fontSizeMax = 48f;
            }

            if (go.GetComponent<HoldRepeatButton>() == null)
                go.AddComponent<HoldRepeatButton>();
            return button;
        }

        /// <summary>Play mode: grows the screen and lays the treatment page out as the four-control table.</summary>
        void BuildTable()
        {
            if (tableBuilt || !Application.isPlaying)
                return;
            var pages = screen?.pageRoot != null ? screen.pageRoot.transform as RectTransform : controlsRoot != null ? controlsRoot.transform.parent as RectTransform : null;
            var template = rows.Count > 0 && rows[0].minus != null ? rows[0].minus : applyButton;
            if (pages == null || template == null || tabs.Count < TabTitles.Length)
                return;
            tableBuilt = true;

            // A taller screen: the canvas grows about its centre (top and bottom edges move 0.08 m each), the bezel follows.
            var canvasRect = (RectTransform)transform;
            canvasRect.sizeDelta += new Vector2(0f, TableGrow);
            foreach (var (name, margin) in new[] { ("Bezel", 44f), ("BezelLip", 22f) })
            {
                var part = transform.Find(name);
                if (part == null)
                    continue;
                part.localPosition = new Vector3(part.localPosition.x, 0f, part.localPosition.z);
                part.localScale = new Vector3(part.localScale.x, canvasRect.sizeDelta.y + margin, part.localScale.z);
            }

            pages.sizeDelta = new Vector2(pages.sizeDelta.x, PageHeight);
            if (screen?.casePage != null && screen.casePage.transform is RectTransform caseRect)
                caseRect.sizeDelta = new Vector2(caseRect.sizeDelta.x, caseRect.sizeDelta.y + TableGrow);

            // The builder's page layouts stay (up to date, invisible, no input); the table takes their place.
            foreach (var hidden in new[] { controlsRoot, soil?.root })
            {
                if (hidden == null)
                    continue;
                var group = hidden.GetComponent<CanvasGroup>();
                if (group == null)
                    group = hidden.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            var divider = pages.Find("Divider");
            if (divider != null)
                divider.gameObject.SetActive(false);
            var sprite = applyButton != null && applyButton.image != null ? applyButton.image.sprite : null;

            // Title band: the state ("Loading", "Observation") or, with a profile, the column titles.
            Place(pages.Find("HeaderBand"), 16f, 4f, PageWidth - 32f, 48f);
            if (header != null)
            {
                Place(header, 28f, 4f, 1000f, 48f);
                header.enableAutoSizing = true;
                header.fontSizeMin = 28f;
                header.fontSizeMax = 40f;
            }

            Place(scenarioButton, 1076f, 4f, 188f, 48f);
            var titles = Place(Node("ColumnTitles", pages), 0f, 0f, PageWidth, 56f);
            columnTitles = titles.gameObject;
            foreach (var (text, x, w, align) in new[]
                     {
                         ("Control", 28f, 190f, TextAlignmentOptions.MidlineLeft), ("Setting", 232f, 250f, TextAlignmentOptions.MidlineLeft),
                         ("−  Value  +", 490f, 344f, TextAlignmentOptions.Center), ("Target", 846f, 222f, TextAlignmentOptions.Center),
                         ("Status", 1080f, 184f, TextAlignmentOptions.MidlineLeft),
                     })
            {
                var title = NewText(titles, "Title" + text, 30f, 24f, Ink, FontStyles.Bold, align);
                title.text = text;
                Place(title, x, 4f, w, 48f);
            }

            tableRoot = Place(Node("ControlTable", pages), 0f, 0f, PageWidth, PageHeight);
            tableRoot.SetSiblingIndex(Mathf.Max(0, pages.childCount - 1));
            for (var c = 0; c < TabTitles.Length; c++)
            {
                var groupTop = TableTop + c * 2f * RowHeight;
                if (c > 0)
                {
                    var rule = NewImage(tableRoot, "Rule" + c, new Color(1f, 1f, 1f, 0.16f), null);
                    Place(rule, 16f, groupTop - 2f, PageWidth - 32f, 2f);
                }

                for (var j = 0; j < 2; j++)
                {
                    var y = groupTop + j * RowHeight;
                    var row = Place(Node($"Row{c + 1}.{j + 1}", tableRoot), 220f, y, PageWidth - 236f, RowHeight);
                    var cell = new Cell { Control = c, Sub = j, Root = row.gameObject };
                    cell.Band = NewImage(row, "Band", new Color(1f, 1f, 1f, 0.04f), sprite);
                    Place(cell.Band, 0f, 3f, PageWidth - 236f, RowHeight - 6f);
                    cell.Label = NewText(row, "Setting", 30f, 22f, Color.white, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
                    Place(cell.Label, 12f, 0f, 254f, RowHeight);
                    cell.Minus = CloneKey(template, row, "Minus", "−");
                    Place(cell.Minus, 270f, (RowHeight - KeySize) * 0.5f, KeySize, KeySize);
                    var box = NewImage(row, "ValueBox", new Color(0.07f, 0.08f, 0.09f, 1f), sprite);
                    Place(box, 354f, (RowHeight - KeySize) * 0.5f, 176f, KeySize);
                    cell.Value = NewText(row, "Value", 34f, 22f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
                    Place(cell.Value, 358f, (RowHeight - KeySize) * 0.5f, 168f, KeySize);
                    cell.Plus = CloneKey(template, row, "Plus", "+");
                    Place(cell.Plus, 538f, (RowHeight - KeySize) * 0.5f, KeySize, KeySize);
                    cell.MinusLabel = cell.Minus.GetComponentInChildren<TMP_Text>(true);
                    cell.PlusLabel = cell.Plus.GetComponentInChildren<TMP_Text>(true);
                    cell.Target = NewText(row, "Target", 28f, 20f, UIPalette.TextSecondary, FontStyles.Italic, TextAlignmentOptions.Center);
                    Place(cell.Target, 626f, 0f, 222f, RowHeight);
                    cell.Tick = TickMark(row, 862f, (RowHeight - 36f) * 0.5f, 36f, UIPalette.HealthGood);
                    cell.Dot = NewImage(row, "Dot", UIPalette.Warning, sprite).gameObject;
                    Place((RectTransform)cell.Dot.transform, 868f, (RowHeight - 24f) * 0.5f, 24f, 24f);
                    cell.Status = NewText(row, "Status", 26f, 18f, Color.white, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                    Place(cell.Status, 904f, 0f, 140f, RowHeight);
                    var control = c;
                    var sub = j;
                    var minus = cell.Minus;
                    var plus = cell.Plus;
                    minus.onClick.AddListener(() => Guarded(minus, () => TableStep(control, sub, -1)));
                    plus.onClick.AddListener(() => Guarded(plus, () => TableStep(control, sub, +1)));
                    cells.Add(cell);
                }

                // The control's tab spans its two rows; a tick in its corner once it reads the chart target.
                var tab = tabs[c];
                if (tab == null)
                {
                    tabTicks.Add(null);
                    continue;
                }

                tab.transform.SetParent(tableRoot, false);
                Place(tab, 16f, groupTop + 2f, 196f, 2f * RowHeight - 4f);
                tabTicks.Add(TickMark((RectTransform)tab.transform, 150f, 8f, 36f, UIPalette.HealthGood));
            }

            // Below the table: status line, readout / result, Apply and Check Health.
            var bottom = TableTop + 8f * RowHeight + 8f;
            Place(statusLine, 16f, bottom, 792f, 46f);
            Place(readout, 16f, bottom + 48f, 792f, 46f);
            Place(resultHeadline, 16f, bottom + 48f, 300f, 46f);
            Place(resultFeedback, 322f, bottom + 48f, 486f, 46f);
            foreach (var text in new[] { statusLine, readout, resultHeadline, resultFeedback })
            {
                if (text == null)
                    continue;
                text.enableAutoSizing = true;
                text.fontSizeMin = 20f;
                text.fontSizeMax = 30f;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }

            Place(applyButton, 824f, bottom + 2f, 214f, 92f);
            Place(checkButton, 1050f, bottom + 2f, 214f, 92f);
            Place(message, 40f, 120f, PageWidth - 80f, 420f);
            Place(retryButton, (PageWidth - 300f) * 0.5f, 560f, 300f, 96f);

            // Every key on the screen gets the raised, travelling physical look.
            var keys = new List<Button>(tabs) { applyButton, checkButton, retryButton, scenarioButton };
            foreach (var cell in cells)
            {
                keys.Add(cell.Minus);
                keys.Add(cell.Plus);
            }

            if (screen != null)
            {
                keys.Add(screen.menuButton);
                keys.Add(screen.modalPrimary);
                keys.Add(screen.modalSecondary);
                keys.Add(screen.casePageClose);
                if (screen.menu != null)
                    keys.AddRange(screen.menu.GetComponentsInChildren<Button>(true));
            }

            foreach (var key in keys)
                if (key != null)
                    PhysicalButton.Attach(key);
            SetTableVisible(Current == State.Ready);
            Debug.Log($"[BTP] Kiosk control table built: {cells.Count} rows, screen {canvasRect.sizeDelta.x:0} x {canvasRect.sizeDelta.y:0} units.");
        }

        void SetTableVisible(bool visible)
        {
            if (!tableBuilt)
                return;
            if (tableRoot != null)
                tableRoot.gameObject.SetActive(visible);
            if (columnTitles != null)
                columnTitles.SetActive(visible);
            if (header != null)
                header.gameObject.SetActive(!visible);
        }

        /// <summary>A table key: control <paramref name="control"/>, setting row <paramref name="sub"/>, one step.</summary>
        public bool TableStep(int control, int sub, int direction)
        {
            if (profile == null || control < 0 || control >= TreatmentProfile.Layout.Length || sub < 0 || sub > 1)
                return false;
            if (page != control)
                ShowPage(control);
            if (control == 3 && watering != null)
            {
                // Soil Moisture follows the WATER station: [-]/[+] step "Water to add"; the delivery row is read-only.
                if (sub != 0)
                    return false;
                AmountStepRequested?.Invoke(direction);
                return true;
            }

            var (factorId, subs) = TreatmentProfile.Layout[control];
            if (!settings.Step(profile, factorId, subs[sub], direction))
                return false;
            Refresh();
            SettingChanged?.Invoke(factorId);
            return true;
        }

        /// <summary>The panel's soil settings follow the soil the WATER station reached (so Apply records what the soil is).</summary>
        void SyncSoilFromWatering()
        {
            if (watering == null || profile == null || settings == null)
                return;
            var rootZone = profile.Find(TreatmentProfile.SoilMoisture, "rootZone");
            if (rootZone != null)
                settings.Set(rootZone, TreatmentProfile.SoilMoisture, watering.MeasuredForServer);
            if (profile.Find(TreatmentProfile.SoilMoisture, "irrigation") != null)
                settings.SetChoice(TreatmentProfile.SoilMoisture, "irrigation", watering.Irrigation);
        }

        void RefreshTable()
        {
            if (!tableBuilt || profile == null)
                return;
            for (var c = 0; c < tabTicks.Count; c++)
                if (tabTicks[c] != null)
                    tabTicks[c].SetActive(ControlDone(c));
            foreach (var cell in cells)
                RefreshCell(cell);
        }

        void RefreshCell(Cell cell)
        {
            var (factorId, subs) = TreatmentProfile.Layout[cell.Control];
            var sub = profile.Find(factorId, subs[cell.Sub]);
            if (sub == null)
            {
                cell.Root.SetActive(false);
                return;
            }

            cell.Root.SetActive(true);
            cell.Band.color = new Color(1f, 1f, 1f, cell.Control == page ? 0.1f : cell.Sub == 0 ? 0.045f : 0.03f);
            bool onTarget;
            string status;
            if (cell.Control == 3 && watering != null)
            {
                string Format(double v) => profile.Find(TreatmentProfile.SoilMoisture, "rootZone")?.Format(v) ?? v.ToString("0");
                var running = watering.Running;
                if (cell.Sub == 0)
                {
                    cell.Label.text = $"Water to add\n<size=72%><color=#B8C7D4>now {Format(watering.Current)} → set {Format(watering.Setpoint)}</color></size>";
                    cell.Value.text = RootZoneWateringModel.AmountText(watering.Amount, Format);
                    cell.Target.text = sub.TargetText;
                    SetKeys(cell, true, "−", "+", !running && watering.Amount - watering.Step >= watering.MinAmount - 1e-9,
                        !running && watering.Amount + watering.Step <= watering.MaxAmount + 1e-9);
                    var measured = watering.MeasuredForServer;
                    onTarget = !running && sub.OnTarget(measured);
                    status = running ? (watering.Draining ? "Draining…" : "Watering…") : onTarget ? "On target" : measured < sub.TargetMin ? "Below" : "Above";
                }
                else
                {
                    cell.Label.text = "Water delivery";
                    cell.Value.text = sub.ShortLabel(watering.Irrigation);
                    cell.Target.text = sub.TargetText;
                    SetKeys(cell, false, "", "", false, false);
                    onTarget = sub.IsTargetChoice(watering.Irrigation);
                    status = onTarget ? "On target" : "Use WATER";
                }
            }
            else if (sub.IsChoice)
            {
                var choice = settings.GetChoice(factorId, sub.Id);
                cell.Label.text = RowLabel(factorId, sub);
                cell.Value.text = sub.ShortLabel(choice);
                cell.Target.text = sub.TargetText;
                SetKeys(cell, true, "<", ">", settings.CanStep(profile, factorId, sub.Id, -1), settings.CanStep(profile, factorId, sub.Id, +1));
                onTarget = sub.IsTargetChoice(choice);
                status = onTarget ? "On target" : "Change";
            }
            else
            {
                var value = settings.Get(factorId, sub.Id);
                cell.Label.text = RowLabel(factorId, sub);
                cell.Value.text = sub.Format(value);
                cell.Target.text = sub.TargetText;
                SetKeys(cell, true, "−", "+", settings.CanStep(profile, factorId, sub.Id, -1), settings.CanStep(profile, factorId, sub.Id, +1));
                onTarget = sub.OnTarget(value);
                status = onTarget ? "On target" : value < sub.TargetMin ? "Below" : "Above";
            }

            cell.Tick.SetActive(onTarget);
            cell.Dot.SetActive(!onTarget);
            cell.Status.text = status;
            cell.Status.color = onTarget ? UIPalette.HealthGood : UIPalette.Warning;
        }

        static void SetKeys(Cell cell, bool visible, string minus, string plus, bool canMinus, bool canPlus)
        {
            cell.Minus.gameObject.SetActive(visible);
            cell.Plus.gameObject.SetActive(visible);
            if (!visible)
                return;
            if (cell.MinusLabel != null)
                cell.MinusLabel.text = minus;
            if (cell.PlusLabel != null)
                cell.PlusLabel.text = plus;
            cell.Minus.interactable = canMinus;
            cell.Plus.interactable = canPlus;
        }

        /// <summary>
        /// Guided treatment: Apply applies the current step (the temperature rows), so it is offered whenever the station allows
        /// it instead of only with all four controls on target.
        /// </summary>
        public bool GuidedApply
        {
            get => guidedApply;
            set
            {
                guidedApply = value;
                RefreshApplyGate();
            }
        }

        bool guidedApply;

        /// <summary>Apply is interactable only when the station allows it and all four controls read their targets (guided: the step decides).</summary>
        void RefreshApplyGate()
        {
            if (applyButton != null)
                applyButton.interactable = canApply && Current == State.Ready && (guidedApply || AllControlsDone);
        }
    }
}
