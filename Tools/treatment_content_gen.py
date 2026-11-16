"""Authoring aid for the treatment content database.

Writes Assets/_Project/Data/Treatment/treatment_content.sql, the canonical, reviewable SQL
source that Unity imports as the bundled read-only SQLite database. Lesson content follows
Docs/DataBase_Plan.md. Run:  python Tools/treatment_content_gen.py
"""
import os
import sys

CONTENT_VERSION = 2
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "_Project", "Data", "Treatment", "treatment_content.sql")

SCHEMA = """
CREATE TABLE content_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE environments (environment_key TEXT PRIMARY KEY, display_name TEXT NOT NULL, prefab_key TEXT NOT NULL, description TEXT);
CREATE TABLE environment_fixtures (environment_key TEXT NOT NULL REFERENCES environments(environment_key), target_id TEXT NOT NULL,
  target_tag TEXT NOT NULL, asset_key TEXT NOT NULL, slot TEXT NOT NULL, label TEXT, module_key TEXT, PRIMARY KEY (environment_key, target_id));
CREATE TABLE tools (tool_key TEXT PRIMARY KEY, name TEXT NOT NULL, asset_key TEXT NOT NULL, description TEXT);
CREATE TABLE cases (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, plant_name TEXT NOT NULL, disease_name TEXT NOT NULL,
  category TEXT NOT NULL, scenario TEXT NOT NULL, outcome TEXT NOT NULL CHECK (outcome IN ('Managed','PestSuppressed','Replaced')),
  environment_key TEXT NOT NULL REFERENCES environments(environment_key), specimen_asset TEXT NOT NULL, specimen_scale REAL NOT NULL DEFAULT 1,
  summary TEXT NOT NULL, result_text TEXT NOT NULL, content_version INTEGER NOT NULL, PRIMARY KEY (plant_id, disease_id));
CREATE TABLE case_modules (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, module_key TEXT NOT NULL, PRIMARY KEY (plant_id, disease_id, module_key));
CREATE TABLE case_tools (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, tool_key TEXT NOT NULL REFERENCES tools(tool_key),
  PRIMARY KEY (plant_id, disease_id, tool_key));
CREATE TABLE case_targets (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, target_id TEXT NOT NULL, target_tag TEXT NOT NULL,
  asset_key TEXT NOT NULL, material_key TEXT, slot TEXT NOT NULL, label TEXT, show_after_step TEXT, hide_after_step TEXT,
  PRIMARY KEY (plant_id, disease_id, target_id));
CREATE TABLE methods (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, method_id TEXT NOT NULL, name TEXT NOT NULL, purpose TEXT NOT NULL,
  limitations TEXT NOT NULL, PRIMARY KEY (plant_id, disease_id, method_id));
CREATE TABLE steps (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, step_id TEXT NOT NULL, ord INTEGER NOT NULL, phase TEXT NOT NULL,
  title TEXT NOT NULL, instruction TEXT NOT NULL, action TEXT NOT NULL, tool_key TEXT, target_tag TEXT, required_count INTEGER NOT NULL DEFAULT 1,
  route TEXT, method_id TEXT, checkpoint_id TEXT, contaminates INTEGER NOT NULL DEFAULT 0, requires_clean_tool INTEGER NOT NULL DEFAULT 0,
  success_text TEXT, wrong_tool_feedback TEXT, wrong_target_feedback TEXT, prerequisite_feedback TEXT, PRIMARY KEY (plant_id, disease_id, step_id));
CREATE TABLE step_prereqs (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, step_id TEXT NOT NULL, requires_step_id TEXT NOT NULL,
  PRIMARY KEY (plant_id, disease_id, step_id, requires_step_id));
CREATE TABLE step_options (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, step_id TEXT NOT NULL, option_id TEXT NOT NULL, ord INTEGER NOT NULL,
  label TEXT NOT NULL, correct INTEGER NOT NULL, route TEXT, feedback TEXT NOT NULL, PRIMARY KEY (plant_id, disease_id, step_id, option_id));
CREATE TABLE followups (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, checkpoint_id TEXT NOT NULL, prompt TEXT NOT NULL, finding TEXT NOT NULL,
  escalation TEXT NOT NULL, PRIMARY KEY (plant_id, disease_id, checkpoint_id));
CREATE TABLE evidence (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, publisher TEXT NOT NULL, url TEXT NOT NULL, reviewed TEXT,
  region TEXT, limitations TEXT);
"""

# Manual treatment mode (button-only VR workflow). Added tables only: the step-lesson tables above are unchanged.
MANUAL_SCHEMA = """
CREATE TABLE case_profile (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, description TEXT NOT NULL, symptoms TEXT NOT NULL,
  initial_severity REAL NOT NULL, initial_health REAL NOT NULL, max_severity_drop REAL NOT NULL, max_health_gain REAL NOT NULL,
  max_worsen REAL NOT NULL, max_spot_fade REAL NOT NULL, max_yellow_fade REAL NOT NULL, max_green_gain REAL NOT NULL,
  max_vein_gain REAL NOT NULL, recovery_note TEXT NOT NULL, PRIMARY KEY (plant_id, disease_id));
CREATE TABLE treatment_controls (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, control_id TEXT NOT NULL, ord INTEGER NOT NULL,
  method_id TEXT NOT NULL, name TEXT NOT NULL, description TEXT NOT NULL, min_value REAL NOT NULL, max_value REAL NOT NULL,
  step_value REAL NOT NULL, start_value REAL NOT NULL, rec_min REAL NOT NULL, rec_max REAL NOT NULL, weight REAL NOT NULL,
  required INTEGER NOT NULL DEFAULT 1, feedback_low TEXT NOT NULL, feedback_high TEXT NOT NULL, feedback_ok TEXT NOT NULL,
  PRIMARY KEY (plant_id, disease_id, control_id),
  FOREIGN KEY (plant_id, disease_id, method_id) REFERENCES methods(plant_id, disease_id, method_id));
CREATE TABLE control_rules (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, rule_id TEXT NOT NULL, control_a TEXT NOT NULL,
  a_min REAL NOT NULL, a_max REAL NOT NULL, control_b TEXT, b_min REAL, b_max REAL, penalty REAL NOT NULL, blocks INTEGER NOT NULL DEFAULT 0,
  feedback TEXT NOT NULL, PRIMARY KEY (plant_id, disease_id, rule_id));
"""

TOOLS = [
    ("magnifier", "Magnifier", "tool.magnifier", "Enlarges symptoms and pests for inspection."),
    ("shears", "Pruning shears", "tool.shears", "Removes marked material at the marked cut point."),
    ("sprayer", "Hand sprayer (training)", "tool.sprayer", "Applies a chosen treatment category to designated surfaces. No product or dose is taught."),
    ("drip_emitter", "Drip emitter", "tool.drip", "Waters the soil at the root zone and keeps foliage dry."),
    ("water_wand", "Overhead water wand", "tool.wand", "Overhead water. Wets foliage; used only for directed mite washing."),
    ("stake", "Plant stake", "tool.stake", "Supports the stem so foliage stays off the soil."),
    ("mulch", "Mulch", "tool.mulch", "Covers soil to reduce splash onto lower leaves."),
    ("rake", "Leaf rake", "tool.rake", "Gathers fallen leaves and residue."),
    ("trellis_tie", "Trellis ties", "tool.tie", "Positions shoots on the trellis for airflow."),
    ("predator_release", "Predator-release container", "tool.predator", "Releases predatory mites at a pest hotspot."),
    ("containment_bag", "Containment bag", "tool.bag", "Encloses an infected plant before it is moved."),
    ("spade", "Spade", "tool.spade", "Assisted removal of a whole plant."),
    ("transplant", "Clean transplant", "tool.transplant", "Certified clean, suitable planting stock."),
    ("insect_mesh", "Insect mesh", "tool.mesh", "Excludes whiteflies from clean planting material."),
    ("sticky_card", "Monitoring card", "tool.card", "Yellow card for monitoring insect vectors (whiteflies, psyllids, leafhoppers)."),
]

ENVIRONMENTS = [
    ("greenhouse", "Greenhouse treatment room", "env.greenhouse", "Indoor treatment room with greenhouse crops outside: leaf holder, tool bench, room controls."),
    ("orchard", "Orchard treatment room", "env.orchard", "Indoor treatment room overlooking the orchard: leaf holder, fruit crates, room controls."),
    ("vineyard", "Vineyard treatment room", "env.vineyard", "Indoor treatment room overlooking the vineyard: leaf holder, trunk cutaway, room controls."),
    ("field", "Field crop treatment room", "env.field", "Indoor treatment room overlooking the field: leaf holder, crop samples, room controls."),
]

# (target_id, tag, asset, slot, label, module)
FIXTURES = [
    ("fx_disposal", "disposal_bin", "fx.disposal_bin", "disposal", "Plant waste", None),
    ("fx_sanitation", "sanitation_station", "fx.sanitation", "sanitation", "Tool hygiene", None),
    ("fx_valve", "irrigation_valve", "fx.valve", "controls", "Drip irrigation", "irrigation"),
    ("fx_vent", "vent_control", "fx.vent", "controls", "Roof vent", "ventilation"),
    ("fx_fan", "fan_control", "fx.fan", "controls", "Air circulation", "ventilation"),
    ("fx_humidity", "humidity_display", "fx.display", "controls", "Humidity risk", "ventilation"),
    ("fx_risk", "risk_card", "fx.risk_card", "bench", "Case notes", "risk"),
    ("fx_board", "planning_board", "fx.board", "bench", "Planning board", "planning"),
    ("fx_result", "lab_result", "fx.lab_result", "bench", "Simulated confirmation", "confirmation"),
    ("fx_neighbour", "neighbour_host", "fx.neighbour", "neighbour", "Neighbouring host plant", "neighbours"),
    ("fx_containment", "containment_area", "fx.containment", "containment", "Infected plant \u2022 contained", "containment"),
    ("fx_clean_pot", "clean_pot", "fx.clean_pot", "clean_station", "Clean planting position", "replacement"),
    ("fx_mesh", "mesh_frame", "fx.mesh_frame", "mesh_station", "Insect-mesh frame", "mesh"),
]

PLANT_NAMES = {"apple": "Apple", "bell_pepper": "Bell Pepper", "cherry": "Cherry", "corn": "Corn", "grape": "Grape",
               "peach": "Peach", "potato": "Potato", "strawberry": "Strawberry", "tomato": "Tomato",
               # Lab and local Treatment only (Tools/treatment_content_lab_only.py)
               "blueberry": "Blueberry", "chilli": "Chilli", "cotton": "Cotton", "mango": "Mango", "orange": "Orange",
               "rice": "Rice", "soybean": "Soybean", "squash": "Squash", "sugarcane": "Sugarcane", "wheat": "Wheat"}
ENVIRONMENT_FOR = {"tomato": "greenhouse", "bell_pepper": "greenhouse", "strawberry": "greenhouse", "apple": "orchard",
                   "cherry": "orchard", "peach": "orchard", "grape": "vineyard", "corn": "field", "potato": "field",
                   "chilli": "greenhouse", "squash": "greenhouse", "blueberry": "orchard", "mango": "orchard", "orange": "orchard",
                   "cotton": "field", "rice": "field", "soybean": "field", "sugarcane": "field", "wheat": "field"}

TOOL_NAMES = {t[0]: t[1].lower() for t in TOOLS}


def q(value):
    if value is None:
        return "NULL"
    if isinstance(value, (int, float)):
        return str(value)
    return "'" + str(value).replace("'", "''") + "'"


class Case:
    def __init__(self, plant, disease, name, category, outcome, scenario, summary, result, modules=(), tools=(), scale=1.0):
        self.plant, self.disease, self.name = plant, disease, name
        self.category, self.outcome, self.scenario = category, outcome, scenario
        self.summary, self.result, self.modules, self.tools, self.scale = summary, result, list(modules), list(tools), scale
        self.targets, self.methods, self.steps, self.followups, self.evidence = [], [], [], [], []
        self.leaf_mat = "leafmat.%s.%s" % (plant, disease)
        self.default_wrong_target = None

    # ---- targets
    def target(self, tid, tag, asset, slot, label, material=None, show=None, hide=None):
        self.targets.append((tid, tag, asset, material, slot, label, show, hide))
        return self

    def leaves(self, prefix, tag, count, slot, label, material=None, show=None):
        for i in range(1, count + 1):
            mat = material or self.leaf_mat
            if mat in (HEALTHY, NEW):
                mat = "%s.%s" % (mat, self.plant)
            self.target("%s%d" % (prefix, i), tag, "target.leaf.%s.%s" % (self.plant, self.disease), slot, label, mat, show)
        return self

    def method(self, mid, name, purpose, limitations):
        self.methods.append((mid, name, purpose, limitations))
        return self

    def source(self, publisher, url, reviewed, region, limitations):
        self.evidence.append((publisher, url, reviewed, region, limitations))
        return self

    def followup(self, cid, prompt, finding, escalation):
        self.followups.append((cid, prompt, finding, escalation))
        return self

    # ---- steps
    def step(self, sid, phase, title, lines, action, tool=None, tag=None, count=1, route=None, method=None, checkpoint=None,
             contaminates=0, clean=0, success=None, wrong_tool=None, wrong_target=None, prereq=None, requires=(), options=()):
        if tool and wrong_tool is None:
            wrong_tool = "Use the %s for this task." % TOOL_NAMES[tool]
        if wrong_target is None:
            wrong_target = self.default_wrong_target
        if tool and tool not in self.tools:
            self.tools.append(tool)
        self.steps.append(dict(sid=sid, phase=phase, title=title, instruction="|".join(lines), action=action, tool=tool, tag=tag,
                               count=count, route=route, method=method, checkpoint=checkpoint, contaminates=contaminates,
                               clean=clean, success=success, wrong_tool=wrong_tool, wrong_target=wrong_target, prereq=prereq,
                               requires=list(requires), options=list(options)))
        return self

    def inspect(self, sid, tag, title, lines, count=1, tool="magnifier", **kw):
        return self.step(sid, "Inspect", title, lines, "inspect", tool, tag, count, success=kw.pop("success", "Noted."), **kw)

    def confirm(self, correct, wrongs, title="Confirm teaching case", lines=("Match the symptoms to the supplied case",)):
        options = [("correct", correct, 1, None, "Confirmed. The symptoms match the supplied teaching case.")]
        for i, (label, feedback) in enumerate(wrongs):
            options.append(("wrong%d" % (i + 1), label, 0, None, feedback))
        return self.step("confirm", "Inspect", title, list(lines), "choose", options=options, success="Teaching case confirmed.")

    def choose(self, sid, phase, title, lines, options, **kw):
        # options: (label, correct, route, feedback)
        rows = [("opt%d" % (i + 1), label, 1 if ok else 0, route, fb) for i, (label, ok, route, fb) in enumerate(options)]
        return self.step(sid, phase, title, lines, "choose", options=rows, **kw)

    def prune(self, sid, tag, count, title, lines, wrong_target, contaminates=1, **kw):
        return self.step(sid, "Prepare", title, lines, "cut", "shears", tag, count, contaminates=contaminates,
                         wrong_target=wrong_target, success=kw.pop("success", "Removed. Next: dispose of it in Plant waste."), **kw)

    def collect(self, sid, tag, count, title, lines, tool=None, **kw):
        return self.step(sid, "Prepare", title, lines, "collect", tool, tag, count, success=kw.pop("success", "Collected. Next: dispose of it in Plant waste."), **kw)

    def dispose(self, sid="dispose", title="Dispose of removed material", lines=("Place removed material in the lidded Plant waste container",
                                                                              "Do not leave diseased material on the ground")):
        return self.step(sid, "Prepare", title, list(lines), "dispose", None, "disposal_bin", success="Disposed of safely.",
                         wrong_target="Put the removed material in the Plant waste container.")

    def clean(self, sid="clean_tools", lines=("Clean and disinfect the tools at the Tool hygiene station",
                                              "Follow the station procedure before touching healthy plants")):
        return self.step(sid, "Prepare", "Clean the tools", list(lines), "clean_tool", None, "sanitation_station",
                         success="Tools cleaned.", wrong_target="Clean the tools at the Tool hygiene station first.")

    def advance(self, sid="wait", title="Advance to simulated follow-up", lines=("Time is compressed in this simulation",
                                                                                   "Inspect again at the next checkpoint")):
        return self.step(sid, "Recheck", title, list(lines), "advance", success="Simulated later observation.")

    def observe(self, sid, tag, checkpoint, title, lines, tool=None):
        return self.step(sid, "Recheck", title, lines, "observe", tool, tag, checkpoint=checkpoint, success="Finding recorded.")

    # ---- SQL
    def sql(self):
        p, d = self.plant, self.disease
        out = []
        out.append("INSERT INTO cases VALUES (%s);" % ", ".join(q(v) for v in (
            p, d, PLANT_NAMES[p], self.name, self.category, self.scenario, self.outcome, ENVIRONMENT_FOR[p],
            "leafspecimen.%s.%s" % (p, d), self.scale, self.summary, self.result, CONTENT_VERSION)))
        for m in self.modules:
            out.append("INSERT INTO case_modules VALUES (%s, %s, %s);" % (q(p), q(d), q(m)))
        for t in self.tools:
            out.append("INSERT INTO case_tools VALUES (%s, %s, %s);" % (q(p), q(d), q(t)))
        for t in self.targets:
            out.append("INSERT INTO case_targets VALUES (%s);" % ", ".join(q(v) for v in (p, d) + t))
        for m in self.methods:
            out.append("INSERT INTO methods VALUES (%s);" % ", ".join(q(v) for v in (p, d) + m))
        for i, s in enumerate(self.steps):
            out.append("INSERT INTO steps VALUES (%s);" % ", ".join(q(v) for v in (
                p, d, s["sid"], (i + 1) * 10, s["phase"], s["title"], s["instruction"], s["action"], s["tool"], s["tag"], s["count"],
                s["route"], s["method"], s["checkpoint"], s["contaminates"], s["clean"], s["success"], s["wrong_tool"],
                s["wrong_target"], s["prereq"])))
            for r in s["requires"]:
                out.append("INSERT INTO step_prereqs VALUES (%s, %s, %s, %s);" % (q(p), q(d), q(s["sid"]), q(r)))
            for j, (oid, label, ok, route, fb) in enumerate(s["options"]):
                out.append("INSERT INTO step_options VALUES (%s);" % ", ".join(q(v) for v in (p, d, s["sid"], oid, j + 1, label, ok, route, fb)))
        for f in self.followups:
            out.append("INSERT INTO followups VALUES (%s);" % ", ".join(q(v) for v in (p, d) + f))
        for e in self.evidence:
            out.append("INSERT INTO evidence VALUES (%s);" % ", ".join(q(v) for v in (p, d) + e))
        return out


CASES = []


def case(*args, **kw):
    c = Case(*args, **kw)
    CASES.append(c)
    return c


HEALTHY = "leafmat.healthy"
NEW = "leafmat.new_growth"
PROTECT_FEEDBACK = "Protection covers healthy tissue only. It does not repair existing lesions."

# =====================================================================================  LEAF LESSONS
# Every case is treated on the enlarged leaf specimen in the crop-themed treatment room. Targets are
# areas of that leaf (lesions, healthy tissue, underside, petiole); growing conditions are corrected
# with the room's controls (humidity display, roof vent, fan, drip valve).

UMN_EB = ("University of Minnesota Extension", "https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/early-blight-in-tomato-and-potato")
UMN_BS = ("University of Minnesota Extension", "https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/bacterial-spot-of-tomato-and-pepper")
UMN_LB = ("University of Minnesota Extension", "https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/late-blight")
UMN_VIR = ("University of Minnesota Extension", "https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/tomato-viruses")
REVIEWED = "Reviewed for this lesson"
CUT_WRONG = "Cut only the marked lesion areas. Healthy tissue and the petiole stay."


def leaf(c, lesion_label, lesions=3, lesion_tag="lesion_spot", lesion_asset="target.spot", underside_label="Leaf underside", healthy=True):
    """The leaf specimen's targets: lesion areas, underside, healthy tissue and the petiole."""
    for i in range(1, lesions + 1):
        c.target("lesion_%d" % i, lesion_tag, lesion_asset, "leaf_lesion", lesion_label)
    for i in range(1, 3):
        c.target("under_%d" % i, "leaf_underside", "target.spot", "leaf_underside", underside_label)
    if healthy:
        for i in range(1, 3):
            c.target("healthy_%d" % i, "healthy_tissue", "target.spot", "leaf_healthy", "Healthy leaf tissue")
    c.target("petiole", "petiole", "target.petiole", "leaf_petiole", "Petiole (leaf stalk)")
    c.default_wrong_target = CUT_WRONG if c.default_wrong_target is None else c.default_wrong_target
    return c


def new_leaf(c, label="New leaf from the treated plant"):
    c.target("new_leaf", "new_leaf", "target.leaf.%s.%s" % (c.plant, c.disease), "comparison", label, "leafmat.healthy.%s" % c.plant, show="wait")


def contained_leaf(c, label="Infected leaf sample"):
    c.target("sample", "infected_plant", "target.leaf_proxy", "leaf_whole", label)
    c.target("replacement", "replacement", "target.seedling", "clean_planting", "Healthy replacement", show="plant_clean")


def inspect_pair(c, look, under, count=2, tag="lesion_spot"):
    c.inspect("inspect", tag, "Inspect the lesions", look, count=count)
    c.inspect("underside", "leaf_underside", "Turn the leaf over", under, count=1)


def humidity(c, why="Prolonged leaf wetness favours this disease"):
    c.inspect("humidity", "humidity_display", "Check room humidity", ["Read the humidity risk display", why], tool=None, success="Humidity risk: high.")
    c.step("vent", "Treat", "Open the roof vent", ["Improve ventilation"], "control", None, "vent_control", requires=["humidity"],
           success="Roof vent OPEN.", wrong_target="Operate the roof vent control on the wall panel.")
    c.step("fan", "Treat", "Start air circulation", ["Dry the leaf surfaces faster"], "control", None, "fan_control", requires=["humidity"],
           success="Air circulation ON.", wrong_target="Switch on the circulation fan.")


def drip(c, why="Overhead watering splashes spores and bacteria onto leaves"):
    c.step("valve", "Treat", "Switch to drip irrigation", [why, "Water the soil, keep leaves dry"], "control", None, "irrigation_valve",
           success="Drip irrigation ON.", wrong_target="Turn on the drip irrigation valve on the wall panel.")


def cut(c, count=2, title="Cut out affected tissue"):
    c.prune("cut", "lesion_spot", count, title, ["Cut only the marked lesion areas", "Keep healthy tissue and the petiole",
                                                 "Then dispose and clean the shears"], CUT_WRONG)
    c.dispose()
    c.clean()


def protect(c, title, lines, options):
    """options: (label, correct, route, feedback). Adds the spray / monitoring steps for the routes offered."""
    c.choose("assess", "Treat", title, lines, options)
    routes = {o[2] for o in options if o[1]}
    if "protect" in routes:
        c.step("spray", "Treat", "Protect healthy tissue", ["Cover the marked healthy tissue", "Follow the label and treatment window",
                                                            "Old lesions will remain"], "spray", "sprayer", "healthy_tissue", 2, route="protect",
               success="Healthy tissue covered.", wrong_target="Cover only the marked healthy tissue, not the lesions or the petiole.")
    if "monitor" in routes:
        c.step("monitor_mark", "Treat", "Plan monitoring", ["Mark the healthy tissue for the next check"], "inspect", None, "healthy_tissue",
               route="monitor", success="Monitoring planned.")


def recheck(c, new_finding, escalation, old_finding="Old lesions remain on the treated leaf; dead tissue does not recover.", old=True,
            title="Advance to simulated follow-up"):
    c.advance(title=title)
    c.observe("check_new", "new_leaf", "new_growth", "Follow-up inspection", ["Compare a new leaf from the treated plant", "Check for renewed spread"])
    c.followup("new_growth", "Inspect the new leaf", new_finding, escalation)
    if old:
        c.observe("check_old", "lesion_spot", "old_lesions", "Check the treated leaf", ["Old lesions remain", "Dead tissue does not turn green"])
        c.followup("old_lesions", "Inspect the remaining lesions", old_finding, "Watch for new lesions near the old ones.")


PROTECT_OR_MONITOR = [
    ("Protect healthy tissue with a crop-appropriate, labelled protectant", True, "protect", PROTECT_FEEDBACK),
    ("Rely on sanitation and corrected conditions; monitor", True, "monitor", "A valid choice: sanitation and drier conditions come first."),
    ("Spray the lesions to make them green again", False, None, "No treatment repairs dead tissue. Protection only shields healthy tissue."),
]

# =====================================================================================  TOMATO (greenhouse room)

c = case("tomato", "early_blight", "Early blight", "Fungal", "Managed",
         "A lower tomato leaf with early blight; a warm, wet week is forecast (high-risk teaching scenario).",
         "Early blight (Alternaria) causes concentric target-like spots with yellow halos, starting on older lower leaves. Spores splash from soil and debris.",
         "Disease managed: lesion tissue removed, healthy tissue protected and the new leaf is clean. Old lesions remain.",
         modules=["irrigation"], tools=["water_wand"])
leaf(c, "Concentric lesion"); new_leaf(c)
c.method("sanitation", "Remove affected tissue and dispose of it", "Reduces the spore source", "Existing lesions on retained tissue stay.")
c.method("cultural", "Water at the base, keep foliage dry", "Reduces splash and leaf wetness", "Does not remove existing infection.")
c.method("protect", "Protective fungicide where justified", "Protects healthy tissue during high-risk weather", "Does not cure lesions; product and dose follow the local label.")
c.method("future", "Rotation and resistant or tolerant varieties", "Lowers risk in later crops", "Not an immediate treatment.")
inspect_pair(c, ["Look for concentric, target-like spots", "Note the yellow halo around each spot"], ["Check the underside: the spots show through", "No mould growth underneath"])
c.confirm("Concentric brown spots with yellow halos (early blight)",
          [("Olive-brown mould on the underside", "That describes leaf mold. These spots are concentric and on the upper surface."),
           ("Yellow, upward-curled young leaf", "Curling suggests a virus. This leaf carries concentric spots.")])
cut(c)
drip(c)
protect(c, "Assess protection", ["High-risk teaching scenario", "Warm, wet weather is forecast"], PROTECT_OR_MONITOR)
recheck(c, "The new leaf is clean: no new lesions in this simulation.", "If lesions appear on new leaves, repeat sanitation and reassess protection.")
c.source(*UMN_EB, REVIEWED, "Upper Midwest, USA", "Regional extension guidance; product choice follows local labels.")

c = case("tomato", "bacterial_spot", "Bacterial spot", "Bacterial", "Managed",
         "A tomato leaf with bacterial spot after overhead watering and wet handling.",
         "Xanthomonas causes small dark water-soaked spots; bacteria spread in splash, on hands and on tools.",
         "Spread reduced: affected tissue removed, tools cleaned, drip irrigation on. Existing spots remain.",
         modules=["irrigation"], tools=["water_wand"])
leaf(c, "Bacterial spot"); new_leaf(c)
c.method("hygiene", "Dry handling, tool and hand sanitation, selective removal", "Limits spread", "Established infection is managed, not cured.")
c.method("irrigation", "Drip instead of overhead irrigation", "Stops splash dispersal", "Does not remove existing infection.")
c.method("copper", "Copper-based protection where justified", "May slow spread on new tissue", "Limited efficacy; copper resistance is documented.")
inspect_pair(c, ["Look for small, dark, water-soaked spots", "Inspect while the leaf is dry"], ["Spots are visible on the underside too"])
c.confirm("Small dark water-soaked spots (bacterial spot)",
          [("Large concentric target spots", "Large concentric spots point to early blight.")])
cut(c)
drip(c)
protect(c, "Evaluate protective treatment", ["Copper may slow spread on new tissue", "Resistance limits it; it is not a cure"],
        [("Labelled copper protection for healthy tissue, as a supplement", True, "protect", "Accepted as a supplement only."),
         ("Rely on hygiene, drip irrigation and monitoring", True, "monitor", "A valid choice: hygiene and dry foliage come first."),
         ("Copper will cure the spotted tissue", False, None, "Copper does not cure established spots.")])
recheck(c, "The new leaf is largely free of fresh spots.", "Replace heavily affected young plants with clean stock.")
c.source(*UMN_BS, REVIEWED, "Upper Midwest, USA", "Copper efficacy varies with resistant strains.")

c = case("tomato", "late_blight", "Late blight", "Oomycete (water mould)", "Replaced",
         "A tomato leaf with rapidly expanding late-blight lesions; healthy tomatoes are nearby.",
         "Phytophthora infestans is a water mould, not a true fungus. Lesions expand fast in cool, wet weather and spores travel far.",
         "Outbreak contained: the infected material is bagged and removed, the neighbour protected, and clean stock planted.",
         modules=["confirmation", "containment", "neighbours", "replacement", "ventilation"], tools=["shears", "water_wand"])
leaf(c, "Expanding lesion", lesions=2, healthy=False); contained_leaf(c)
c.default_wrong_target = "Late blight spreads quickly: contain the leaf, do not cut it open."
c.method("contain", "Bag and remove infected material", "Removes the spore source", "Infected material cannot be saved.")
c.method("protect", "Late-blight-active protection for healthy hosts", "Protects neighbours", "Ordinary fungicides cannot be assumed effective against a water mould.")
c.method("clean_stock", "Clean transplants", "Restarts with healthy plants", "Windborne spores can still arrive.")
inspect_pair(c, ["Look for large greasy grey-green lesions"], ["White growth at lesion edges on the underside in humid air"], count=1)
c.inspect("confirm_result", "lab_result", "Obtain confirmation", ["Read the simulated diagnostic result"], tool=None, success="Late blight confirmed (simulated).")
c.choose("confirm", "Inspect", "Confirm the diagnosis", ["Late blight is a water mould (oomycete)"],
         [("Late blight: contain it; use late-blight-active products", True, None, "Correct. Fungicides for true fungi cannot be assumed to work."),
          ("Early blight: cut out the spots", False, None, "Expanding greasy lesions with white growth indicate late blight. Contain, do not cut.")])
c.step("bag", "Prepare", "Bag the infected leaf", ["Enclose the leaf in a containment bag"], "contain", "containment_bag", "infected_plant", success="Leaf enclosed.")
c.step("remove", "Prepare", "Move it to containment", ["Place the bagged leaf in the containment compartment"], "remove_plant", None, "infected_plant",
       success="Infected material contained.")
c.clean(lines=("Clean gloves and tools before touching healthy plants",))
humidity(c, "Cool, humid air favours late blight")
c.inspect("neighbours", "neighbour_host", "Inspect the neighbouring host", ["Check the nearby tomato for lesions"])
c.choose("assess_n", "Treat", "Choose protection for the neighbour", ["Healthy plants nearby are threatened"],
         [("A product active against late blight, as labelled", True, "protect", "Correct: the water mould needs a late-blight-active product."),
          ("Any general fungicide", False, None, "Ordinary fungicides cannot be assumed effective against late blight.")])
c.step("spray_n", "Treat", "Protect the neighbour", ["Cover the healthy neighbour's foliage"], "spray", "sprayer", "neighbour_host", route="protect",
       success="Neighbour protected.", wrong_target="Protect the healthy neighbour, not the contained leaf.")
c.step("plant_clean", "Treat", "Establish clean stock", ["Plant the healthy transplant in the clean pot"], "plant", "transplant", "clean_pot",
       success="Healthy replacement planted.")
c.advance(lines=("A later planting stage, simulated",))
c.observe("check_replacement", "replacement", "replacement", "Inspect the replacement", ["Check the new plant for lesions"])
c.followup("replacement", "Inspect the replacement", "The replacement is healthy; the outbreak is contained.", "Report new lesions immediately.")
c.source(*UMN_LB, REVIEWED, "Upper Midwest, USA", "Product choice depends on local registrations.")

c = case("tomato", "leaf_mold", "Leaf mold", "Fungal", "Managed",
         "A greenhouse tomato leaf with leaf mold under prolonged high humidity.",
         "Passalora fulva causes yellow patches above and olive-brown mould underneath, favoured by prolonged high humidity.",
         "Disease managed after humidity control: the new leaf is clean. Old patches remain.",
         modules=["irrigation", "ventilation"], tools=["water_wand"])
leaf(c, "Yellow patch", underside_label="Olive mould (underside)"); new_leaf(c)
c.method("environment", "Ventilation, spacing and dry foliage", "Reduces prolonged humidity", "Must be maintained.")
c.method("sanitation", "Remove affected tissue", "Removes spore sources", "Old patches remain.")
c.method("protect", "Protective fungicide as a supplement", "Protects healthy tissue", "Supplements, not replaces, environmental control.")
inspect_pair(c, ["Look for pale yellow patches on the upper surface"], ["Olive-brown velvety mould on the underside"], count=1)
c.confirm("Yellow patches above, olive mould below (leaf mold)",
          [("Concentric spots on the upper surface", "Concentric spots indicate early blight; here the mould is underneath.")])
humidity(c, "Prolonged high humidity favours leaf mold")
drip(c, "Keep leaves dry")
cut(c, count=1, title="Remove the affected patch")
protect(c, "Assess protection", ["Environmental correction comes first"],
        [("Add a labelled protectant to supplement ventilation", True, "protect", "Accepted as a supplement to humidity control."),
         ("Rely on the corrected environment and monitor", True, "monitor", "A valid choice: humidity control is the main measure."),
         ("Spray but keep the vents closed", False, None, "Without humidity control, spraying alone will not hold leaf mold back.")])
recheck(c, "The new leaf has no mould underneath in this simulation.", "If mould returns, check humidity and vent operation first.")
c.source("University of Minnesota Extension", "https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/tomato-leaf-mold",
         REVIEWED, "Upper Midwest, USA", "Race-specific resistance may fail against new races.")

c = case("tomato", "septoria_leaf_spot", "Septoria leaf spot", "Fungal", "Managed",
         "A lower tomato leaf with many small spots after splashing irrigation.",
         "Septoria lycopersici makes many small circular spots with pale centres and tiny dark fruiting bodies (pycnidia).",
         "Disease managed: upward spread reduced. Old spots remain on the treated leaf.",
         modules=["irrigation"], tools=["water_wand"])
leaf(c, "Small spot with pycnidia"); new_leaf(c)
c.method("sanitation", "Remove affected tissue; manage residue", "Removes spore sources", "Retained tissue keeps its spots.")
c.method("cultural", "Dry foliage, drip irrigation, rotation", "Reduces splash and leaf wetness", "Prevention, not cure.")
c.method("protect", "Protective fungicide as a supplement", "Protects healthy tissue", "Does not remove existing spots.")
inspect_pair(c, ["Find small spots with pale centres", "Look for tiny dark fruiting bodies"], ["Spots show through underneath"])
c.confirm("Many small spots with dark fruiting bodies (Septoria leaf spot)",
          [("Large concentric target spots", "Large concentric spots point to early blight.")])
cut(c)
drip(c)
protect(c, "Assess protection", ["Spots are spreading upward"], PROTECT_OR_MONITOR)
recheck(c, "The new upper leaf stays clean in this simulation.", "If spots reach upper leaves, reassess protection.")
c.source("University of Minnesota Extension", "https://extension.umn.edu/garden-and-home/yard-and-garden/gardening-in-minnesota/yard-and-garden-problems/tomato-leaf-spot-diseases",
         REVIEWED, "Upper Midwest, USA", "Home-garden and small-farm guidance.")

c = case("tomato", "spider_mites", "Two-spotted spider mite", "Pest", "PestSuppressed",
         "A tomato leaf from a water-stressed plant with a spider-mite colony underneath.",
         "Tetranychus urticae feeds on leaf undersides, causing pale stippling and fine webbing. Hot, dry conditions favour outbreaks.",
         "Pest suppressed: few active mites and a cleaner new leaf. Old stippling remains.",
         modules=["irrigation"], tools=["shears"])
leaf(c, "Stippling", lesions=2)
c.target("colony_1", "mite_hotspot", "target.mites", "leaf_underside", "Mite colony", hide="wait")
c.target("colony_2", "mite_hotspot", "target.mites", "leaf_underside", "Mite colony", hide="wait")
new_leaf(c)
c.default_wrong_target = "Treat the mite colonies on the underside."
c.method("conditions", "Correct water stress and dust", "Less favourable for mites", "Does not remove a colony alone.")
c.method("biological", "Conserve or release predatory mites", "Suppresses mites without harming natural enemies", "Needs time and compatible treatments.")
c.method("contact", "Directed water wash, then labelled insecticidal soap or oil", "Knocks back the colony", "Contact only; repeat checks needed.")
c.inspect("inspect", "lesion_spot", "Inspect the stippling", ["Pale speckles on the upper surface"], count=1)
c.inspect("colonies", "mite_hotspot", "Confirm live mites", ["Turn the leaf: look underneath", "Moving mites and fine webbing"], count=2)
c.confirm("Live mites and webbing with pale stippling (spider mites)",
          [("Stippling but no live mites", "Without live mites you would not treat; here live mites are visible.")])
drip(c, "The source plant is water-stressed; mites thrive on stressed plants")
c.choose("route", "Treat", "Choose a compatible route", ["Pick one route for the colony"],
         [("Release predatory mites; avoid broad-spectrum sprays", True, "predator", "Biological route chosen."),
          ("Wash with water, then a labelled insecticidal soap", True, "contact", "Contact route chosen."),
          ("Broad-spectrum insecticide", False, None, "Broad-spectrum sprays kill predators and often make mite outbreaks worse.")])
c.step("release", "Treat", "Release predatory mites", ["Place the release sachet at each colony"], "release", "predator_release", "mite_hotspot", 2,
       route="predator", success="Predators released.")
c.step("wash", "Treat", "Wash the colonies", ["Direct water at the underside"], "spray", "water_wand", "mite_hotspot", 2, route="contact", success="Colony washed.")
c.step("soap", "Treat", "Apply the contact treatment", ["Cover the colonies", "Follow the label"], "spray", "sprayer", "mite_hotspot", 2,
       route="contact", success="Colony treated.")
recheck(c, "Few active mites; the new leaf is less damaged.", "If mites rebound, reassess the route and conditions.",
        old_finding="Old stippling remains.")
c.source("UC IPM", "https://ipm.ucanr.edu/home-and-landscape/spider-mites/", REVIEWED, "California, USA", "Predator availability and registrations vary.")

c = case("tomato", "target_spot", "Target spot", "Fungal", "Managed",
         "An inner-canopy tomato leaf with target spot under prolonged leaf wetness.",
         "Corynespora cassiicola causes brown spots with concentric rings, favoured by long leaf wetness.",
         "Disease managed: affected tissue removed and healthy tissue protected. Advanced damage remains.",
         modules=["ventilation"], tools=["water_wand"])
leaf(c, "Ringed brown spot"); new_leaf(c)
c.method("sanitation", "Remove infected material; manage residue", "Reduces inoculum", "Advanced damage remains.")
c.method("drying", "Shorten leaf wetness", "Less infection", "Must be maintained.")
c.method("programme", "Effective fungicide programme with resistance management", "Important under high pressure", "Do not assume an early-blight claim applies.")
inspect_pair(c, ["Find brown spots with concentric rings"], ["Check the underside"])
c.confirm("Ringed brown spots from a humid inner canopy (target spot, supplied diagnosis)",
          [("Early blight", "Similar rings, but this supplied case is target spot.")])
cut(c)
humidity(c, "Long leaf wetness favours target spot")
protect(c, "Select a treatment programme", ["High pressure; fungicide resistance is documented"],
        [("A programme effective against target spot, rotating modes of action", True, "protect", "Correct: resistance management keeps products working."),
         ("Reuse an early-blight product because it is labelled for early blight", False, None, "Do not assume an early-blight claim applies to target spot.")])
recheck(c, "The new leaf is protected; advanced lesions remain on the old one.", "Rotate modes of action if spots reappear.")
c.source("UF/IFAS Extension", "https://ask.ifas.ufl.edu/publication/PP351", REVIEWED, "Florida, USA", "Resistance status varies.")

c = case("tomato", "yellow_leaf_curl_virus", "Yellow leaf curl virus", "Viral", "Replaced",
         "A curled yellow tomato leaf with whiteflies underneath.",
         "TYLCV is spread by whiteflies. Infected plants cannot be cured; management prevents spread to healthy plants.",
         "Healthy replacement established. The infected leaf was contained, not cured; vector monitoring continues.",
         modules=["confirmation", "containment", "replacement", "mesh"], tools=["shears", "sprayer"])
leaf(c, "Curled yellow tissue", lesions=2, healthy=False)
c.target("whiteflies", "whiteflies", "target.whiteflies", "leaf_underside", "Whiteflies", hide="bag")
contained_leaf(c)
c.default_wrong_target = "There is no cure. Follow the containment steps."
c.method("contain", "Contain vectors, then remove infected material", "Stops it acting as a virus source", "Infected tissue is not cured.")
c.method("vectors", "Whitefly management and exclusion mesh", "Prevents reinfection", "Requires continued monitoring.")
c.method("clean_stock", "Clean, resistant transplants", "Healthy replacement", "Resistance may not eliminate symptoms.")
c.inspect("inspect", "lesion_spot", "Inspect the symptoms", ["Curled, yellowed young tissue"], count=1)
c.inspect("vector", "whiteflies", "Inspect for whiteflies", ["Turn the leaf: small white insects underneath"])
c.inspect("confirm_result", "lab_result", "Read simulated confirmation", ["Read the diagnostic result"], tool=None, success="TYLCV confirmed (simulated).")
c.choose("confirm", "Inspect", "Decide the management", ["No cure for infected tissue"],
         [("Contain the leaf, manage whiteflies, plant clean stock", True, None, "Correct: there is no curative treatment."),
          ("Spray the leaf to cure the virus", False, None, "No spray cures a virus infection.")])
c.step("bag", "Prepare", "Contain the infected leaf", ["Bag it before moving it", "This traps the whiteflies"], "contain", "containment_bag", "infected_plant",
       success="Leaf and whiteflies enclosed.")
c.step("remove", "Prepare", "Move to containment", ["Place the bag in the covered compartment"], "remove_plant", None, "infected_plant", success="Contained.")
c.step("mesh", "Treat", "Manage whiteflies", ["Fit insect mesh over the clean planting area"], "place", "insect_mesh", "mesh_frame", success="Mesh fitted.")
c.step("card", "Treat", "Set vector monitoring", ["Hang the whitefly-monitoring card"], "place", "sticky_card", "mesh_frame", success="Monitoring card in place.")
c.step("plant_clean", "Treat", "Plant clean stock", ["Plant the clean, resistant transplant"], "plant", "transplant", "clean_pot", success="Healthy replacement established.")
c.advance(lines=("Simulated later observation",))
c.observe("check_replacement", "replacement", "replacement", "Monitor the replacement", ["Inspect the new plant"])
c.followup("replacement", "Inspect the replacement", "Healthy replacement established; few whiteflies on the card.", "If counts rise, review exclusion.")
c.source(*UMN_VIR, REVIEWED, "Upper Midwest, USA", "Regional whitefly pressure differs.")
c.source("UC IPM", "https://ipm.ucanr.edu/agriculture/tomato/tomato-yellow-leaf-curl/", REVIEWED, "California, USA", "Commercial guidance.")

c = case("tomato", "mosaic_virus", "Mosaic virus", "Viral", "Replaced",
         "A confirmed tomato mosaic (tobamovirus) leaf: mottled and distorted.",
         "Tobamoviruses spread mechanically on hands, tools, supports and surfaces. Insecticide is not a cure.",
         "A distinct healthy replacement is established. The infected material was removed, never cured.",
         modules=["confirmation", "containment", "replacement"], tools=["shears", "sprayer"])
leaf(c, "Mottled tissue", lesions=2, healthy=False); contained_leaf(c)
c.target("ties", "used_ties", "target.ties", "bench", "Used ties and supports")
c.default_wrong_target = "Mosaic virus has no cure; follow containment and sanitation."
c.method("remove", "Remove infected material", "Removes the virus source", "Cannot be cured.")
c.method("sanitation", "Validated cleaning of hands, tools, supports and surfaces", "Stops mechanical spread", "Must follow validated procedures.")
c.method("clean_stock", "Clean planting material and resistance", "Healthy replacement", "Resistance varies by strain.")
inspect_pair(c, ["Light and dark green mottling", "Distorted tissue"], ["The underside shows the same mottling"])
c.inspect("confirm_result", "lab_result", "Obtain simulated confirmation", ["Read the diagnostic result"], tool=None, success="Tobamovirus confirmed (simulated).")
c.choose("confirm", "Inspect", "Decide the management", ["Tobamoviruses spread mechanically"],
         [("Remove the material and sanitise everything it touched", True, None, "Correct: sanitation stops mechanical spread."),
          ("Spray insecticide to cure it", False, None, "Nothing cures the infected tissue, and insects are not the route here.")])
c.step("bag", "Prepare", "Contain the leaf", ["Bag the infected leaf"], "contain", "containment_bag", "infected_plant", success="Leaf enclosed.")
c.step("remove", "Prepare", "Move to containment", ["Place it in the containment compartment"], "remove_plant", None, "infected_plant", contaminates=1,
       success="Infected material removed.")
c.collect("ties", "used_ties", 1, "Replace handling materials", ["Remove the used ties and supports"])
c.dispose()
c.clean(lines=("Clean hands, tools and work surfaces", "Use the validated procedure"))
c.step("plant_clean", "Treat", "Establish clean stock", ["Plant the clean transplant"], "plant", "transplant", "clean_pot", clean=1, success="Healthy replacement planted.")
c.advance(lines=("Simulated later observation",))
c.observe("check_replacement", "replacement", "replacement", "Inspect later", ["Inspect the replacement for mottling"])
c.followup("replacement", "Inspect the replacement", "The replacement shows no mottling.", "If symptoms appear, repeat removal and sanitation.")
c.source(*UMN_VIR, REVIEWED, "Upper Midwest, USA", "Sanitation products must be validated for tobamoviruses.")
c.source("UC IPM", "https://ipm.ucanr.edu/agriculture/tomato/tobacco-mosaic/", REVIEWED, "California, USA", "Commercial guidance.")

# =====================================================================================  ORCHARD ROOM: APPLE, CHERRY, PEACH

c = case("apple", "apple_scab", "Apple scab", "Fungal", "Managed",
         "A scabbed leaf from a susceptible apple tree in a wet spring.",
         "Venturia inaequalis overwinters in fallen leaves and infects new growth in wet spring weather, causing olive-dark velvety lesions.",
         "Fewer new infections on later leaves. Old scabs remain on the treated leaf.",
         modules=["risk", "planning", "ventilation"], tools=["water_wand"])
leaf(c, "Scab lesion"); new_leaf(c, "Later leaf from the treated tree")
c.method("sanitation", "Destroy fallen and affected leaves", "Removes the overwintering spore source", "Reduces, does not eliminate, spring spores.")
c.method("drying", "Faster drying of foliage", "Shorter infection periods", "Does not remove existing scabs.")
c.method("protect", "Locally approved scab fungicide timed to infection risk", "Protects susceptible growth", "Existing scabs remain.")
c.method("resistance", "Scab-resistant varieties", "Long-term prevention", "For future planting.")
inspect_pair(c, ["Look for olive-dark, velvety spots"], ["Check the underside for more lesions"])
c.confirm("Olive-dark velvety lesions (apple scab)", [("Bright orange spots", "Bright orange spots indicate cedar apple rust.")])
cut(c, title="Remove scabbed tissue")
humidity(c, "Leaf wetness after rain drives scab infection")
c.inspect("risk", "risk_card", "Read the risk card", ["Wet spring weather is forecast", "New leaves are susceptible"], tool=None, success="Infection risk: high.")
protect(c, "Select timely protection", ["Protect susceptible growth before infection periods"],
        [("Locally approved scab fungicide, timed to infection risk", True, "protect", PROTECT_FEEDBACK),
         ("Spray old scabs to cure them", False, None, "Existing scabs will not disappear; protection only shields new growth.")])
c.choose("future", "Treat", "Plan future planting", ["Choose for the next planting"],
         [("A scab-resistant variety", True, None, "Resistant varieties are the most reliable long-term measure."),
          ("The same susceptible variety", False, None, "A susceptible variety needs protection every wet spring.")])
recheck(c, "The later leaf has few new infections.", "If scab appears, review timing against infection periods.", title="Advance to later in the season")
c.source("UC IPM", "https://ipm.ucanr.edu/agriculture/apple/apple-scab/", REVIEWED, "California, USA", "Infection timing models vary by region.")

c = case("apple", "black_rot", "Black rot", "Fungal", "Managed",
         "An apple leaf with frogeye spots, and mummified fruit collected from the same tree.",
         "Botryosphaeria obtusa survives in mummified fruit, dead wood and cankers, and causes frogeye leaf spot.",
         "Infection sources removed and healthy tissue protected. Rotten fruit never becomes sound fruit.",
         modules=["risk"], tools=["water_wand"])
leaf(c, "Frogeye spot"); new_leaf(c, "Later leaf from the treated tree")
c.target("mummy_1", "fruit_mummy", "target.mummy", "bench", "Mummified fruit")
c.target("mummy_2", "fruit_mummy", "target.mummy", "bench", "Mummified fruit")
c.method("sanitation", "Remove mummies, dead wood and cankers", "Removes the main inoculum", "Central to control.")
c.method("protect", "Protection with an appropriate fungicide", "Protects healthy tissue", "Rotten fruit is not repaired.")
inspect_pair(c, ["Purple-edged frogeye spots"], ["Check the underside"])
c.inspect("mummies", "fruit_mummy", "Inspect the mummified fruit", ["Shrivelled black fruit from the same tree"])
c.confirm("Frogeye spots with fruit mummies (black rot)", [("Velvety olive spots", "That pattern is apple scab.")])
c.collect("collect", "fruit_mummy", 2, "Collect the fruit mummies", ["They carry the fungus to next season"])
cut(c, title="Remove frogeye tissue")
protect(c, "Decide on protection", ["Infection sources are removed"], PROTECT_OR_MONITOR)
recheck(c, "The later leaf is clean.", "Check for new cankers at winter pruning.")
c.source("Penn State Extension", "https://extension.psu.edu/pome-fruit-disease-black-rot-and-frogeye-leaf-spot", REVIEWED, "Pennsylvania, USA",
         "Product choice follows local labels.")

c = case("apple", "cedar_apple_rust", "Cedar apple rust", "Fungal", "Managed",
         "An apple leaf with rust spots; a juniper with galls stands near the orchard (shown in the room).",
         "Gymnosporangium juniperi-virginianae alternates between juniper galls and apple leaves; spores are windborne.",
         "Fewer new infections next season. Existing spotted tissue remains damaged.",
         modules=["risk", "planning"], tools=["water_wand"])
leaf(c, "Orange rust spot"); new_leaf(c, "Next-season leaf")
c.target("juniper", "alternate_host", "target.juniper", "alt_host", "Juniper (alternate host)")
c.target("gall_1", "gall", "target.gall", "alt_host", "Juniper gall")
c.target("gall_2", "gall", "target.gall", "alt_host", "Juniper gall")
c.method("resistance", "Resistant apple varieties", "Main defence", "For future planting.")
c.method("galls", "Remove accessible juniper galls before spore release", "Reduces local spores", "Cannot exclude windborne spores.")
c.method("protect", "Timely spring protection", "Protects new leaves", "Does not repair existing spots.")
inspect_pair(c, ["Bright orange-yellow spots"], ["Tufted spore structures underneath"])
c.inspect("host", "alternate_host", "Inspect the alternate host", ["Examine the juniper"], tool=None, success="Galls found on the juniper.")
c.confirm("Orange spots with galls on juniper (cedar apple rust)", [("Olive velvety spots", "That is apple scab.")])
c.prune("galls", "gall", 2, "Remove juniper galls", ["Late winter: before spores are released", "Cut each gall"], "Remove only the marked galls.", contaminates=0)
c.dispose()
protect(c, "Plan next-season prevention", ["Spores also travel on the wind"],
        [("Timely spring protection for the susceptible tree", True, "protect", PROTECT_FEEDBACK),
         ("Monitor; plant resistant varieties in future", True, "monitor", "A valid choice after gall removal."),
         ("Spray the orange spots to repair them", False, None, "Spraying established spots does not repair them.")])
recheck(c, "Fewer new infections.", "Windborne spores may still arrive; keep monitoring.", title="Advance to next season")
c.source("University of Minnesota Extension", "https://extension.umn.edu/garden-and-home/yard-and-garden/gardening-in-minnesota/yard-and-garden-problems/cedar-apple-rust",
         REVIEWED, "Upper Midwest, USA", "Home-orchard guidance.")

c = case("cherry", "powdery_mildew", "Powdery mildew", "Fungal", "Managed",
         "A cherry leaf with active powdery mildew from a shaded inner shoot.",
         "Podosphaera clandestina forms white powdery growth, favoured by shaded, crowded canopies.",
         "Mildew suppressed and later growth protected. Distorted tissue is not repaired.",
         modules=["risk", "ventilation"], tools=["water_wand", "shears"])
leaf(c, "Mildew patch"); new_leaf(c, "Later shoot leaf")
c.default_wrong_target = "Treat the mildew and protect the healthy tissue."
c.method("canopy", "Better light and airflow", "Less favourable conditions", "Does not remove mildew alone.")
c.method("protect", "Cherry-labelled mildew treatment", "Suppresses mildew", "Depends on growth stage, temperature and compatibility.")
inspect_pair(c, ["White powdery growth on the surface"], ["Check the underside"])
c.confirm("White powdery growth (powdery mildew)", [("Orange pustules", "Orange pustules indicate a rust.")])
humidity(c, "Shaded, still air favours mildew")
c.inspect("risk", "risk_card", "Read the treatment-stage card", ["Growth stage", "Temperature", "Product compatibility"], tool=None)
c.choose("assess", "Treat", "Select a compatible treatment", ["Active mildew on a susceptible tree"],
         [("A cherry-labelled mildew treatment suited to stage and temperature", True, "protect", "Correct."),
          ("Sulfur during a heatwave, right after an oil spray", False, None, "Sulfur can injure foliage in heat and is incompatible with recent oil sprays.")])
c.step("spray", "Treat", "Cover the mildew patches", ["Cover the marked mildew patches"], "spray", "sprayer", "lesion_spot", 2,
       route="protect", success="Covered.")
recheck(c, "Mildew suppressed on later growth.", "Keep scouting while conditions favour mildew.", old_finding="Distorted tissue remains.")
c.source("UC IPM", "https://ipm.ucanr.edu/agriculture/cherry/powdery-mildew/", REVIEWED, "California, USA", "Registrations differ.")

c = case("peach", "bacterial_spot", "Bacterial spot", "Bacterial", "Managed",
         "A shot-hole peach leaf from a drought-stressed tree in midsummer, with a pitted fruit.",
         "Xanthomonas arboricola pv. pruni causes angular spots that drop out (shot holes) and pitted fruit.",
         "Tree condition improved and new damage reduced. Existing holes and pits remain.",
         modules=["irrigation", "planning"], tools=["water_wand", "shears"])
leaf(c, "Shot hole"); new_leaf(c, "Later leaf")
c.target("fruit", "pitted_fruit", "target.fruit", "bench", "Pitted fruit")
c.default_wrong_target = "Holes and pits do not heal; follow the panel."
c.method("cultural", "Water, nutrition, reduced stress", "Healthier trees", "Gradual.")
c.method("resistance", "Resistant varieties", "Main long-term measure", "For future planting.")
c.method("copper", "Copper only in label-permitted stages", "Limited protection", "Leaf injury and label limits apply.")
inspect_pair(c, ["Small angular holes where spots fell out"], ["Check the underside"])
c.inspect("fruit", "pitted_fruit", "Inspect the fruit", ["Pits and cracks"])
c.confirm("Shot holes and pitted fruit (bacterial spot)", [("Powdery white growth", "That is powdery mildew.")])
drip(c, "The tree is drought-stressed")
c.choose("window", "Treat", "Check the treatment window", ["It is midsummer", "Copper use is limited by stage and label"],
         [("No spray now; record it for the next labelled stage", True, None, "Correct: copper on summer peach foliage risks injury."),
          ("Spray copper on the leaves now", False, None, "In-season copper can injure peach leaves."),
          ("Treat so the holes heal", False, None, "Holes and pits do not heal.")])
c.choose("future", "Treat", "Plan future planting", ["Choose for the next planting"],
         [("A bacterial-spot-resistant variety", True, None, "Resistance is the most reliable measure."),
          ("Any variety, then spray more", False, None, "Chemical control is difficult; resistance matters most.")])
recheck(c, "Less new damage on a better-watered tree.", "Review nutrition if the tree stays stressed.", old_finding="Existing holes remain.")
c.source("Clemson Cooperative Extension", "https://hgic.clemson.edu/factsheet/peach-diseases/", REVIEWED, "South Carolina, USA", "Home-orchard guidance.")

# =====================================================================================  GREENHOUSE ROOM: PEPPER, STRAWBERRY

c = case("bell_pepper", "bacterial_spot", "Bacterial spot", "Bacterial", "Managed",
         "A pepper leaf with bacterial spot, and a spotted transplant in the new tray.",
         "Xanthomonas causes dark leaf spots; it spreads in splash, on tools and through infected transplants.",
         "Spread reduced and the diseased transplant rejected. Old spots remain.",
         modules=["irrigation"], tools=["water_wand"])
leaf(c, "Bacterial spot"); new_leaf(c)
c.target("transplant", "diseased_transplant", "target.seedling_spotted", "bench", "Spotted transplant")
c.method("clean_stock", "Clean seed and transplants", "Prevents introduction", "Does not cure infected plants.")
c.method("hygiene", "Dry handling, sanitation, selective removal", "Limits spread", "Managed, not cured.")
c.method("copper", "Copper where justified", "May slow spread", "Resistance documented.")
inspect_pair(c, ["Small dark spots"], ["Spots show underneath"])
c.inspect("tray", "diseased_transplant", "Check the transplants", ["Inspect the new tray"])
c.confirm("Dark leaf spots spread by splash (bacterial spot)", [("White powdery growth", "That is powdery mildew.")])
c.collect("reject", "diseased_transplant", 1, "Reject the spotted transplant", ["Do not plant diseased transplants"])
cut(c)
drip(c)
protect(c, "Assess protective treatment", ["Copper has limits and resistance exists"],
        [("Labelled copper for healthy tissue, as a supplement", True, "protect", "Accepted as a supplement only."),
         ("Hygiene, drip irrigation and monitoring", True, "monitor", "A valid choice."),
         ("Copper will cure the spots", False, None, "Copper does not cure established infection.")])
recheck(c, "The new leaf is largely clean.", "Replace badly affected young plants.")
c.source("UC IPM", "https://ipm.ucanr.edu/agriculture/peppers/bacterial-spot/", REVIEWED, "California, USA", "Commercial guidance.")
c.source(*UMN_BS, REVIEWED, "Upper Midwest, USA", "Copper resistance varies.")

c = case("strawberry", "leaf_scorch", "Leaf scorch", "Fungal", "Managed",
         "A scorched strawberry leaf from a perennial bed before post-harvest renovation.",
         "Diplocarpon earlianum causes small purple spots that merge until the leaf looks scorched; it survives on old leaves.",
         "Healthier later foliage after renovation. Scorched tissue is not repaired.",
         modules=["irrigation", "planning"], tools=["water_wand"])
leaf(c, "Scorched area"); new_leaf(c, "Post-renovation leaf")
c.method("water", "Drip irrigation and weed control", "Drier canopy", "Ongoing.")
c.method("renovation", "Remove old foliage at post-harvest renovation", "Reduces inoculum", "Timing matters; never damage crowns.")
c.method("fungicide", "Fungicide depending on pressure", "For heavy pressure", "Mild cases may need none.")
inspect_pair(c, ["Purple spots merging into scorched margins"], ["Check the underside"])
c.inspect("setting", "planning_board", "Identify the bed type", ["Read the lesson setting"], tool=None, success="Perennial bed: renovation applies.")
c.confirm("Purple spots merging into scorched leaves (leaf scorch)", [("Grey fuzzy fruit rot", "That is grey mould.")])
drip(c)
c.choose("assess", "Treat", "Decide on fungicide", ["Mild pressure in this bed"],
         [("No fungicide now; rely on renovation and drip irrigation", True, None, "Correct for this mild case."),
          ("Spray scorched tissue to repair it", False, None, "Scorched tissue is not repaired by spraying.")])
c.advance("renovate_time", "Advance to post-harvest renovation", ["After harvest, old foliage is removed"])
cut(c, title="Remove scorched tissue (renovation)")
recheck(c, "Healthier new foliage after renovation.", "If scorch returns strongly, review resistant planting material.")
c.source("NC State Extension", "https://content.ces.ncsu.edu/leaf-scorch-of-strawberry", REVIEWED, "North Carolina, USA", "Systems differ.")

# =====================================================================================  FIELD ROOM: CORN, POTATO

CORN_STRIP = "Corn lessons protect functional leaf tissue: do not cut the leaf."


def corn(c, lesion_label):
    leaf(c, lesion_label); new_leaf(c, "Ear leaf later in the season")
    c.default_wrong_target = CORN_STRIP


def corn_plan(c):
    c.choose("future", "Treat", "Plan the next crop", ["Choose for the next season"],
             [("Rotate to a non-host crop, manage residue, choose a resistant hybrid", True, None, "Correct."),
              ("Plant corn again into the same residue", False, None, "Corn on corn residue keeps the inoculum close.")])


c = case("corn", "gray_leaf_spot", "Gray leaf spot", "Fungal", "Managed",
         "A leaf from a susceptible hybrid near tasseling; lesions are approaching the ear leaf and humid weather is forecast.",
         "Cercospora zeae-maydis causes rectangular tan lesions bounded by veins; it survives in residue.",
         "Remaining healthy tissue protected. Dead rectangular lesions remain.",
         modules=["risk", "planning"], tools=["shears", "water_wand"])
corn(c, "Rectangular lesion")
c.method("resistance", "Resistant hybrids", "Main defence", "For the next crop.")
c.method("rotation", "Rotation and residue management", "Reduces inoculum", "Not an in-season fix.")
c.method("fungicide", "Foliar fungicide only when justified", "Protects functional leaves", "Only when stage, susceptibility and weather justify it.")
inspect_pair(c, ["Rectangular lesions between veins"], ["Lesions show on both surfaces"])
c.confirm("Rectangular tan lesions between veins (gray leaf spot)", [("Cinnamon pustules", "Pustules indicate common rust.")])
c.inspect("risk", "risk_card", "Review crop stage and weather", ["Near tasseling", "Susceptible hybrid", "Humid forecast"], tool=None)
protect(c, "Choose management", ["Is a foliar fungicide justified?"],
        [("Yes: protect the remaining healthy tissue", True, "protect", "Justified here: stage, susceptibility and weather all favour disease."),
         ("Cut the lesions out of the leaf", False, None, CORN_STRIP)])
corn_plan(c)
recheck(c, "Healthy tissue protected through grain fill.", "Consult an adviser if lesions reach the ear leaf early.")
c.source("University of Minnesota Extension", "https://extension.umn.edu/agriculture/crop-production/corn/gray-leaf-spot-on-corn", REVIEWED,
         "Upper Midwest, USA", "Thresholds vary.")

c = case("corn", "common_rust", "Common rust", "Fungal", "Managed",
         "A leaf with mild common rust from a resistant hybrid past the vulnerable stage.",
         "Puccinia sorghi forms cinnamon-brown pustules on both leaf surfaces; spores blow in on the wind.",
         "Leaf function protected without unnecessary spraying. Light rust remains.",
         modules=["risk", "planning"], tools=["shears", "sprayer"])
corn(c, "Rust pustule")
c.method("resistance", "Resistant hybrids", "Main defence", "For future crops.")
c.method("monitor", "Monitoring for mild disease", "Avoids unnecessary spraying", "Requires follow-up.")
c.method("fungicide", "Early justified fungicide on susceptible crops", "For susceptible crops", "Not needed in mild cases.")
inspect_pair(c, ["Cinnamon-brown pustules"], ["Pustules on both surfaces"])
c.confirm("Cinnamon pustules on both surfaces (common rust)", [("Long cigar-shaped lesions", "That indicates northern leaf blight.")])
c.inspect("risk", "risk_card", "Assess severity and stage", ["Light pustules", "Resistant hybrid", "Past the vulnerable stage"], tool=None)
c.choose("assess", "Treat", "Choose management", ["Is spraying needed in this mild case?"],
         [("Monitor: mild rust on a resistant hybrid", True, "monitor", "Correct: spraying would add cost without benefit."),
          ("Spray now to be safe", False, None, "Unnecessary here. Monitor and reassess."),
          ("Cut rusted tissue out of the leaf", False, None, CORN_STRIP)])
c.step("monitor_mark", "Treat", "Set monitoring", ["Mark healthy tissue for weekly checks"], "inspect", None, "healthy_tissue", route="monitor",
       success="Monitoring set.")
c.inspect("example", "planning_board", "Compare a higher-risk example", ["Susceptible sweet corn with early heavy rust may justify fungicide"], tool=None)
corn_plan(c)
recheck(c, "Leaf tissue stays functional; rust stays light.", "Reassess if pustules increase sharply before grain fill.")
c.source("University of Minnesota Extension", "https://extension.umn.edu/agriculture/crop-production/corn/common-rust-on-corn", REVIEWED,
         "Upper Midwest, USA", "Sweet-corn thresholds differ.")

c = case("corn", "northern_leaf_blight", "Northern leaf blight", "Fungal", "Managed",
         "A leaf with early northern leaf blight from a susceptible hybrid.",
         "Exserohilum turcicum causes long cigar-shaped grey-green lesions; it survives in residue.",
         "Less progression into healthy tissue. Existing lesions remain dead tissue.",
         modules=["risk", "planning"], tools=["shears", "water_wand"])
corn(c, "Cigar-shaped lesion")
c.method("resistance", "Resistant hybrids", "Main defence", "For the next crop.")
c.method("rotation", "Rotation and residue", "Reduce inoculum", "Not an in-season fix.")
c.method("fungicide", "Fungicide for early disease on susceptible crops", "Protects healthy tissue", "Existing lesions remain.")
inspect_pair(c, ["Long cigar-shaped grey-green lesions"], ["Check the underside"])
c.confirm("Long cigar-shaped lesions (northern leaf blight)", [("Small rectangles between veins", "That indicates gray leaf spot.")])
c.inspect("risk", "risk_card", "Assess stage and susceptibility", ["Early disease", "Susceptible hybrid"], tool=None)
protect(c, "Choose management", ["Early disease on a susceptible hybrid"],
        [("Protect the healthy tissue", True, "protect", "Justified: early disease on a susceptible hybrid."),
         ("Cut the lesions out", False, None, CORN_STRIP)])
corn_plan(c)
recheck(c, "Healthy tissue stays functional; old lesions remain.", "Consult an adviser if lesions reach the ear leaf early.")
c.source("University of Minnesota Extension", "https://extension.umn.edu/agriculture/crop-production/corn/northern-corn-leaf-blight", REVIEWED,
         "Upper Midwest, USA", "Thresholds vary.")

c = case("potato", "early_blight", "Early blight", "Fungal", "Managed",
         "An older potato leaf with early blight from a water-stressed crop.",
         "Alternaria solani causes dark concentric lesions on older leaves, worse on stressed plants.",
         "Healthy tissue protected and progression reduced. Old lesions remain.",
         modules=["irrigation", "planning"], tools=["water_wand"])
leaf(c, "Concentric lesion"); new_leaf(c, "Later leaf")
c.method("care", "Appropriate irrigation and nutrition", "Less stress", "Gradual.")
c.method("clean", "Clean seed, rotation, volunteer control", "Fewer sources", "Long-term.")
c.method("protect", "Protection where pressure warrants", "Protects tissue", "Old lesions remain.")
inspect_pair(c, ["Dark concentric lesions"], ["Check the underside"])
c.confirm("Dark concentric lesions on an older leaf (early blight)", [("Greasy expanding lesions with white growth", "That indicates late blight.")])
drip(c, "Correct the crop's water stress")
cut(c)
protect(c, "Assess protection", ["Disease pressure is moderate"], PROTECT_OR_MONITOR)
c.choose("future", "Treat", "Plan rotation", ["Choose for the next season"],
         [("Rotate away from potato and tomato; use clean seed", True, None, "Correct."),
          ("Plant potatoes again in the same field", False, None, "Same-field replanting keeps inoculum close.")])
recheck(c, "Progression reduced on later leaves.", "Reassess if lesions climb the canopy.")
c.source(*UMN_EB, REVIEWED, "Upper Midwest, USA", "Regional guidance.")

c = case("potato", "late_blight", "Late blight", "Oomycete (water mould)", "Replaced",
         "A potato leaf and tuber from a late-blight outbreak; healthy potatoes are nearby.",
         "Phytophthora infestans infects foliage and tubers. It is a water mould.",
         "Outbreak contained and a later crop started from clean seed. Infected material was not cured.",
         modules=["confirmation", "containment", "neighbours", "replacement"], tools=["shears", "water_wand"])
leaf(c, "Water-soaked lesion", lesions=2, healthy=False); contained_leaf(c)
c.target("tuber", "tuber_cutaway", "target.tuber", "bench", "Tuber cutaway")
c.default_wrong_target = "Contain the infected material; do not cut it open."
c.method("contain", "Contain infected foliage and tubers", "Removes spore sources", "Infected material is not saved.")
c.method("protect", "Late-blight-active protection for healthy hosts", "Protects neighbours", "Ordinary fungicides cannot be assumed effective.")
c.method("clean_seed", "Certified seed potatoes", "Healthy later crop", "Windborne spores can still arrive.")
inspect_pair(c, ["Water-soaked lesions"], ["White growth on the underside in humid air"], count=1)
c.inspect("tuber", "tuber_cutaway", "Inspect the tuber", ["Reddish-brown rot under the skin"])
c.inspect("confirm_result", "lab_result", "Simulated confirmation", ["Read the adviser's result"], tool=None, success="Late blight confirmed (simulated).")
c.choose("confirm", "Inspect", "Confirm the diagnosis", ["Late blight is a water mould"],
         [("Late blight: contain it; use late-blight-active products", True, None, "Correct."),
          ("A generic fungal blight", False, None, "Late blight is an oomycete; generic fungal treatment cannot be assumed to work.")])
c.step("bag", "Prepare", "Bag the infected leaf", ["Enclose it before moving it"], "contain", "containment_bag", "infected_plant", success="Leaf enclosed.")
c.step("remove", "Prepare", "Move it to containment", ["Place it in the containment compartment"], "remove_plant", None, "infected_plant", contaminates=1,
       success="Infected material contained.")
c.clean()
c.choose("assess_n", "Treat", "Protect neighbouring hosts", ["Healthy potatoes nearby are threatened"],
         [("A product active against late blight, as labelled", True, "protect", "Correct."),
          ("A general fungicide", False, None, "Ordinary fungicides cannot be assumed effective against late blight.")])
c.step("spray_n", "Treat", "Protect the neighbour", ["Cover the healthy neighbour"], "spray", "sprayer", "neighbour_host", route="protect", success="Neighbour protected.")
c.step("plant_clean", "Treat", "Plant clean seed", ["Plant certified seed potato for the later crop"], "plant", "transplant", "clean_pot", success="Clean crop started.")
c.advance(lines=("Simulated later crop",))
c.observe("check_replacement", "replacement", "crop", "Inspect the later crop", ["Inspect the new plant"])
c.followup("crop", "Inspect the later crop", "A healthy later crop; the outbreak is contained.", "Report any new lesions quickly.")
c.source(*UMN_LB, REVIEWED, "Upper Midwest, USA", "Registrations vary.")

# =====================================================================================  VINEYARD ROOM: GRAPE

c = case("grape", "black_rot", "Black rot", "Fungal", "Managed",
         "A grape leaf with black-rot spots, and berry mummies collected from the vine.",
         "Guignardia bidwellii overwinters in mummified berries; leaf spots and berry rot follow wet weather.",
         "Infection sources removed and healthy tissue protected.",
         modules=["risk", "ventilation"], tools=["water_wand"])
leaf(c, "Black-rot spot"); new_leaf(c, "Later leaf from the vine")
c.target("mummy_1", "berry_mummy", "target.mummy", "bench", "Berry mummy")
c.target("mummy_2", "berry_mummy", "target.mummy", "bench", "Berry mummy")
c.method("sanitation", "Remove mummies and infected material", "Removes inoculum", "Must include ground mummies.")
c.method("drying", "Canopy drying", "Shorter wetness", "Ongoing.")
c.method("protect", "Protection during susceptible stages", "Protects new tissue", "Infected berries are not saved.")
inspect_pair(c, ["Tan spots with dark borders"], ["Check the underside"])
c.inspect("mummies", "berry_mummy", "Inspect berry mummies", ["Shrivelled black berries"])
c.confirm("Leaf spots and berry mummies (black rot)", [("Tiger-striped leaves", "Tiger stripes point to esca.")])
c.collect("collect", "berry_mummy", 2, "Collect the mummies", ["They overwinter the fungus"])
cut(c, title="Remove spotted tissue")
humidity(c, "Wet canopies favour black rot")
protect(c, "Choose protection", ["New tissue is susceptible now"],
        [("Appropriately timed protection", True, "protect", PROTECT_FEEDBACK),
         ("Spray the mummies", False, None, "Mummies are removed, not treated.")])
recheck(c, "The later leaf stays clean.", "Remove any new mummies promptly.")
c.source("Iowa State University Extension", "https://yardandgarden.extension.iastate.edu/encyclopedia/black-rot-grape", REVIEWED, "Iowa, USA", "Home-vineyard guidance.")

c = case("grape", "esca", "Esca (black measles)", "Trunk-disease complex", "Replaced",
         "A tiger-striped leaf from a severely affected vine; its trunk cutaway shows extensive decay.",
         "Esca is a complex of wood-infecting fungi. Established wood infections cannot be eradicated by foliar fungicide.",
         "A healthy replacement vine is established. The infected vine was not cured.",
         modules=["containment", "replacement", "planning"], tools=["shears", "sprayer"])
leaf(c, "Tiger-stripe tissue", lesions=2, healthy=False); contained_leaf(c, "Leaf from the infected vine")
c.target("trunk", "trunk_cutaway", "target.trunk_cutaway", "bench", "Trunk cutaway")
c.default_wrong_target = "Foliar treatment cannot cure wood infection. Follow the panel."
c.method("prevention", "Pruning-wound prevention", "Prevents new infections", "Future practice.")
c.method("replace", "Replace severely affected vines", "Restores production", "The original vine is not cured.")
inspect_pair(c, ["Tiger-stripe patterns between veins"], ["Check the underside"])
c.inspect("trunk", "trunk_cutaway", "Inspect the trunk cutaway", ["Dark streaks and decayed wood"])
c.choose("severity", "Inspect", "Judge severity", ["Leaf symptoms are only the surface sign"],
         [("Severe wood infection: replace the vine", True, None, "Correct: foliar fungicide will not eradicate wood infection."),
          ("Spray the leaves to cure the trunk", False, None, "No foliar fungicide eradicates established wood infection.")])
c.step("bag", "Prepare", "Bag the sample", ["Enclose the leaf from the infected vine"], "contain", "containment_bag", "infected_plant", success="Sample enclosed.")
c.step("remove", "Prepare", "Move it to containment", ["The vine itself is removed in the field (assisted)"], "remove_plant", None, "infected_plant",
       success="Infected material removed.")
c.step("plant_clean", "Treat", "Establish clean stock", ["Plant the certified clean vine"], "plant", "transplant", "clean_pot", success="Replacement planted.")
c.choose("future", "Treat", "Protect future pruning wounds", ["Choose the pruning practice"],
         [("Prune in dry weather and protect large wounds", True, None, "Correct."),
          ("Prune during rain", False, None, "Wet pruning wounds admit trunk pathogens.")])
c.advance(lines=("Simulated later season",))
c.observe("check_replacement", "replacement", "replacement", "Inspect the replacement", ["Inspect the young vine"])
c.followup("replacement", "Inspect the replacement", "A healthy replacement vine.", "Protect its pruning wounds every winter.")
c.source("UC IPM", "https://ipm.ucanr.edu/agriculture/grape/esca-black-measles/", REVIEWED, "California, USA", "Trunk diseases vary.")

c = case("grape", "leaf_blight", "Leaf blight (Isariopsis leaf spot)", "Fungal", "Managed",
         "A leaf from a susceptible grape cultivar in a warm, humid region, about to drop early.",
         "Pseudocercospora vitis (Isariopsis) causes dark angular blotches and premature leaf drop on susceptible cultivars.",
         "Healthy tissue protected and the next cycle better protected. Old blotches persist.",
         modules=["risk", "ventilation"], tools=["water_wand", "shears"])
leaf(c, "Dark blotch"); new_leaf(c, "Post-harvest leaf")
c.default_wrong_target = "Protect the healthy tissue; this lesson keeps the leaf functional."
c.method("protect", "Disease-specific protection for susceptible cultivars", "Keeps foliage functional", "Generic copper/Bordeaux cannot be assumed effective.")
c.method("cultivar", "Cultivar-appropriate management", "Matches susceptibility", "Varies by cultivar.")
inspect_pair(c, ["Dark angular blotches"], ["Check the underside"])
c.inspect("notes", "risk_card", "Read the case notes", ["Susceptible cultivar", "Warm, humid region"], tool=None)
c.confirm("Dark angular blotches on a susceptible cultivar (Isariopsis leaf spot)", [("Tiger stripes", "Tiger stripes indicate esca.")])
humidity(c, "Warm, humid air favours this disease")
protect(c, "Choose protection", ["Protect the remaining healthy tissue"],
        [("A treatment supported for this disease and cultivar", True, "protect", "Correct."),
         ("Bordeaux mixture, as for other grape diseases", False, None, "Bordeaux mixture showed poor efficacy in the studied context."),
         ("No action", False, None, "Untreated, early leaf loss weakens the vine.")])
recheck(c, "Functional foliage retained.", "Continue protection after harvest where relevant.", title="Advance to post-harvest")
c.source("Embrapa", "https://www.infoteca.cnptia.embrapa.br/infoteca/bitstream/doc/1060452/1/ComunicadoTecnico1891.pdf", REVIEWED, "Brazil",
         "Studied context; susceptibility varies by cultivar.")

# =====================================================================================  MANUAL TREATMENT MODE
# Button-only VR workflow: the learner sets abstract 0-10 treatment levels per case. Every control is
# derived from one of the case's existing `methods` rows (method_id is a foreign key), so the manual
# mode teaches the same measures as the step lesson. Levels are ABSTRACT TEACHING SCALES: no product
# names, doses, concentrations or intervals. Start values are always below the recommended range, so
# the recommended value is never pre-selected. Recovery caps keep the outcome honest: Managed leaves
# never lose their lesions, Replaced leaves never improve (treatment cannot save them).
# EXPERT REVIEW NEEDED: every range, start value, weight, rule, severity and cap below is an
# illustrative teaching value chosen by the developers, not agronomic guidance.

# control type -> (name, description, feedback when too low, feedback when too high)
CONTROL_TYPES = {
    "sanitation": ("Sanitation thoroughness",
                   "How thoroughly affected tissue and debris are removed and disposed of (0 = nothing removed, 10 = every affected part removed and bagged).",
                   "Too little: affected tissue left in place keeps acting as an infection source.",
                   "More than this leaf needs: removing healthy tissue weakens the plant without extra benefit."),
    "protect": ("Protective spray coverage",
                "Share of the healthy tissue covered with a suitable, labelled protectant category. Abstract 0-10 scale: no product or dose is taught.",
                "Too little coverage leaves healthy tissue exposed while conditions favour infection.",
                "Over-application adds cost, residue and resistance risk without extra protection."),
    "drip": ("Irrigation shift to drip",
             "0 = all overhead watering, 10 = all water delivered at the root zone so the foliage stays dry.",
             "Overhead watering keeps leaves wet and splashes the pathogen onto healthy tissue.",
             "Above the recommended range for this case."),
    "airflow": ("Ventilation / airflow",
                "Venting, spacing and air circulation that shorten leaf-wetness periods.",
                "Still, humid air keeps leaves wet long enough for new infections.",
                "Excess airflow chills and dries the crop without extra disease benefit."),
    "monitoring": ("Monitoring frequency",
                   "How often the crop is re-inspected for new symptoms (0 = never, 10 = every day).",
                   "Without regular checks, new symptoms are found too late.",
                   "More frequent checks than needed cost time without changing decisions."),
    "vector": ("Vector control",
               "Exclusion mesh and monitoring cards that keep insect vectors away from clean plants.",
               "Vectors left unmanaged carry the virus to healthy plants.",
               "Above the recommended range for this case."),
    "containment": ("Containment",
                    "Bagging, isolating and removing infected material so it cannot act as a source.",
                    "Infected material that is not contained keeps spreading the disease.",
                    "Above the recommended range for this case."),
    "biocontrol": ("Biological control",
                   "Release and conservation of natural enemies such as predatory mites.",
                   "Too few natural enemies to hold the colony back.",
                   "More predators than the colony can support; most starve or disperse."),
    "contact": ("Directed contact treatment",
                "Directed water wash and a labelled contact treatment on the colonies. Abstract 0-10 scale: no product or dose is taught.",
                "Too little contact treatment leaves the colony active.",
                "Repeated heavy contact treatment harms natural enemies and stresses the leaf."),
    "stress": ("Plant stress relief",
               "Correct watering and nutrition so the plant is less stressed.",
               "A stressed plant stays more susceptible.",
               "Over-watering stresses roots and favours other problems."),
    "prevention": ("Prevention planning",
                   "Rotation, residue management, resistant varieties and clean stock for later crops.",
                   "Without planning, the next crop starts with the same risk.",
                   "Above the recommended range for this case."),
    "replacement": ("Clean replacement stock",
                    "Readiness to replace infected plants with certified clean, suitable stock.",
                    "Without clean replacement stock the space is replanted with risky material or stays empty.",
                    "Above the recommended range for this case."),
}

OUTCOME_CAPS = {
    # outcome: (max_severity_drop, max_health_gain, max_worsen, spot, yellow, green, vein)
    "Managed": (0.45, 0.25, 0.12, 0.6, 0.75, 0.6, 0.7),
    "PestSuppressed": (0.3, 0.15, 0.12, 0.35, 0.4, 0.35, 0.4),
    "Replaced": (0.0, 0.0, 0.15, 0.0, 0.0, 0.0, 0.0),
}

RECOVERY_NOTES = {
    "Managed": "Dead lesion tissue does not turn green again. Good management dries the spots and stops their spread while the remaining green tissue recovers.",
    "PestSuppressed": "Old stippling stays. Fewer active mites means the speckling stops spreading and later growth is cleaner.",
    "Replaced": "This leaf cannot be saved by treatment. Correct containment stops it acting as a source; recovery means replacing it with clean stock.",
}


def ctl(kind, method, rec_min, rec_max, start, weight=1.0, required=1, cid=None, name=None, desc=None, low=None, high=None):
    return dict(kind=kind, method=method, rec_min=rec_min, rec_max=rec_max, start=start, weight=weight, required=required,
                cid=cid or kind, name=name, desc=desc, low=low, high=high)


def rule(rid, a, a_min, a_max, feedback, penalty, b=None, b_min=None, b_max=None, blocks=0):
    return (rid, a, a_min, a_max, b, b_min, b_max, penalty, blocks, feedback)


WET_SPRAY = "Spraying while overhead watering keeps the foliage wet: the coating washes off and splash keeps spreading the disease."


def wet_spray(penalty=0.25):
    return rule("wet_spray", "drip", 0, 3, WET_SPRAY, penalty, "protect", 5, 10)


def contain_rule(text):
    return rule("not_contained", "containment", 0, 6, text, 0.5, blocks=1)


# (plant, disease): (severity, symptoms, controls, rules)
MANUAL = {
    ("tomato", "early_blight"): (0.35, ["Concentric, target-like brown spots", "Yellow halo around each spot", "Starts on older, lower leaves"],
        [ctl("sanitation", "sanitation", 6, 8, 2), ctl("drip", "cultural", 7, 10, 1),
         ctl("protect", "protect", 5, 7, 1, 0.8), ctl("prevention", "future", 5, 10, 0, 0.4, required=0)],
        [wet_spray()]),
    ("tomato", "bacterial_spot"): (0.3, ["Small, dark, water-soaked spots", "Spots visible on both leaf surfaces", "Spread by splash, hands and tools"],
        [ctl("sanitation", "hygiene", 6, 8, 2, name="Hygiene and selective removal"), ctl("drip", "irrigation", 8, 10, 1, 1.2),
         ctl("protect", "copper", 3, 5, 0, 0.5, name="Copper protection coverage (supplement)")],
        [rule("overhead", "drip", 0, 2, "Heavy overhead irrigation splashes bacteria from leaf to leaf.", 0.25)]),
    ("tomato", "late_blight"): (0.6, ["Large, greasy grey-green lesions", "White growth at lesion edges in humid air", "Lesions expand quickly"],
        [ctl("containment", "contain", 9, 10, 2, 1.5), ctl("protect", "protect", 6, 8, 1, name="Neighbour protection coverage",
             desc="Coverage of healthy neighbouring hosts with a late-blight-active protectant category. Abstract scale: no product or dose is taught."),
         ctl("replacement", "clean_stock", 7, 10, 1)],
        [contain_rule("Late blight spreads fast: without full containment the outbreak continues. The infected leaf cannot be saved.")]),
    ("tomato", "leaf_mold"): (0.35, ["Pale yellow patches on the upper surface", "Olive-brown velvety mould underneath", "Favoured by prolonged high humidity"],
        [ctl("airflow", "environment", 7, 9, 1, 1.3), ctl("sanitation", "sanitation", 5, 7, 1),
         ctl("protect", "protect", 3, 5, 0, 0.5, name="Protective spray coverage (supplement)")],
        [rule("closed_vents", "airflow", 0, 3, "Spraying with the vents closed will not hold leaf mold back.", 0.3, "protect", 4, 10)]),
    ("tomato", "septoria_leaf_spot"): (0.4, ["Many small circular spots with pale centres", "Tiny dark fruiting bodies (pycnidia)", "Spreads upward from lower leaves"],
        [ctl("sanitation", "sanitation", 6, 8, 2), ctl("drip", "cultural", 7, 10, 1), ctl("protect", "protect", 4, 6, 1, 0.7)],
        [wet_spray()]),
    ("tomato", "spider_mites"): (0.3, ["Pale stippling on the upper surface", "Fine webbing and moving mites underneath", "Worse on hot, dry, water-stressed plants"],
        [ctl("stress", "conditions", 6, 8, 2), ctl("biocontrol", "biological", 6, 8, 1), ctl("contact", "contact", 3, 5, 0, 0.7)],
        [rule("kills_predators", "biocontrol", 6, 10, "Heavy contact treatment right after a predator release kills the predators.", 0.3, "contact", 7, 10)]),
    ("tomato", "target_spot"): (0.4, ["Brown spots with concentric rings", "Inner-canopy leaves first", "Favoured by long leaf wetness"],
        [ctl("sanitation", "sanitation", 6, 8, 2), ctl("airflow", "drying", 7, 9, 1),
         ctl("protect", "programme", 6, 8, 1, name="Protective programme coverage",
             desc="Coverage with an effective protectant programme that rotates modes of action. Abstract scale: no product or dose is taught.")],
        [rule("wet_canopy", "airflow", 0, 3, "Long leaf wetness undermines any protectant programme.", 0.2, "protect", 6, 10)]),
    ("tomato", "yellow_leaf_curl_virus"): (0.55, ["Upward-curled, yellowed young leaves", "Stunted new growth", "Whiteflies on the underside"],
        [ctl("containment", "contain", 9, 10, 2, 1.5), ctl("vector", "vectors", 7, 10, 1, 1.2), ctl("replacement", "clean_stock", 7, 10, 1)],
        [contain_rule("There is no cure: an infected plant that is not contained stays a virus source."),
         rule("open_vectors", "vector", 0, 3, "Whiteflies left unmanaged carry the virus to the replacement.", 0.3)]),
    ("tomato", "mosaic_virus"): (0.5, ["Light and dark green mottling", "Distorted, puckered tissue", "Spreads on hands, tools and supports"],
        [ctl("containment", "remove", 9, 10, 2, 1.5),
         ctl("sanitation", "sanitation", 8, 10, 2, 1.2, name="Hygiene of hands, tools and surfaces",
             desc="Validated cleaning of hands, tools, supports and work surfaces after handling infected material."),
         ctl("replacement", "clean_stock", 7, 10, 1)],
        [contain_rule("Mosaic virus has no cure: infected material that stays in place keeps spreading mechanically."),
         rule("dirty_replant", "sanitation", 0, 4, "Planting clean stock with unsanitised hands and tools re-infects it.", 0.35, "replacement", 7, 10)]),
    ("apple", "apple_scab"): (0.3, ["Olive-dark, velvety spots", "Lesions on both surfaces", "Infections follow wet spring weather"],
        [ctl("sanitation", "sanitation", 6, 8, 2), ctl("airflow", "drying", 5, 7, 1, 0.8, name="Canopy drying / airflow"),
         ctl("protect", "protect", 6, 8, 1, name="Timed protective coverage"),
         ctl("prevention", "resistance", 6, 10, 0, 0.4, required=0, name="Resistant variety planning")],
        []),
    ("apple", "black_rot"): (0.3, ["Purple-edged frogeye spots", "Mummified fruit on the same tree", "Fungus survives in dead wood and cankers"],
        [ctl("sanitation", "sanitation", 8, 10, 2, 1.4, name="Sanitation thoroughness (mummies, dead wood)"), ctl("protect", "protect", 5, 7, 1)],
        [rule("mummies_left", "sanitation", 0, 4, "Spraying while mummified fruit stays in the tree leaves the main infection source in place.", 0.25, "protect", 5, 10)]),
    ("apple", "cedar_apple_rust"): (0.25, ["Bright orange-yellow spots", "Tufted spore structures underneath", "Galls on a nearby juniper"],
        [ctl("sanitation", "galls", 7, 9, 1, name="Gall removal", desc="Removal of accessible juniper galls before their spores are released."),
         ctl("protect", "protect", 5, 7, 1, name="Spring protective coverage"),
         ctl("prevention", "resistance", 6, 10, 0, 0.5, required=0, name="Resistant variety planning")],
        []),
    ("cherry", "powdery_mildew"): (0.35, ["White powdery growth on the surface", "Distorted young leaves", "Shaded, crowded shoots worst affected"],
        [ctl("airflow", "canopy", 6, 8, 1, name="Light and airflow (canopy)"),
         ctl("protect", "protect", 6, 8, 1, name="Mildew treatment coverage",
             desc="Coverage with a cherry-labelled mildew treatment category suited to stage and temperature. Abstract scale: no product or dose is taught.")],
        [rule("shaded", "airflow", 0, 2, "In a shaded, still canopy, treatment alone will not hold mildew back.", 0.2, "protect", 6, 10)]),
    ("peach", "bacterial_spot"): (0.3, ["Small angular spots that drop out (shot holes)", "Pitted, cracked fruit", "Worse on stressed trees"],
        [ctl("stress", "cultural", 7, 9, 2, 1.2, name="Tree stress relief (water, nutrition)"),
         ctl("monitoring", "copper", 6, 8, 1, name="Treatment-window records",
             desc="Recording the next label-permitted treatment stage instead of spraying copper on summer foliage."),
         ctl("prevention", "resistance", 6, 10, 0, 0.6, name="Resistant variety planning")],
        []),
    ("bell_pepper", "bacterial_spot"): (0.3, ["Small dark leaf spots", "Spots visible underneath", "Spotted transplants in the new tray"],
        [ctl("replacement", "clean_stock", 8, 10, 2, name="Clean seed and transplants",
             desc="Rejecting spotted transplants and using clean seed and stock."),
         ctl("sanitation", "hygiene", 6, 8, 2, name="Hygiene and selective removal"),
         ctl("protect", "copper", 3, 5, 0, 0.5, name="Copper protection coverage (supplement)")],
        []),
    ("strawberry", "leaf_scorch"): (0.4, ["Small purple spots", "Spots merge until the leaf looks scorched", "Survives on old leaves"],
        [ctl("drip", "water", 7, 10, 1), ctl("sanitation", "renovation", 6, 8, 1, name="Renovation thoroughness",
             desc="Removal of old foliage at post-harvest renovation without damaging the crowns.")],
        []),
    ("corn", "gray_leaf_spot"): (0.35, ["Rectangular tan lesions bounded by veins", "Lesions on both surfaces", "Survives in residue"],
        [ctl("protect", "fungicide", 6, 8, 1, 1.2, name="Foliar protection coverage"),
         ctl("prevention", "rotation", 6, 10, 1, 0.8, name="Rotation and residue management"),
         ctl("prevention", "resistance", 6, 10, 1, 0.6, cid="resistance", name="Resistant hybrid choice", desc="Choosing a resistant hybrid for the next crop.")],
        []),
    ("corn", "common_rust"): (0.2, ["Cinnamon-brown pustules", "Pustules on both surfaces", "Spores arrive on the wind"],
        [ctl("monitoring", "monitor", 6, 8, 1, 1.2),
         ctl("prevention", "resistance", 6, 10, 1, 0.8, cid="resistance", name="Resistant hybrid choice", desc="Choosing a resistant hybrid for later crops.")],
        []),
    ("corn", "northern_leaf_blight"): (0.3, ["Long, cigar-shaped grey-green lesions", "Lesions start on lower leaves", "Survives in residue"],
        [ctl("protect", "fungicide", 6, 8, 1, name="Foliar protection coverage"),
         ctl("prevention", "rotation", 6, 10, 1, 0.8, name="Rotation and residue management"),
         ctl("prevention", "resistance", 6, 10, 1, 0.6, cid="resistance", required=0, name="Resistant hybrid choice", desc="Choosing a resistant hybrid for the next crop.")],
        []),
    ("potato", "early_blight"): (0.35, ["Dark concentric lesions", "Older leaves first", "Worse on water-stressed plants"],
        [ctl("stress", "care", 6, 8, 2, name="Irrigation and nutrition (stress relief)"), ctl("protect", "protect", 5, 7, 1),
         ctl("prevention", "clean", 6, 10, 1, 0.6, required=0, name="Clean seed, rotation and volunteer control")],
        []),
    ("potato", "late_blight"): (0.6, ["Water-soaked, expanding lesions", "White growth underneath in humid air", "Reddish-brown rot in tubers"],
        [ctl("containment", "contain", 9, 10, 2, 1.5), ctl("protect", "protect", 6, 8, 1, name="Neighbour protection coverage",
             desc="Coverage of healthy neighbouring hosts with a late-blight-active protectant category. Abstract scale: no product or dose is taught."),
         ctl("replacement", "clean_seed", 7, 10, 1, name="Certified seed for the later crop")],
        [contain_rule("Late blight spreads fast: without full containment of foliage and tubers the outbreak continues.")]),
    ("grape", "black_rot"): (0.3, ["Tan spots with dark borders", "Berry mummies on the vine", "Follows wet weather"],
        [ctl("sanitation", "sanitation", 8, 10, 2, 1.3, name="Sanitation thoroughness (mummies)"), ctl("airflow", "drying", 6, 8, 1, name="Canopy drying / airflow"),
         ctl("protect", "protect", 5, 7, 1)],
        [rule("mummies_left", "sanitation", 0, 4, "Spraying while berry mummies stay on the vine leaves the main infection source in place.", 0.25, "protect", 5, 10)]),
    ("grape", "esca"): (0.55, ["Tiger-stripe patterns between the veins", "Dark streaks in the trunk wood", "Severe vine decline"],
        [ctl("containment", "replace", 9, 10, 2, 1.5, name="Containment and removal of the vine",
             desc="Isolating and removing the severely affected vine (assisted removal in the field)."),
         ctl("replacement", "replace", 7, 10, 1, name="Clean replacement vine"),
         ctl("prevention", "prevention", 7, 10, 1, 0.8, name="Pruning-wound protection",
             desc="Pruning in dry weather and protecting large pruning wounds on the replacement.")],
        [contain_rule("Esca lives in the wood: leaving the severely affected vine in place keeps the source. No foliar treatment cures it.")]),
    ("grape", "leaf_blight"): (0.4, ["Dark angular blotches", "Premature leaf drop", "Susceptible cultivar in a warm, humid region"],
        [ctl("protect", "protect", 6, 8, 1, 1.3, name="Disease-specific protection coverage"),
         ctl("monitoring", "cultivar", 6, 8, 1, name="Cultivar-matched monitoring", desc="Inspection frequency matched to the cultivar's susceptibility.")],
        []),
}


# Lab-only crops (Lab and local Treatment lab only; no Farm model): 34 cases and their manual profiles.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import treatment_content_lab_only  # noqa: E402
treatment_content_lab_only.register(sys.modules[__name__])


def severity_label(value):
    return "Mild" if value < 0.3 else "Moderate" if value < 0.5 else "Severe"


def manual_sql():
    out = ["", "-- ====================================================================  MANUAL TREATMENT MODE",
           "-- EXPERT REVIEW NEEDED: abstract 0-10 teaching scales, recommended ranges, start values, weights, rules,",
           "-- severities and recovery caps are illustrative values chosen by the developers. No products or doses.",
           "-- Each control references one of the case's own methods rows (method_id). Start values sit below the",
           "-- recommended range so the recommended value is never pre-selected."]
    for c in CASES:
        key = (c.plant, c.disease)
        if key not in MANUAL:
            continue
        severity, symptoms, controls, rules = MANUAL[key]
        caps = OUTCOME_CAPS[c.outcome]
        health = round(1.0 - severity * 0.85, 2)
        # Old lesions remain: a perfect treatment recovers at most 60% of the missing health.
        caps = (caps[0], round(min(caps[1], (1.0 - health) * 0.6), 2)) + caps[2:]
        method_rows = {m[0]: m for m in c.methods}
        description = "%s severity: about %d%% of the leaf area is affected (simulated estimate)." % (severity_label(severity), round(severity * 100))
        out.append("")
        out.append("-- %s / %s (manual treatment; expert review needed)" % key)
        out.append("INSERT INTO case_profile VALUES (%s);" % ", ".join(q(v) for v in (
            c.plant, c.disease, description, "|".join(symptoms), severity, health) + caps + (RECOVERY_NOTES[c.outcome],)))
        for i, k in enumerate(controls):
            name, desc, low, high = CONTROL_TYPES[k["kind"]]
            method = method_rows[k["method"]]  # KeyError here means the control is not derived from a real method row
            ok = "Within the recommended range. Limitation: %s" % method[3]
            out.append("INSERT INTO treatment_controls VALUES (%s);" % ", ".join(q(v) for v in (
                c.plant, c.disease, k["cid"], (i + 1) * 10, k["method"], k["name"] or name, k["desc"] or desc,
                0, 10, 1, k["start"], k["rec_min"], k["rec_max"], k["weight"], k["required"],
                k["low"] or low, k["high"] or high, ok)))
        for r in rules:
            out.append("INSERT INTO control_rules VALUES (%s);" % ", ".join(q(v) for v in (c.plant, c.disease) + r))
    return out


# =====================================================================================  VALIDATION
# Mirrors the structural rules of TreatmentCaseValidator.Validate / ValidateManual (C#), so a case that Unity would
# refuse fails here, before the SQL is written. Asset bindings are checked in Unity (they need the asset catalog).

PANEL_ACTIONS = {"choose", "advance"}
ACTIONS = {"inspect", "choose", "cut", "collect", "dispose", "clean_tool", "irrigate", "control", "spray", "place", "release",
           "contain", "remove_plant", "plant", "advance", "observe"}


def validate():
    problems = []
    keys = [(c.plant, c.disease) for c in CASES]
    if len(set(keys)) != len(keys):
        problems.append("duplicate (plant_id, disease_id) keys")
    tools = {t[0] for t in TOOLS}
    for c in CASES:
        label = "%s/%s" % (c.plant, c.disease)
        p = lambda text: problems.append("%s: %s" % (label, text))
        if c.plant not in PLANT_NAMES or c.plant not in ENVIRONMENT_FOR:
            p("plant has no display name or environment")
        if c.outcome not in ("Managed", "PestSuppressed", "Replaced"):
            p("unknown outcome")
        if not c.evidence or any(not e[1] for e in c.evidence):
            p("needs evidence with a URL")
        if not c.methods or any(not m[3] for m in c.methods):
            p("every method needs limitations")
        steps = c.steps
        ids = [s["sid"] for s in steps]
        if len(set(ids)) != len(ids):
            p("duplicate step IDs")
        if not any(s["action"] == "observe" for s in steps):
            p("no follow-up observation")
        fixtures = {f[1]: f[3] for f in FIXTURES if f[5] is None or f[5] in c.modules}
        tags = {t[1] for t in c.targets} | set(fixtures)
        slots = {}
        for t in c.targets:
            slots.setdefault(t[1], []).append(t[4])
        routes = {o[3] for s in steps for o in s["options"] if o[2] and o[3]}
        followups = {f[0] for f in c.followups}
        for s in steps:
            if s["action"] not in ACTIONS:
                p("step %s has unknown action %s" % (s["sid"], s["action"]))
            for r in s["requires"]:
                if r not in ids:
                    p("step %s requires unknown step %s" % (s["sid"], r))
            if s["route"] and s["route"] not in routes:
                p("step %s route %s is never chosen" % (s["sid"], s["route"]))
            if s["action"] in PANEL_ACTIONS:
                if s["action"] == "choose" and not any(o[2] for o in s["options"]):
                    p("choice %s has no valid option" % s["sid"])
                continue
            if not s["tag"] or s["tag"] not in tags:
                p("step %s targets %s, which is not placed (check modules)" % (s["sid"], s["tag"]))
            if s["tool"] and (s["tool"] not in tools or s["tool"] not in c.tools):
                p("step %s tool %s is unknown or not on the rack" % (s["sid"], s["tool"]))
            if s["action"] == "observe" and s["checkpoint"] not in followups:
                p("step %s records missing follow-up %s" % (s["sid"], s["checkpoint"]))
        for i, s in enumerate(steps):
            if s["action"] in ("cut", "collect") and not any(x["action"] == "dispose" for x in steps[i + 1:]):
                p("step %s removes material but nothing later disposes of it" % s["sid"])
        removal = next((i for i, s in enumerate(steps) if s["action"] == "remove_plant"), None)
        if removal is not None:
            for s in steps[removal + 1:]:
                placed = slots.get(s["tag"], [])
                if placed and all(x.startswith("plant_") or x.startswith("leaf_") or x == "root_zone" for x in placed):
                    p("step %s targets the removed original" % s["sid"])
        for t in c.targets:
            for after in (t[6], t[7]):
                if after and after not in ids:
                    p("target %s refers to unknown step %s" % (t[0], after))
        if c.outcome == "Replaced":
            if not any(s["action"] == "plant" for s in steps):
                p("replacement lesson plants no clean stock")
            if not any(s["action"] in ("contain", "remove_plant") for s in steps):
                p("replacement lesson never contains the original")
            if any(s["action"] == "spray" and s["tag"] == "infected_plant" for s in steps):
                p("replacement lesson treats the original as curable")
        # Manual profile
        severity, symptoms, controls, rules = MANUAL[(c.plant, c.disease)]
        methods = {m[0] for m in c.methods}
        cids = [k["cid"] for k in controls]
        if not symptoms or len(controls) < 2 or not any(k["required"] for k in controls) or len(set(cids)) != len(cids):
            p("manual profile needs symptoms, two or more unique controls and a required one")
        for k in controls:
            if k["method"] not in methods:
                p("control %s refers to unknown method %s" % (k["cid"], k["method"]))
            if not (0 <= k["start"] < k["rec_min"] <= k["rec_max"] <= 10):
                p("control %s range is invalid or pre-selected" % k["cid"])
            if any(float(v) != int(v) for v in (k["start"], k["rec_min"], k["rec_max"])):
                p("control %s is off the step grid" % k["cid"])
        for r in rules:
            if r[1] not in cids or (r[4] and r[4] not in cids) or not (0 <= r[7] <= 1):
                p("rule %s refers to an unknown control or has a bad penalty" % r[0])
        if c.outcome == "Replaced":
            containment = next((k for k in controls if k["cid"] == "containment"), None)
            if containment is None or not containment["required"]:
                p("replacement case needs a required containment control")
            if not any(r[8] and r[1] == "containment" for r in rules):
                p("replacement case needs a blocking containment rule")
    return problems


# =====================================================================================  OUTPUT

def build():
    lines = ["-- Treatment content database: canonical, reviewable authoring source.",
             "-- Generated by Tools/treatment_content_gen.py from Docs/DataBase_Plan.md. Educational cases only:",
             "-- no pesticide products, doses, mixing or intervals (those follow the applicable local label).",
             "PRAGMA foreign_keys = ON;", SCHEMA.strip(), MANUAL_SCHEMA.strip(), "BEGIN;",
             "INSERT INTO content_info VALUES ('content_version', '%d');" % CONTENT_VERSION,
             "INSERT INTO content_info VALUES ('default_case', 'tomato/early_blight');"]
    for t in TOOLS:
        lines.append("INSERT INTO tools VALUES (%s);" % ", ".join(q(v) for v in t))
    for e in ENVIRONMENTS:
        lines.append("INSERT INTO environments VALUES (%s);" % ", ".join(q(v) for v in e))
        for f in FIXTURES:
            lines.append("INSERT INTO environment_fixtures VALUES (%s);" % ", ".join(q(v) for v in (e[0],) + f))
    for c in CASES:
        lines.append("")
        lines.append("-- %s / %s" % (c.plant, c.disease))
        lines.extend(c.sql())
    missing = [k for k in ((c.plant, c.disease) for c in CASES) if k not in MANUAL]
    if missing:
        raise SystemExit("Manual treatment data missing for: %s" % missing)
    problems = validate()
    if problems:
        raise SystemExit("Content problems:\n  " + "\n  ".join(problems))
    lines.extend(manual_sql())
    lines.append("COMMIT;")
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print("Wrote %d cases to %s" % (len(CASES), os.path.normpath(OUT)))


if __name__ == "__main__":
    build()