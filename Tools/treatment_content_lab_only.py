"""Lesson content for the 34 Lab-only conditions (10 crops without a Farm model).

Registered by Tools/treatment_content_gen.py, which owns the schema, helpers and SQL output, so one run
regenerates all 58 cases. Same rules as the original 24: educational cases only, no pesticide product
names, doses, mixing instructions or application intervals; the outcome (Managed, PestSuppressed or
Replaced) says honestly what the simulated action achieved. Every case cites crop-specific sources.
EXPERT REVIEW NEEDED: as for the original cases, manual-mode ranges, start values, weights, rules and
severities are illustrative teaching values chosen by the developers, not agronomic guidance.
"""

REVIEWED = "2026-11-12"
FIELD_STRIP = "This lesson keeps the leaf working: field-crop leaves are protected, not cut."


def register(g):
    """Adds the 34 cases and their manual-mode profiles to the generator module ``g``."""
    case, ctl, rule = g.case, g.ctl, g.rule
    leaf, new_leaf, contained_leaf, inspect_pair = g.leaf, g.new_leaf, g.contained_leaf, g.inspect_pair
    humidity, drip, cut, protect, recheck = g.humidity, g.drip, g.cut, g.protect, g.recheck
    wet_spray, contain_rule, PROTECT_FEEDBACK = g.wet_spray, g.contain_rule, g.PROTECT_FEEDBACK
    MANUAL = g.MANUAL

    def field_leaf(c, label, later="Later leaf from the same crop"):
        leaf(c, label)
        new_leaf(c, later)
        c.default_wrong_target = FIELD_STRIP

    def monitor_only(c, title, lines, cure_claim):
        """Assessment where the valid route is monitoring: nothing sprayed repairs these lesions."""
        protect(c, title, lines,
                [("Keep the corrected conditions and monitor the crop", True, "monitor",
                  "Correct: for this case a spray would add cost without changing the outcome."),
                 (cure_claim, False, None, "No treatment repairs existing lesions, and this pathogen is not controlled that way.")])

    def plan(c, correct, wrong, why):
        c.choose("future", "Treat", "Plan the next crop", ["Choose for the next season"],
                 [(correct, True, None, "Correct."), (wrong, False, None, why)])

    def vector_replacement(c, symptom, vector_lines, confirmed, decide, mesh_line, card_line, plant_line,
                           finding, escalation, vector_target=False):
        """Virus spread by an insect vector: no cure, so contain, manage the vector and replant clean stock."""
        leaf(c, symptom, lesions=2, healthy=False)
        if vector_target:
            c.target("whiteflies", "whiteflies", "target.whiteflies", "leaf_underside", "Whiteflies", hide="bag")
        contained_leaf(c)
        c.default_wrong_target = "There is no cure. Follow the containment steps."
        c.inspect("inspect", "lesion_spot", "Inspect the symptoms", [symptom], count=1)
        if vector_target:
            c.inspect("vector", "whiteflies", "Inspect for the vector", vector_lines)
        c.inspect("confirm_result", "lab_result", "Read the simulated confirmation", ["Read the diagnostic result"], tool=None,
                  success=confirmed)
        c.choose("confirm", "Inspect", "Decide the management", ["No treatment cures infected tissue"], decide)
        c.step("bag", "Prepare", "Contain the infected leaf", ["Bag it before moving it"], "contain", "containment_bag", "infected_plant",
               success="Infected material enclosed.")
        c.step("remove", "Prepare", "Move to containment", ["Place the bag in the covered compartment"], "remove_plant", None, "infected_plant",
               success="Contained.")
        if mesh_line:
            c.step("mesh", "Treat", "Exclude the vector", [mesh_line], "place", "insect_mesh", "mesh_frame", success="Exclusion in place.")
        c.step("card", "Treat", "Set vector monitoring", [card_line], "place", "sticky_card", "mesh_frame", success="Monitoring card in place.")
        c.step("plant_clean", "Treat", "Plant clean stock", [plant_line], "plant", "transplant", "clean_pot", success="Healthy replacement established.")
        c.advance(lines=("Simulated later observation",))
        c.observe("check_replacement", "replacement", "replacement", "Monitor the replacement", ["Inspect the new plant"])
        c.followup("replacement", "Inspect the replacement", finding, escalation)

    def soil_replacement(c, symptom, look, confirmed, decide, clean_lines, future, plant_line, finding, escalation):
        """Systemic or soil-borne disease: the affected plant is not cured; the next planting starts clean."""
        leaf(c, symptom, lesions=2, healthy=False)
        contained_leaf(c, "Leaf sample from the affected plant")
        c.default_wrong_target = "Leaf treatment cannot cure this infection. Follow the panel."
        c.inspect("inspect", "lesion_spot", "Inspect the symptoms", look, count=1)
        c.inspect("confirm_result", "lab_result", "Read the simulated confirmation", ["Read the diagnostic result"], tool=None,
                  success=confirmed)
        c.choose("confirm", "Inspect", "Decide the management", ["The infection is inside the plant, not on the leaf surface"], decide)
        c.step("bag", "Prepare", "Bag the sample", ["Enclose the sample before moving it"], "contain", "containment_bag", "infected_plant",
               success="Sample enclosed.")
        c.step("remove", "Prepare", "Move it to containment", ["The affected plant itself is removed in the field (assisted)"], "remove_plant",
               None, "infected_plant", success="Infected material removed.")
        c.clean(lines=clean_lines)
        c.choose("future", "Treat", "Plan the replanting", ["Choose for the next planting"], future)
        c.step("plant_clean", "Treat", "Establish clean stock", [plant_line], "plant", "transplant", "clean_pot", success="Clean replacement planted.")
        c.advance(lines=("Simulated later season",))
        c.observe("check_replacement", "replacement", "replacement", "Inspect the replacement", ["Inspect the new planting"])
        c.followup("replacement", "Inspect the replacement", finding, escalation)

    def replaced_manual(key, severity, symptoms, contain_method, contain_name, contain_desc, others, rules):
        MANUAL[key] = (severity, symptoms,
                       [ctl("containment", contain_method, 9, 10, 2, 1.5, name=contain_name, desc=contain_desc)] + others,
                       [contain_rule(rules[0])] + list(rules[1:]))

    # ==================================================================================  BLUEBERRY (orchard room)

    c = g.case("blueberry", "rust", "Leaf rust", "Fungal", "Managed",
               "A highbush blueberry leaf with leaf rust in late summer after a run of long, wet nights.",
               "Leaf rust (Thekopsora / Pucciniastrum) needs an alternate conifer host (hemlock) and about two days of leaf wetness to infect. "
               "Yellow spots above and orange spore pustules below lead to early leaf drop.",
               "Disease managed: rusted tissue removed, foliage kept drier and healthy leaves protected. Existing spots remain.",
               modules=["irrigation", "planning"], tools=["water_wand"])
    leaf(c, "Rust spot"); new_leaf(c, "Leaf from a later flush")
    c.method("sanitation", "Remove rusted leaves and fallen leaf litter", "Removes spores close to the bush", "Windborne spores from the alternate host still arrive.")
    c.method("cultural", "Limit overhead irrigation and open the canopy", "Shortens leaf wetness", "Does not remove existing infection.")
    c.method("protect", "Labelled protection once scouting finds rust", "Protects healthy leaves when pressure is high", "Does not cure spotted leaves; follow the local label.")
    c.method("resistance", "Less susceptible cultivars", "Lower risk in new plantings", "Susceptibility varies by cultivar; not an immediate treatment.")
    inspect_pair(c, ["Yellow to reddish spots on the upper surface"], ["Orange, powdery rust pustules on the underside"])
    c.confirm("Yellow spots above, orange pustules below (leaf rust)",
              [("White powdery coating on the surface", "A white surface coating indicates powdery mildew, not rust.")])
    cut(c, title="Remove rusted tissue")
    drip(c, "Long leaf wetness drives rust infection")
    protect(c, "Assess protection", ["Scouting found rust", "Wet nights continue"], g.PROTECT_OR_MONITOR)
    recheck(c, "The later leaf is free of new pustules in this simulation.", "If rust spreads, reassess protection and check nearby hemlock.")
    c.source("Michigan State University Extension", "https://www.canr.msu.edu/news/be_on_the_lookout_for_leaf_rust_in_michigan_blueberry_fields",
             REVIEWED, "Michigan, USA", "Regional guidance; rust life cycle depends on a nearby alternate host.")
    c.source("UF/IFAS Extension", "https://edis.ifas.ufl.edu/publication/PP385", REVIEWED, "Florida, USA",
             "Southern highbush systems; timing differs from northern plantings.")
    MANUAL[("blueberry", "rust")] = (0.3, ["Yellow to reddish spots on the upper surface", "Orange pustules underneath", "Early leaf drop after wet weather"],
        [ctl("sanitation", "sanitation", 5, 7, 1), ctl("drip", "cultural", 7, 10, 1, name="Leaf-wetness reduction (drip, open canopy)"),
         ctl("protect", "protect", 5, 7, 1, 0.8), ctl("prevention", "resistance", 5, 10, 0, 0.4, required=0, name="Cultivar planning")],
        [wet_spray()])

    # ==================================================================================  CHILLI (greenhouse room)

    c = case("chilli", "anthracnose", "Anthracnose", "Fungal", "Managed",
             "A chilli leaf with anthracnose spots from a humid planting where fruit rot has also been seen.",
             "Colletotrichum spp. cause small sunken spots with dark rings on leaves and serious fruit rot. The fungus is seed-borne and "
             "spreads in rain splash, favoured by high humidity.",
             "Disease managed: affected tissue removed, splash reduced and healthy tissue protected. Existing spots remain.",
             modules=["irrigation"], tools=["water_wand"])
    leaf(c, "Ringed anthracnose spot"); new_leaf(c)
    c.method("sanitation", "Remove affected leaves and fruit", "Removes the spore source", "Spots on retained tissue stay.")
    c.method("seed", "Disease-free or treated seed", "Stops the seed-borne start of the disease", "Prevention for the next crop.")
    c.method("cultural", "Drip irrigation and spacing", "Reduces splash and humidity", "Does not remove existing infection.")
    c.method("protect", "Protective fungicide where justified", "Protects healthy tissue and fruit", "Does not cure spots; follow the local label.")
    inspect_pair(c, ["Small sunken brown spots with dark rings", "Yellowing around the spots"], ["The spots show through underneath"])
    c.confirm("Small ringed spots (anthracnose)",
              [("Round spots with pale grey centres and dark margins", "Pale-centred frogeye spots indicate Cercospora leaf spot."),
               ("Puckered, curled leaf without spots", "Puckering and curling indicate leaf curl virus.")])
    cut(c)
    drip(c)
    protect(c, "Assess protection", ["Humid weather continues", "Fruit is setting"], g.PROTECT_OR_MONITOR)
    recheck(c, "The new leaf is clean in this simulation.", "If fruit spots appear, remove them and reassess protection.")
    c.source("TNAU Agritech Portal (Tamil Nadu Agricultural University)", "https://agritech.tnau.ac.in/crop_protection/chilli_phdiseases_2.html",
             REVIEWED, "Tamil Nadu, India", "Older page; product recommendations are not used here, check current local labels.")
    c.source("NIPHM (Govt. of India) IPM package for chilli", "https://niphm.gov.in/IPMPackages/Chilli.pdf", REVIEWED, "India",
             "National IPM package; regional registrations differ.")
    c.specimen_note = "Specimen shows strong chlorosis as well as anthracnose spots."
    MANUAL[("chilli", "anthracnose")] = (0.35, ["Small sunken spots with dark rings", "Yellowing around the spots", "Fruit rot in humid weather"],
        [ctl("sanitation", "sanitation", 6, 8, 2), ctl("drip", "cultural", 7, 10, 1), ctl("protect", "protect", 5, 7, 1, 0.8),
         ctl("prevention", "seed", 6, 10, 0, 0.4, required=0, name="Clean seed planning")],
        [wet_spray()])

    c = case("chilli", "cercospora_leaf_spot", "Cercospora leaf spot", "Fungal", "Managed",
             "A chilli leaf with frogeye spots from a crowded, wet planting.",
             "Cercospora capsici causes round spots with pale grey centres and dark brown margins (frogeye). Heavy spotting makes leaves drop.",
             "Disease managed: spotted tissue removed and healthy tissue protected. Existing spots remain.",
             modules=["irrigation", "ventilation"], tools=["water_wand"])
    leaf(c, "Frogeye spot"); new_leaf(c)
    c.method("sanitation", "Remove spotted leaves and crop debris", "Removes the spore source", "Spots on retained tissue stay.")
    c.method("cultural", "Spacing, airflow and drip irrigation", "Shortens leaf wetness", "Must be maintained.")
    c.method("protect", "Protective fungicide where justified", "Protects healthy tissue", "Does not cure spots; follow the local label.")
    c.method("rotation", "Rotation away from peppers", "Lowers inoculum in later crops", "Not an immediate treatment.")
    inspect_pair(c, ["Round spots with pale grey centres", "Dark brown margins (frogeye)"], ["The spots show on the underside too"])
    c.confirm("Round spots with grey centres and dark margins (Cercospora leaf spot)",
              [("Small sunken spots with dark rings and fruit rot", "That pattern points to anthracnose.")])
    cut(c)
    humidity(c, "Crowded, humid canopies favour Cercospora")
    protect(c, "Assess protection", ["Spots are spreading"], g.PROTECT_OR_MONITOR)
    recheck(c, "The new leaf has no fresh spots in this simulation.", "If spots spread upward, reassess spacing and protection.")
    c.source("TNAU Agritech Portal (Tamil Nadu Agricultural University)", "https://agritech.tnau.ac.in/crop_protection/chilli_diseases_5.html",
             REVIEWED, "Tamil Nadu, India", "Regional guidance; product choice follows local labels.")
    c.source("NIPHM (Govt. of India) IPM package for chilli", "https://niphm.gov.in/IPMPackages/Chilli.pdf", REVIEWED, "India",
             "National IPM package.")
    MANUAL[("chilli", "cercospora_leaf_spot")] = (0.35, ["Round spots with pale grey centres", "Dark brown margins", "Leaf drop when spotting is heavy"],
        [ctl("sanitation", "sanitation", 6, 8, 2), ctl("airflow", "cultural", 6, 8, 1, name="Spacing and airflow"),
         ctl("protect", "protect", 5, 7, 1, 0.8), ctl("prevention", "rotation", 5, 10, 0, 0.4, required=0, name="Rotation planning")],
        [rule("crowded", "airflow", 0, 3, "In a crowded, humid canopy, spraying alone will not hold the spots back.", 0.2, "protect", 5, 10)])

    c = case("chilli", "leaf_curl", "Leaf curl virus", "Viral", "Replaced",
             "A puckered, curled chilli leaf from a plant with whiteflies, in a hot, dry season.",
             "Chilli leaf curl begomoviruses are spread by whiteflies. Infected plants cannot be cured; management protects healthy plants.",
             "Healthy replacement established. The infected plant was contained, not cured; whitefly monitoring continues.",
             modules=["confirmation", "containment", "replacement", "mesh"], tools=["shears", "sprayer"])
    c.method("contain", "Remove infected plants early", "Stops them acting as a virus source", "Infected tissue is not cured.")
    c.method("vectors", "Whitefly exclusion, monitoring and weed-host removal", "Prevents reinfection", "Requires continued monitoring.")
    c.method("clean_stock", "Healthy, tolerant seedlings raised under insect net", "Healthy replacement", "Tolerance may not prevent all symptoms.")
    vector_replacement(c, "Puckered, curled leaf tissue",
                       ["Turn the leaf: small white insects underneath"], "Chilli leaf curl virus confirmed (simulated).",
                       [("Contain the plant, manage whiteflies, replant clean seedlings", True, None, "Correct: there is no curative treatment."),
                        ("Spray the leaf to cure the virus", False, None, "No spray cures a virus infection.")],
                       "Fit insect mesh over the clean seedling area", "Hang the whitefly-monitoring card",
                       "Plant the healthy seedling raised under insect net",
                       "Healthy replacement established; few whiteflies on the card.", "If whitefly counts rise, review exclusion and weed hosts.",
                       vector_target=True)
    c.source("NIPHM (Govt. of India) IPM package for chilli", "https://niphm.gov.in/IPMPackages/Chilli.pdf", REVIEWED, "India",
             "National IPM package; regional registrations differ.")
    c.source("Frontiers in Plant Science (review)", "https://www.frontiersin.org/journals/plant-science/articles/10.3389/fpls.2023.1223982/epub",
             REVIEWED, "Asia", "Research review; resistance breeding status varies.")
    replaced_manual(("chilli", "leaf_curl"), 0.55, ["Puckered, upward-curled leaves", "Stunted new growth", "Whiteflies underneath"],
                    "contain", "Containment", None,
                    [ctl("vector", "vectors", 7, 10, 1, 1.2), ctl("replacement", "clean_stock", 7, 10, 1)],
                    ["There is no cure: an infected plant that is not contained stays a virus source.",
                     rule("open_vectors", "vector", 0, 3, "Whiteflies left unmanaged carry the virus to the replacement.", 0.3)])

    # ==================================================================================  COTTON (field room)

    c = case("cotton", "bacterial_blight", "Bacterial blight", "Bacterial", "Managed",
             "A cotton leaf with angular lesions after storms; the field was planted with saved seed.",
             "Xanthomonas citri pv. malvacearum causes angular, water-soaked lesions bounded by veins. It survives in seed and residue and "
             "spreads in wind-driven rain.",
             "Spread reduced for the next crop. Fungicides do not control bacteria, and the existing lesions remain.",
             modules=["irrigation", "planning"], tools=["shears", "sprayer"])
    field_leaf(c, "Angular lesion")
    c.method("resistance", "Resistant varieties", "Main defence", "For the next crop.")
    c.method("seed", "Clean (delinted) planting seed", "Stops the seed-borne start of the disease", "Prevention for the next crop.")
    c.method("residue", "Residue destruction and rotation", "Reduces the carry-over source", "Not an in-season cure.")
    c.method("cultural", "Avoid overhead irrigation and working wet plants", "Reduces spread in splash", "Does not remove existing infection.")
    inspect_pair(c, ["Angular, water-soaked lesions bounded by veins", "Dry lesions tear out of the leaf"], ["Lesions show through underneath"])
    c.confirm("Angular lesions bounded by veins (bacterial blight)",
              [("Yellowing and browning from the leaf edge inward", "Marginal yellowing and wilting point to Fusarium wilt.")])
    drip(c, "Splashing water spreads the bacteria")
    monitor_only(c, "Choose management", ["Is a spray justified for a bacterial disease?"], "Spray a fungicide to cure the lesions")
    plan(c, "Resistant variety, clean seed, residue destroyed and rotation", "Replant saved seed into the same residue",
         "Saved seed and residue carry the bacteria into the next crop.")
    recheck(c, "New leaves stay largely clean in this simulation.", "Note the field for a resistant variety next season.")
    c.source("Crop Protection Network", "https://cropprotectionnetwork.org/encyclopedia/bacterial-blight-of-cotton", REVIEWED,
             "Southern USA", "US cotton belt guidance; races differ by region.")
    c.source("Texas A&M AgriLife Extension", "https://ccag.tamu.edu/wp-content/uploads/sites/10/2020/12/16_FS_FC010_Cot_Bact_Bl.pdf",
             REVIEWED, "Texas, USA", "Regional fact sheet.")
    MANUAL[("cotton", "bacterial_blight")] = (0.3, ["Angular, water-soaked lesions between veins", "Dry lesions tear out", "Follows wind-driven rain"],
        [ctl("prevention", "resistance", 6, 10, 1, 1.2, cid="resistance", name="Resistant variety choice",
             desc="Choosing a resistant variety for the next crop."),
         ctl("sanitation", "residue", 6, 8, 1, name="Residue destruction and rotation",
             desc="Destroying infected residue and rotating away from cotton."),
         ctl("drip", "cultural", 7, 10, 1, 0.8)],
        [])

    c = case("cotton", "fusarium_wilt", "Fusarium wilt", "Fungal", "Replaced",
             "A leaf from a wilting cotton plant; a cut stem shows brown streaks in the water-conducting tissue.",
             "Fusarium oxysporum f. sp. vasinfectum lives in soil, enters through roots and blocks the vascular system. Nematodes worsen it. "
             "No leaf treatment reaches the infection.",
             "A clean, resistant replacement is planted. The wilted plant was not cured.",
             modules=["confirmation", "containment", "replacement", "planning"], tools=["sprayer"])
    c.method("contain", "Remove affected plants and keep infested soil in place", "Stops spread on equipment and in soil", "The affected plant is not cured.")
    c.method("hygiene", "Clean equipment, tools and boots", "Avoids moving infested soil", "Must be routine.")
    c.method("resistant", "Resistant varieties and nematode management", "Restores production on infested fields", "No immune varieties exist.")
    soil_replacement(c, "Yellowing and browning leaf margins",
                     ["Yellowing and browning from the margin", "The plant wilts despite moist soil"],
                     "Fusarium wilt confirmed: vascular browning in the stem (simulated).",
                     [("Remove the plant, stop soil movement, replant a resistant variety", True, None, "Correct: the infection is in the vascular system."),
                      ("Spray the leaves to cure the wilt", False, None, "No foliar spray reaches a vascular, soil-borne infection.")],
                     ("Clean soil off tools and boots", "Do not carry infested soil to clean fields"),
                     [("A resistant variety with nematode management", True, None, "Correct."),
                      ("The same susceptible variety", False, None, "The fungus persists in the soil for years.")],
                     "Plant the resistant variety in clean soil",
                     "A healthy, resistant replacement.", "Report wilting to an adviser so the strain can be identified.")
    c.source("Crop Protection Network", "https://cropprotectionnetwork.org/encyclopedia/fusarium-wilt-of-cotton", REVIEWED,
             "USA", "Race 4 (FOV4) differs from older races; check local status.")
    c.source("Cotton Incorporated", "https://www.cottoninc.com/wp-content/uploads/2019/04/FOV-4-Two-page-Producer-Handout.pdf", REVIEWED,
             "USA", "Producer handout on race 4.")
    replaced_manual(("cotton", "fusarium_wilt"), 0.55, ["Yellowing and browning from the leaf margin", "Wilting despite moist soil", "Brown vascular streaks"],
                    "contain", "Removal of the affected plant", "Removing the affected plant without moving infested soil.",
                    [ctl("sanitation", "hygiene", 7, 10, 2, name="Equipment and soil hygiene",
                         desc="Cleaning soil off tools, boots and machinery before entering clean fields."),
                     ctl("replacement", "resistant", 7, 10, 1, name="Resistant variety for replanting")],
                    ["The wilt lives in soil and the vascular system: leaving the affected plant in place keeps the source."])

    c = case("cotton", "leaf_curl_virus", "Leaf curl virus", "Viral", "Replaced",
             "A cupped, thick-veined cotton leaf from a plant with whiteflies.",
             "Cotton leaf curl begomoviruses are spread by whiteflies. Leaves cup and veins thicken; infected plants cannot be cured.",
             "Healthy replacement established. The infected plant was contained, not cured; whitefly monitoring continues.",
             modules=["confirmation", "containment", "replacement", "mesh"], tools=["shears", "sprayer"])
    c.method("contain", "Remove infected plants early", "Stops them acting as a virus source", "Infected tissue is not cured.")
    c.method("vectors", "Whitefly monitoring at thresholds and weed-host removal", "Limits reinfection", "Broad-spectrum sprays can flare whiteflies.")
    c.method("clean_stock", "Tolerant varieties", "Healthy replacement", "No variety is fully resistant to every virus strain.")
    vector_replacement(c, "Cupped leaf with thickened veins",
                       ["Turn the leaf: small white insects underneath"], "Cotton leaf curl virus confirmed (simulated).",
                       [("Contain the plant, manage whiteflies, replant a tolerant variety", True, None, "Correct: there is no curative treatment."),
                        ("Spray the leaf to cure the virus", False, None, "No spray cures a virus infection.")],
                       None, "Hang the whitefly-monitoring card", "Plant the tolerant variety",
                       "Healthy replacement established; whiteflies below threshold on the card.",
                       "If counts pass the threshold, follow the local whitefly guidance.", vector_target=True)
    c.source("USDA-ARS (National Plant Disease Recovery System)", "https://www.ars.usda.gov/ARSUserFiles/OPMP/CLCuVRecoveryPlan_FINAL_Dec2.17.pdf",
             REVIEWED, "USA / South Asia", "Recovery plan; describes the threat and research needs.")
    c.source("University of Georgia Extension", "https://ipm.uga.edu/files/2020/07/Whiteflies_2020.pdf", REVIEWED, "Georgia, USA",
             "Whitefly thresholds are regional.")
    replaced_manual(("cotton", "leaf_curl_virus"), 0.55, ["Cupped, curled leaves", "Thickened, dark veins", "Whiteflies underneath"],
                    "contain", "Containment", None,
                    [ctl("vector", "vectors", 6, 9, 1, 1.2, name="Whitefly management at threshold"),
                     ctl("replacement", "clean_stock", 7, 10, 1, name="Tolerant variety")],
                    ["There is no cure: an infected plant that is not contained stays a virus source.",
                     rule("open_vectors", "vector", 0, 3, "Whiteflies left unmanaged carry the virus to the replacement.", 0.3)])

    # ==================================================================================  MANGO (orchard room)

    c = case("mango", "anthracnose", "Anthracnose", "Fungal", "Managed",
             "A mango leaf from a new flush with anthracnose spots during wet weather before flowering.",
             "Colletotrichum spp. infect young leaves, flowers and fruit in wet weather; the fungus survives on old leaves and twigs.",
             "Disease managed: infected tissue pruned, the canopy dried and new flush protected. Existing spots remain.",
             modules=["ventilation", "risk"], tools=["water_wand"])
    leaf(c, "Anthracnose spot"); new_leaf(c, "Leaf from the next flush")
    c.method("sanitation", "Prune infected twigs and remove fallen leaves", "Removes the spore source", "Spots on retained leaves stay.")
    c.method("canopy", "Open, well-pruned canopy", "Faster drying after rain", "Must be maintained.")
    c.method("protect", "Protection of new flush and flowers in wet weather", "Protects susceptible growth", "Does not cure spots; follow the local label.")
    inspect_pair(c, ["Dark brown to black irregular spots", "Spots merge on young leaves"], ["The spots show underneath"])
    c.confirm("Dark irregular spots on young leaves (anthracnose)",
              [("Raised, angular black spots with yellow halos", "Raised angular spots point to bacterial black spot.")])
    cut(c, title="Prune affected tissue")
    humidity(c, "Wet weather on young flush drives anthracnose")
    c.inspect("risk", "risk_card", "Read the risk card", ["Wet forecast", "New flush and flowering coming"], tool=None, success="Infection risk: high.")
    protect(c, "Choose protection", ["Protect the next flush"], g.PROTECT_OR_MONITOR)
    recheck(c, "The next flush is clean in this simulation.", "Keep protecting flowers in wet weather.")
    c.source("Queensland Department of Agriculture and Fisheries", "https://www.daf.qld.gov.au/business-priorities/agriculture/plants/fruit-vegetable/diseases-disorders/anthracnose",
             REVIEWED, "Queensland, Australia", "Regional guidance; registrations differ by country.")
    c.source("PestNet factsheet", "https://www.pestnet.org/fact_sheets/mango_anthracnose_009.htm", REVIEWED, "Pacific region", "Factsheet summary.")
    MANUAL[("mango", "anthracnose")] = (0.35, ["Dark brown to black irregular spots", "Young leaves worst affected", "Spreads in wet weather"],
        [ctl("sanitation", "sanitation", 6, 8, 2), ctl("airflow", "canopy", 6, 8, 1, name="Canopy opening / airflow"),
         ctl("protect", "protect", 6, 8, 1, name="Flush and flower protection coverage")],
        [rule("wet_canopy", "airflow", 0, 3, "In a dense, wet canopy, protection alone will not hold anthracnose back.", 0.2, "protect", 6, 10)])

    c = case("mango", "bacterial_canker", "Bacterial canker (black spot)", "Bacterial", "Managed",
             "A mango leaf with raised black spots from a windy, exposed block.",
             "Xanthomonas citri pv. mangiferaeindicae enters through wounds, often from wind. It causes raised, angular black spots with "
             "yellow halos, and cankers on twigs.",
             "Spread reduced: infected twigs pruned, tools cleaned and new flush protected. Existing spots remain.",
             modules=["planning"], tools=["water_wand"])
    leaf(c, "Raised black spot"); new_leaf(c, "Leaf from the next flush")
    c.method("windbreak", "Windbreaks", "Fewer wind wounds for the bacteria to enter", "Takes years to establish.")
    c.method("sanitation", "Prune infected twigs after harvest; clean tools", "Removes the bacterial source", "Existing spots stay.")
    c.method("copper", "Copper-based protection of new growth where registered", "May slow spread on new tissue", "Limited efficacy; not a cure.")
    inspect_pair(c, ["Raised, angular black spots", "Yellow halo around each spot"], ["The spots show on both surfaces"])
    c.confirm("Raised angular black spots with halos (bacterial black spot)",
              [("Flat, irregular dark spots that merge", "Flat merging spots point to anthracnose.")])
    cut(c, title="Prune the infected tissue")
    protect(c, "Evaluate protective treatment", ["Copper may slow spread on new tissue", "It does not cure existing spots"],
            [("Copper-based protection of new flush where registered, as a supplement", True, "protect", "Accepted as a supplement only."),
             ("Rely on windbreaks, pruning and monitoring", True, "monitor", "A valid choice: fewer wounds and less inoculum come first."),
             ("Copper will cure the spotted tissue", False, None, "Copper does not cure established spots.")])
    c.choose("future", "Treat", "Plan the orchard", ["Reduce wind injury"],
             [("Establish windbreaks around exposed blocks", True, None, "Correct: wind wounds let the bacteria in."),
              ("Leave the block exposed", False, None, "Exposed trees keep getting wounded by wind.")])
    recheck(c, "The next flush shows few new spots.", "Prune new cankers after harvest.")
    c.source("Queensland Department of Agriculture and Fisheries", "https://www.daf.qld.gov.au/business-priorities/agriculture/plants/fruit-vegetable/diseases-disorders/black-spot",
             REVIEWED, "Queensland, Australia", "Regional guidance; copper registrations differ by country.")
    c.source("PestNet factsheet", "https://www.pestnet.org/fact_sheets/mango_bacterial_black_spot_213.htm", REVIEWED, "Pacific region", "Factsheet summary.")
    MANUAL[("mango", "bacterial_canker")] = (0.3, ["Raised, angular black spots", "Yellow halos", "Cankers on twigs"],
        [ctl("sanitation", "sanitation", 6, 8, 2, name="Pruning and tool hygiene"),
         ctl("prevention", "windbreak", 6, 10, 1, 0.8, name="Windbreak protection", desc="Windbreaks that reduce wind wounds on leaves and fruit."),
         ctl("protect", "copper", 3, 5, 0, 0.5, name="Copper protection coverage (supplement)")],
        [])

    c = case("mango", "die_back", "Die back", "Fungal", "Managed",
             "A browning mango leaf from a twig that is dying back from the tip on a water-stressed tree.",
             "Twig dieback is linked to Botryosphaeria-family fungi such as Lasiodiplodia that invade stressed or damaged twigs. "
             "Leaves brown from the tip and margin as the twig dies.",
             "Spread stopped: affected twigs pruned into healthy wood and tree stress reduced. The dead leaf does not recover.",
             modules=["irrigation", "planning"], tools=["water_wand"])
    leaf(c, "Dieback tissue"); new_leaf(c, "Leaf from the regrowth after pruning")
    c.method("pruning", "Prune affected twigs into healthy wood and dispose of them", "Removes the infection", "Dead tissue does not recover.")
    c.method("hygiene", "Clean pruning tools between trees", "Avoids spreading the fungi", "Must be routine.")
    c.method("stress", "Balanced irrigation, nutrition and canopy care", "Less stress, fewer infections", "Gradual.")
    inspect_pair(c, ["Browning from the tip and margin", "The twig behind the leaf is darkening"], ["The underside browns too"])
    c.confirm("Browning from the tip on a dying twig (die back)",
              [("Raised, angular black spots", "Raised angular spots point to bacterial black spot.")])
    cut(c, title="Prune into healthy wood")
    drip(c, "Water stress predisposes trees to dieback")
    c.choose("future", "Treat", "Reduce tree stress", ["Choose the follow-up care"],
             [("Balanced watering and nutrition, then monitor for new dieback", True, None, "Correct."),
              ("Leave the tree water-stressed", False, None, "Stressed trees are invaded again.")])
    recheck(c, "The regrowth is healthy in this simulation.", old_finding="The pruned-off leaf was dead tissue; it does not recover.",
            escalation="If dieback continues, check the trunk and roots with an adviser.")
    c.source("Australian Mango Industry Association", "https://www.industry.mangoes.net.au/resources/resources-library/mttd/", REVIEWED,
             "Northern Territory, Australia", "Mango twig tip dieback research; causes can be complex.")
    MANUAL[("mango", "die_back")] = (0.4, ["Browning from the leaf tip and margin", "Twig darkening behind the leaf", "Worse on stressed trees"],
        [ctl("sanitation", "pruning", 7, 9, 2, 1.3, name="Pruning into healthy wood",
             desc="Cutting affected twigs back into healthy wood and disposing of them."),
         ctl("sanitation", "hygiene", 6, 8, 1, 0.8, cid="hygiene", name="Pruning-tool hygiene", desc="Cleaning pruning tools between trees."),
         ctl("stress", "stress", 6, 8, 2)],
        [rule("dirty_cuts", "hygiene", 0, 3, "Pruning with uncleaned tools can carry the fungi to the next tree.", 0.2, "sanitation", 7, 10)])

    c = case("mango", "gall_midge", "Leaf gall midge", "Pest", "PestSuppressed",
             "A mango leaf covered in small raised galls from a new flush.",
             "Mango leaf gall midges (Procontarinia spp.) lay eggs in young leaves; larvae develop inside wart-like galls and leave exit holes. "
             "Heavy galling distorts leaves and causes leaf drop.",
             "Pest suppressed: galled leaves removed and new flushes monitored. Existing galls stay on retained leaves.",
             modules=["risk"], tools=["sprayer"])
    leaf(c, "Leaf gall"); new_leaf(c, "Leaf from the next flush")
    c.method("remove", "Remove and destroy heavily galled leaves", "Removes larvae before they emerge", "Galls on retained leaves stay.")
    c.method("monitor", "Monitor each new flush", "Finds new galling early", "Requires follow-up.")
    c.method("natural", "Conserve natural enemies", "Keeps midge numbers down", "Broad-spectrum sprays harm them.")
    inspect_pair(c, ["Small raised, wart-like galls", "Galls cluster on young leaves"], ["Galls bulge underneath; look for exit holes"])
    c.confirm("Raised wart-like galls (leaf gall midge)",
              [("Raised, angular black spots with halos", "Black angular spots point to bacterial black spot, not galls.")])
    cut(c, title="Remove the galled leaf tissue")
    protect(c, "Choose follow-up", ["Larvae develop inside the galls"],
            [("Monitor new flushes and conserve natural enemies", True, "monitor", "Correct: catching galling early on new flush matters most."),
             ("Spray the old galls to clear them", False, None, "Larvae inside old galls are shielded; spraying does not remove the damage.")])
    recheck(c, "The next flush has few new galls.", "In Australia, report suspected exotic gall midge to the Exotic Plant Pest Hotline.",
            old_finding="Galls remain on the retained leaves.")
    c.source("Australian Mango Industry Association", "https://industry.mangoes.net.au/resources/resources-library/mango-leaf-gall-midge",
             REVIEWED, "Australia", "Exotic-pest status in Australia; elsewhere the midge is established.")
    c.source("IPPC pest report (Australia)", "https://ippc.int/en/countries/australia/pestreports/2008/11/mango-leaf-gall-midge-mlgm/",
             REVIEWED, "Australia", "Official pest report.")
    MANUAL[("mango", "gall_midge")] = (0.3, ["Small raised, wart-like galls", "Exit holes underneath", "Leaf drop when galling is heavy"],
        [ctl("sanitation", "remove", 6, 8, 2, name="Removal of galled leaves"),
         ctl("monitoring", "monitor", 6, 8, 1),
         ctl("biocontrol", "natural", 5, 7, 1, 0.8, name="Conserving natural enemies")],
        [])

    # ==================================================================================  ORANGE (orchard room)

    c = case("orange", "huanglongbing", "Huanglongbing (citrus greening)", "Bacterial (insect-vectored)", "Replaced",
             "A sweet orange leaf with blotchy mottle from a tree where Asian citrus psyllids were found on new flush.",
             "Huanglongbing is caused by a phloem-limited bacterium (Candidatus Liberibacter spp.) spread by the Asian citrus psyllid. "
             "Infected trees cannot be cured and decline.",
             "A clean, certified replacement tree is protected from psyllids. The infected tree was removed, not cured.",
             modules=["confirmation", "containment", "replacement", "mesh"], tools=["shears", "sprayer"])
    c.method("contain", "Remove confirmed infected trees", "Removes the bacterial source for psyllids", "The infected tree is not cured.")
    c.method("vectors", "Psyllid management and individual protective covers for young trees", "Prevents reinfection", "Requires area-wide, continued effort.")
    c.method("clean_stock", "Certified, disease-free nursery trees", "Healthy replacement", "Replacements can still be infected if psyllids are not managed.")
    vector_replacement(c, "Blotchy, asymmetric yellow mottle",
                       None, "Huanglongbing confirmed by the simulated test.",
                       [("Remove the tree, manage psyllids, plant a certified tree", True, None, "Correct: there is no cure for an infected tree."),
                        ("Feed and spray the tree until it recovers", False, None, "Nothing cures an infected tree; it stays a source for psyllids.")],
                       "Fit a protective cover over the young replacement tree", "Hang the psyllid-monitoring card",
                       "Plant the certified, disease-free nursery tree",
                       "The protected replacement is healthy; few psyllids on the card.",
                       "Keep the cover on young trees and follow area-wide psyllid management.")
    c.source("UF/IFAS Extension (EDIS CG086)", "https://edis.ifas.ufl.edu/publication/CG086", REVIEWED, "Florida, USA",
             "Production guide; insecticide plans follow local specialists.")
    c.source("UF/IFAS Citrus Research and Education Center", "https://crec.ifas.ufl.edu/media/crecifasufledu/extension/plant-pathology-/greening/pdf/Huanglongbing.pdf",
             REVIEWED, "Florida, USA", "Disease overview.")
    replaced_manual(("orange", "huanglongbing"), 0.6, ["Blotchy, asymmetric yellow mottle", "Yellow shoots on the tree", "Psyllids on new flush"],
                    "contain", "Removal of the infected tree", "Removing the confirmed infected tree so psyllids cannot pick up the bacterium.",
                    [ctl("vector", "vectors", 7, 10, 1, 1.2, name="Psyllid management and protective covers"),
                     ctl("replacement", "clean_stock", 7, 10, 1, name="Certified nursery tree")],
                    ["Huanglongbing has no cure: an infected tree left standing keeps feeding the bacterium to psyllids.",
                     rule("open_vectors", "vector", 0, 3, "Unmanaged psyllids carry the bacterium to the replacement tree.", 0.35)])

    # ==================================================================================  RICE (field room)

    PHILRICE = ("Philippine Rice Research Institute (PhilRice)",
                "https://www.philrice.gov.ph/wp-content/uploads/2023/02/Major-diseases-of-the-rice-plant-in-the-Philippines.pdf")
    IRRI = ("International Rice Research Institute (IRRI)", "https://rice-diseases.irri.org/contents")

    def rice_sources(c, note):
        c.source(*PHILRICE, REVIEWED, "Philippines (tropical Asia)", note)
        c.source(*IRRI, REVIEWED, "Asia", "Disease reference index.")

    c = case("rice", "bacterial_leaf_blight", "Bacterial leaf blight", "Bacterial", "Managed",
             "A rice leaf with yellow streaks from a heavily fertilised paddy flooded after a storm.",
             "Xanthomonas oryzae pv. oryzae causes wavy yellow to straw lesions from the leaf tip and edges; it spreads in water and wind-driven rain.",
             "Spread reduced: nitrogen balanced, water managed and a resistant variety planned. Existing streaks remain.",
             modules=["risk", "planning"], tools=["shears", "sprayer"])
    field_leaf(c, "Yellow-straw streak")
    c.method("resistance", "Resistant varieties", "Main defence", "For the next crop.")
    c.method("nutrition", "Balanced nitrogen, no excess", "Less lush, susceptible growth", "Does not remove existing infection.")
    c.method("sanitation", "Remove stubble, straw and weed hosts", "Reduces the carry-over source", "Not an in-season cure.")
    inspect_pair(c, ["Wavy yellow to straw lesions from the tip and margins"], ["Look for bacterial ooze droplets on fresh lesions"])
    c.confirm("Wavy yellow-straw streaks from the margin (bacterial leaf blight)",
              [("Diamond-shaped lesions with grey centres", "Diamond lesions point to blast.")])
    c.inspect("risk", "risk_card", "Review the field notes", ["Heavy nitrogen", "Recent storm and flooding"], tool=None)
    monitor_only(c, "Choose management", ["Balance nitrogen and manage water first"], "Spray a fungicide to cure the streaks")
    plan(c, "A resistant variety, balanced nitrogen and clean stubble", "The same susceptible variety with heavy nitrogen",
         "Lush, susceptible growth keeps bacterial blight severe.")
    recheck(c, "New leaves stay largely clean in this simulation.", "Report rapid spread to an adviser.")
    rice_sources(c, "National guidance; resistant varieties differ by country.")
    c.source("PhilRice fact sheet", "https://dbmp.philrice.gov.ph/CPD/Stemborer/PDF%20files/Bacterial%20Leaf%20Blight%20fact%20sheet.pdf",
             REVIEWED, "Philippines", "Fact sheet.")
    MANUAL[("rice", "bacterial_leaf_blight")] = (0.35, ["Wavy yellow to straw streaks", "Start at the leaf tip and edges", "Spreads in flood water and storms"],
        [ctl("stress", "nutrition", 6, 8, 2, name="Balanced nitrogen", desc="Applying nitrogen in balance, avoiding excess."),
         ctl("sanitation", "sanitation", 6, 8, 1, name="Stubble and weed-host removal"),
         ctl("prevention", "resistance", 6, 10, 1, 0.8, name="Resistant variety choice")],
        [])

    c = case("rice", "brown_spot", "Brown spot", "Fungal", "Managed",
             "A rice leaf with many oval brown spots from a nutrient-poor field.",
             "Bipolaris oryzae causes oval brown spots with grey centres. It is seed-borne and most severe on nutrient-deficient, stressed crops.",
             "Disease managed: soil nutrition corrected, clean seed planned and healthy tissue protected. Existing spots remain.",
             modules=["risk", "planning"], tools=["shears", "water_wand"])
    field_leaf(c, "Oval brown spot")
    c.method("nutrition", "Correct soil nutrition (potassium, silicon, nitrogen)", "Main defence on poor soils", "Gradual.")
    c.method("seed", "Clean or treated seed", "Stops the seed-borne start of the disease", "Prevention for the next crop.")
    c.method("protect", "Fungicide only when severity justifies it", "Protects remaining leaf tissue", "Does not cure spots; follow the local label.")
    inspect_pair(c, ["Oval brown spots with grey centres", "Many spots across the blade"], ["The spots show on both surfaces"])
    c.confirm("Many oval brown spots (brown spot)",
              [("Diamond-shaped lesions with grey centres", "Diamond lesions point to blast.")])
    c.inspect("risk", "risk_card", "Review the soil test", ["Low potassium", "Stressed crop"], tool=None)
    protect(c, "Choose management", ["Is protection justified?"],
            [("Correct soil nutrition; protect the healthy tissue only if spotting is severe", True, "protect", PROTECT_FEEDBACK),
             ("Correct soil nutrition and monitor", True, "monitor", "A valid choice: nutrition is the main measure."),
             ("Cut the spots out of the blade", False, None, FIELD_STRIP)])
    plan(c, "Clean seed and corrected soil nutrition", "Saved seed from this field", "Saved seed carries the fungus into the next crop.")
    recheck(c, "New leaves carry far fewer spots in this simulation.", "If spotting persists, test the soil again.")
    rice_sources(c, "National guidance; nutrient recommendations follow local soil tests.")
    MANUAL[("rice", "brown_spot")] = (0.35, ["Oval brown spots with grey centres", "Many spots across the blade", "Worst on nutrient-poor soils"],
        [ctl("stress", "nutrition", 6, 8, 2, 1.3, name="Soil nutrition correction", desc="Correcting potassium, silicon and nitrogen supply."),
         ctl("protect", "protect", 3, 5, 0, 0.5, name="Protective coverage (only when severe)"),
         ctl("prevention", "seed", 6, 10, 1, 0.6, required=0, name="Clean seed planning")],
        [rule("starved_crop", "stress", 0, 3, "Spraying a nutrient-starved crop does not stop brown spot; fix the nutrition first.", 0.25, "protect", 4, 10)])

    c = case("rice", "leaf_blast", "Leaf blast", "Fungal", "Managed",
             "A rice leaf with blast lesions from a susceptible variety given heavy nitrogen in humid weather.",
             "Pyricularia (Magnaporthe) oryzae causes diamond-shaped lesions with grey centres and brown margins. Excess nitrogen, "
             "long dew periods and drought stress favour it.",
             "Disease managed: nitrogen balanced and healthy tissue protected through the risk period. Existing lesions remain.",
             modules=["risk", "planning"], tools=["shears", "water_wand"])
    field_leaf(c, "Diamond-shaped lesion")
    c.method("resistance", "Resistant varieties", "Main defence", "Resistance can break down as the fungus changes.")
    c.method("nutrition", "Split, balanced nitrogen", "Less susceptible growth", "Does not remove existing lesions.")
    c.method("protect", "Fungicide at early lesions when risk is high", "Protects leaves and later the panicle", "Does not cure lesions; follow the local label.")
    inspect_pair(c, ["Diamond-shaped lesions", "Grey centres with brown margins"], ["Lesions show on both surfaces"])
    c.confirm("Diamond lesions with grey centres (leaf blast)",
              [("Wavy yellow streaks from the margin", "Marginal streaks point to bacterial leaf blight.")])
    c.inspect("risk", "risk_card", "Review the risk", ["Susceptible variety", "Heavy nitrogen", "Humid forecast"], tool=None)
    protect(c, "Choose management", ["High blast risk this week"],
            [("Protect the healthy tissue and balance nitrogen", True, "protect", PROTECT_FEEDBACK),
             ("Cut the lesions out of the blade", False, None, FIELD_STRIP)])
    plan(c, "A resistant variety with split nitrogen", "The same variety with a single heavy nitrogen dose", "Heavy nitrogen keeps blast severe.")
    recheck(c, "New leaves stay protected in this simulation.", "Watch the panicles at heading: neck blast causes the biggest losses.")
    rice_sources(c, "National guidance; fungicide choice follows local labels.")
    MANUAL[("rice", "leaf_blast")] = (0.35, ["Diamond-shaped lesions", "Grey centres, brown margins", "Worse with heavy nitrogen"],
        [ctl("protect", "protect", 6, 8, 1, 1.2), ctl("stress", "nutrition", 6, 8, 2, name="Split, balanced nitrogen"),
         ctl("prevention", "resistance", 6, 10, 1, 0.6, required=0, name="Resistant variety choice")],
        [rule("heavy_n", "stress", 0, 3, "Heavy nitrogen keeps producing blast-susceptible growth that protection cannot keep up with.", 0.2, "protect", 6, 10)])

    c = case("rice", "sheath_blight", "Sheath blight", "Fungal", "Managed",
             "A rice leaf with banded lesions from a dense, heavily fertilised canopy at maximum tillering.",
             "Rhizoctonia solani survives in soil as sclerotia that float in paddy water. Oval to irregular greyish lesions with brown margins "
             "climb from the sheath to the leaves in dense, humid canopies.",
             "Disease managed: canopy density and nitrogen corrected and healthy tissue protected at threshold. Existing lesions remain.",
             modules=["risk", "planning"], tools=["shears", "water_wand"])
    field_leaf(c, "Banded lesion")
    c.method("spacing", "Wider spacing and balanced nitrogen", "A drier, less dense canopy", "Must be planned at planting.")
    c.method("sanitation", "Remove weeds and floating debris; deep ploughing", "Fewer sclerotia", "Not an in-season cure.")
    c.method("protect", "Fungicide when the threshold is reached", "Stops upward spread", "Does not cure lesions; follow the local label.")
    inspect_pair(c, ["Greyish, irregular lesions with brown margins", "Lesions climb from the sheath"], ["Lesions show on both surfaces"])
    c.confirm("Banded greyish lesions climbing from the sheath (sheath blight)",
              [("Diamond lesions on an open canopy", "Diamond lesions point to blast.")])
    c.inspect("risk", "risk_card", "Review the canopy", ["Dense canopy", "Heavy nitrogen", "Maximum tillering"], tool=None)
    protect(c, "Choose management", ["Lesions have reached the threshold"],
            [("Protect the healthy tissue and open the canopy next time", True, "protect", PROTECT_FEEDBACK),
             ("Cut the lesions out of the blade", False, None, FIELD_STRIP)])
    plan(c, "Wider spacing, balanced nitrogen, clean field", "Denser planting with more nitrogen", "Dense, lush canopies favour sheath blight.")
    recheck(c, "Upward spread stopped in this simulation.", "Check the flag leaf before heading.")
    rice_sources(c, "National guidance; no reliably resistant variety is available.")
    c.specimen_note = "Supplied model was exported as rice leaf scald."
    MANUAL[("rice", "sheath_blight")] = (0.4, ["Greyish, irregular lesions with brown margins", "Lesions climb from the sheath", "Dense, humid canopy"],
        [ctl("protect", "protect", 6, 8, 1, 1.2, name="Protection at threshold"),
         ctl("airflow", "spacing", 6, 8, 1, name="Canopy spacing and balanced nitrogen", desc="Wider spacing and balanced nitrogen for a drier canopy."),
         ctl("sanitation", "sanitation", 5, 7, 1, 0.6, required=0, name="Weed and debris removal")],
        [rule("dense", "airflow", 0, 2, "In a very dense canopy the lesions keep climbing despite protection.", 0.2, "protect", 6, 10)])

    c = case("rice", "tungro", "Tungro", "Viral", "Replaced",
             "A yellow-orange rice leaf from a stunted hill in a field with green leafhoppers.",
             "Rice tungro is caused by two viruses spread by green leafhoppers. Infected hills are stunted and cannot be cured.",
             "A resistant variety is transplanted in time with neighbours. The infected hill was removed, not cured.",
             modules=["confirmation", "containment", "replacement", "mesh"], tools=["shears", "sprayer"])
    c.method("contain", "Remove infected hills", "Removes the virus source", "Infected plants are not cured.")
    c.method("vectors", "Leafhopper monitoring and synchronous planting", "Breaks the vector cycle", "Needs area-wide coordination.")
    c.method("clean_stock", "Tungro-resistant varieties", "Healthy replacement", "Resistance differs by region.")
    vector_replacement(c, "Yellow to orange discoloured leaf",
                       None, "Rice tungro confirmed (simulated).",
                       [("Remove the hill, monitor leafhoppers, replant a resistant variety", True, None, "Correct: there is no cure."),
                        ("Add fertiliser until the leaf turns green", False, None, "Yellowing from tungro is not a nutrient problem; the hill stays infected.")],
                       None, "Hang the leafhopper-monitoring card by the seedbed", "Transplant the resistant variety in time with neighbours",
                       "Healthy replacement; few leafhoppers on the card.", "Plant at the same time as neighbouring fields next season.")
    rice_sources(c, "National guidance; tungro occurs in South and Southeast Asia.")
    replaced_manual(("rice", "tungro"), 0.55, ["Yellow to orange leaves", "Stunted hills", "Green leafhoppers in the field"],
                    "contain", "Removal of infected hills", "Removing infected hills so leafhoppers cannot pick up the viruses.",
                    [ctl("vector", "vectors", 6, 9, 1, 1.2, name="Leafhopper monitoring and synchronous planting"),
                     ctl("replacement", "clean_stock", 7, 10, 1, name="Resistant variety")],
                    ["Tungro has no cure: infected hills left in the field keep feeding the viruses to leafhoppers.",
                     rule("open_vectors", "vector", 0, 3, "Unmanaged leafhoppers carry tungro to the new planting.", 0.3)])

    # ==================================================================================  SOYBEAN (field room)

    OKSTATE = ("Oklahoma State University Extension", "https://extension.okstate.edu/fact-sheets/foliar-diseases-of-soybeans.html")
    CLEMSON = ("Clemson Cooperative Extension", "https://lgpress.clemson.edu/publication/identifying-and-managing-foliar-fungal-diseases-of-soybean/")

    c = case("soybean", "bacterial_blight", "Bacterial blight", "Bacterial", "Managed",
             "A soybean leaf with angular spots after cool, rainy, windy weather.",
             "Pseudomonas savastanoi pv. glycinea causes small angular spots with yellow halos that dry and tear out. It survives in residue and "
             "seed and spreads in wind-driven rain. It seldom justifies treatment.",
             "Spread reduced for later crops. Fungicides do not control bacteria, and the existing spots remain.",
             modules=["planning"], tools=["shears", "sprayer"])
    field_leaf(c, "Angular spot with halo")
    c.method("rotation", "Rotation and residue management", "Less carry-over", "Not an in-season cure.")
    c.method("seed", "Clean seed and resistant varieties", "Fewer infections in later crops", "Prevention for the next crop.")
    c.method("cultural", "Avoid field work while leaves are wet", "Less spread on equipment", "Does not remove existing infection.")
    inspect_pair(c, ["Small angular spots with yellow halos", "Dry centres tear out"], ["Spots look water-soaked underneath"])
    c.confirm("Small angular spots with halos (bacterial blight)",
              [("Round spots with grey centres and dark margins", "Pale-centred round spots point to frogeye leaf spot.")])
    monitor_only(c, "Choose management", ["Is a spray justified?"], "Spray a fungicide to cure the spots")
    plan(c, "Rotation, clean seed and a resistant variety", "Soybean again into the same residue", "Residue carries the bacteria to the next crop.")
    recheck(c, "Upper leaves stay largely clean as the weather warms.", "Hot, dry weather normally stops spread.")
    c.source(*OKSTATE, REVIEWED, "Oklahoma, USA", "Regional fact sheet.")
    MANUAL[("soybean", "bacterial_blight")] = (0.25, ["Small angular spots with yellow halos", "Dry centres tear out", "Follows cool, rainy, windy weather"],
        [ctl("prevention", "rotation", 6, 10, 1, 1.2, name="Rotation and residue management"),
         ctl("prevention", "seed", 6, 10, 1, cid="seed", name="Clean seed and resistant variety", desc="Clean seed and a resistant variety for later crops."),
         ctl("monitoring", "cultural", 5, 7, 1, 0.6, required=0, name="Dry-field working", desc="Keeping equipment out of the crop while leaves are wet.")],
        [])

    c = case("soybean", "cercospora_leaf_blight", "Cercospora leaf blight", "Fungal", "Managed",
             "A bronzed, leathery soybean leaf from the upper canopy of a susceptible variety in a warm, humid season.",
             "Cercospora spp. (C. kikuchii, C. flagellaris) cause purple-bronze, leathery upper leaves and purple seed stain. They survive in residue and seed.",
             "Remaining healthy tissue protected at the justified stage. Bronzed tissue does not recover.",
             modules=["risk", "planning"], tools=["shears", "water_wand"])
    field_leaf(c, "Purple-bronze tissue")
    c.method("resistance", "Less susceptible varieties", "Main defence", "For the next crop.")
    c.method("rotation", "Rotation and residue management", "Less inoculum", "Not an in-season fix.")
    c.method("protect", "Fungicide at early pod stages when justified", "Protects remaining healthy tissue", "Only when stage, variety and weather justify it.")
    inspect_pair(c, ["Purple-bronze, leathery upper leaves"], ["The underside shows the discoloration"])
    c.confirm("Purple-bronze leathery leaves (Cercospora leaf blight)",
              [("Round spots with grey centres", "Round pale-centred spots point to frogeye leaf spot.")])
    c.inspect("risk", "risk_card", "Review stage and weather", ["Early pod stage", "Susceptible variety", "Warm, humid forecast"], tool=None)
    protect(c, "Choose management", ["Is protection justified?"],
            [("Yes: protect the remaining healthy tissue", True, "protect", "Justified here: stage, susceptibility and weather all favour disease."),
             ("Cut the bronzed tissue out", False, None, FIELD_STRIP)])
    plan(c, "Rotation and a less susceptible variety", "Soybean again on the same residue", "Residue keeps the fungus close.")
    recheck(c, "Healthy tissue stays protected through pod fill.", "Check harvested seed for purple stain.")
    c.source("Crop Protection Network", "https://cropprotectionnetwork.org/encyclopedia/cercospora-leaf-blight-of-soybean", REVIEWED,
             "Southern USA", "Causal species are being reclassified; thresholds vary.")
    MANUAL[("soybean", "cercospora_leaf_blight")] = (0.35, ["Purple-bronze, leathery upper leaves", "Upper canopy first", "Purple seed stain at harvest"],
        [ctl("protect", "protect", 6, 8, 1, 1.2, name="Foliar protection coverage"),
         ctl("prevention", "rotation", 6, 10, 1, 0.8, name="Rotation and residue management"),
         ctl("prevention", "resistance", 6, 10, 1, 0.6, cid="resistance", required=0, name="Variety choice", desc="A less susceptible variety next season.")],
        [])

    c = case("soybean", "downy_mildew", "Downy mildew", "Oomycete (water mould)", "Managed",
             "A soybean leaf with pale yellow spots from a humid, cool spell; greyish tufts show underneath.",
             "Peronospora manshurica is a water mould that survives in leaves and on seed. Pale yellow spots above and grey tufts below are "
             "common, but it seldom causes serious yield loss.",
             "Managed without unnecessary spraying: clean seed and rotation planned. The spots remain.",
             modules=["planning"], tools=["shears", "sprayer"])
    field_leaf(c, "Pale yellow spot")
    c.method("monitor", "Monitor; spraying is seldom justified", "Avoids cost without benefit", "Commonly used soybean fungicides do not control it.")
    c.method("seed", "Certified seed", "Fewer seed-borne infections", "Prevention for the next crop.")
    c.method("rotation", "Rotation and resistant varieties", "Lower risk in later crops", "Not an immediate treatment.")
    inspect_pair(c, ["Pale green to yellow spots on the upper surface"], ["Greyish, fuzzy tufts on the underside"])
    c.confirm("Yellow spots above, grey tufts below (downy mildew)",
              [("Orange-tan pustules underneath", "Pustules point to soybean rust.")])
    monitor_only(c, "Choose management", ["Downy mildew seldom reduces yield"], "Spray a general fungicide to clear it")
    plan(c, "Certified seed and rotation", "Saved seed from this crop", "Saved seed can carry the pathogen.")
    recheck(c, "The crop stays productive; spots remain cosmetic.", "Note the field if spotting becomes severe in a susceptible variety.")
    c.source("Crop Protection Network", "https://cropprotectionnetwork.org/encyclopedia/downy-mildew-of-soybean", REVIEWED,
             "USA", "Usually minor; severity varies by variety.")
    c.source("University of Kentucky Extension", "https://publications.ca.uky.edu/sites/publications.ca.uky.edu/files/ppfs-ag-s-03.pdf",
             REVIEWED, "Kentucky, USA", "Regional fact sheet.")
    MANUAL[("soybean", "downy_mildew")] = (0.2, ["Pale yellow spots on the upper surface", "Grey tufts underneath", "Follows cool, humid spells"],
        [ctl("monitoring", "monitor", 5, 7, 1, 1.2),
         ctl("prevention", "seed", 6, 10, 1, name="Certified seed planning"),
         ctl("prevention", "rotation", 5, 10, 0, 0.4, cid="rotation", required=0, name="Rotation planning", desc="Rotation and resistant varieties.")],
        [])

    c = case("soybean", "frogeye_leaf_spot", "Frogeye leaf spot", "Fungal", "Managed",
             "A soybean leaf with frogeye spots in the upper canopy of a susceptible variety during warm, wet weather.",
             "Cercospora sojina causes round spots with grey centres and dark reddish-brown margins. It survives in residue and seed; "
             "some populations resist one fungicide group.",
             "Remaining healthy tissue protected. Existing spots remain.",
             modules=["risk", "planning"], tools=["shears", "water_wand"])
    field_leaf(c, "Frogeye spot")
    c.method("resistance", "Resistant varieties", "Main defence", "For the next crop.")
    c.method("rotation", "Rotation and residue management", "Less inoculum", "Not an in-season fix.")
    c.method("protect", "Fungicide with more than one mode of action when justified", "Protects healthy tissue", "Single-group products can fail on resistant populations.")
    inspect_pair(c, ["Round spots with grey centres", "Dark reddish-brown margins"], ["The spots show underneath"])
    c.confirm("Round grey-centred spots with dark margins (frogeye leaf spot)",
              [("Bronzed leathery leaves", "Bronzing points to Cercospora leaf blight.")])
    c.inspect("risk", "risk_card", "Review stage and history", ["Early pod stage", "Susceptible variety", "Warm, wet forecast"], tool=None)
    protect(c, "Choose management", ["Is protection justified?"],
            [("Protect with more than one mode of action", True, "protect", "Correct: mixed modes of action slow resistance."),
             ("Reuse a single-group product that failed before", False, None, "Resistant populations are not controlled by that group."),
             ("Cut the spots out", False, None, FIELD_STRIP)])
    plan(c, "Rotation and a resistant variety", "Soybean again on the same residue", "Residue keeps the fungus close.")
    recheck(c, "Healthy tissue stays protected.", "If spots keep spreading after treatment, suspect fungicide resistance.")
    c.source("Crop Protection Network", "https://cropprotectionnetwork.org/encyclopedia/frogeye-leaf-spot-of-soybean", REVIEWED,
             "USA and Ontario, Canada", "Fungicide resistance status varies by region.")
    c.source("University of Nebraska-Lincoln CropWatch", "https://cropwatch.unl.edu/plant-disease/soybean/frogeye-leaf-spot", REVIEWED,
             "Nebraska, USA", "Regional guidance.")
    MANUAL[("soybean", "frogeye_leaf_spot")] = (0.35, ["Round spots with grey centres", "Dark reddish-brown margins", "Upper canopy in warm, wet weather"],
        [ctl("protect", "protect", 6, 8, 1, 1.2, name="Protection coverage (mixed modes of action)"),
         ctl("prevention", "rotation", 6, 10, 1, 0.8, name="Rotation and residue management"),
         ctl("prevention", "resistance", 6, 10, 1, 0.6, cid="resistance", required=0, name="Resistant variety choice", desc="A resistant variety next season.")],
        [])

    c = case("soybean", "mosaic_virus", "Mosaic virus", "Viral", "Replaced",
             "A soybean leaf from a stunted plant in a seed-production field; soybean mosaic virus was confirmed in the crop.",
             "Soybean mosaic virus enters fields on infected seed and aphids spread it within the crop. Leaves show light and dark green "
             "mosaic, puckering and distortion. Infected plants cannot be cured, and insecticides do not stop the virus.",
             "The next crop starts from certified virus-free seed. The infected plant was removed from the seed field, not cured.",
             modules=["confirmation", "containment", "replacement", "planning"], tools=["sprayer"])
    c.method("contain", "Rogue infected plants from seed-production fields", "Keeps the virus out of saved seed", "Infected plants are not cured.")
    c.method("clean_seed", "Certified virus-free seed", "Main defence", "Prevention for the next crop.")
    c.method("timing", "Avoid late planting", "Less exposure to aphid flights", "Does not protect an infected crop.")
    soil_replacement(c, "Mottled, distorted tissue",
                     ["Light and dark green mosaic", "Puckered, distorted leaves"],
                     "Soybean mosaic virus confirmed (simulated).",
                     [("Remove the plant from the seed field; plant virus-free seed next season", True, None, "Correct: there is no cure."),
                      ("Spray insecticide to cure the plant", False, None, "Insecticides do not cure the virus and do not reliably stop its spread.")],
                     ("Clean hands and tools before handling healthy plants",),
                     [("Certified virus-free seed, planted on time", True, None, "Correct."),
                      ("Seed saved from this field", False, None, "Infected seed brings the virus straight back.")],
                     "Plant certified virus-free seed",
                     "The new crop is free of mosaic symptoms.", "If mosaic appears early, check the seed source.")
    c.source("NC State Extension", "https://content.ces.ncsu.edu/publication/soybean-mosaic-virus", REVIEWED, "North Carolina, USA",
             "No commercially available resistant variety is listed there.")
    c.source("University of Minnesota Extension", "https://extension.umn.edu/soybean-pest-management/soybean-mosaic-virus", REVIEWED,
             "Minnesota, USA", "The value of insecticides against the aphid vectors is uncertain.")
    c.specimen_note = "Specimen mismatch: the supplied model shows white powdery patches, not mosaic mottling."
    replaced_manual(("soybean", "mosaic_virus"), 0.45, ["Light and dark green mosaic", "Puckered, distorted leaves", "Stunted plants"],
                    "contain", "Roguing infected plants", "Removing infected plants from seed-production fields.",
                    [ctl("replacement", "clean_seed", 8, 10, 2, 1.2, name="Certified virus-free seed"),
                     ctl("prevention", "timing", 6, 10, 1, 0.6, required=0, name="Planting date (avoid late planting)")],
                    ["Infected plants left in a seed field put the virus into the seed."])

    c = case("soybean", "septoria_brown_spot", "Septoria brown spot", "Fungal", "Managed",
             "A lower soybean leaf with brown flecks and yellowing early in the season.",
             "Septoria glycines causes small angular brown flecks and yellowing on lower leaves. It is the most common soybean leaf disease but "
             "losses are usually low, and late fungicides do not control it well.",
             "Managed without unnecessary spraying: rotation and residue management planned. The spotted tissue remains.",
             modules=["planning"], tools=["shears", "sprayer"])
    field_leaf(c, "Brown fleck")
    c.method("monitor", "Monitor; treatment is seldom justified", "Avoids cost without benefit", "Requires follow-up if spots climb.")
    c.method("rotation", "Rotation and residue breakdown", "Less inoculum", "Not an in-season fix.")
    c.method("seed", "High-quality seed", "Fewer infections early", "Prevention for the next crop.")
    inspect_pair(c, ["Small angular brown flecks", "Yellowing around the flecks"], ["The flecks show underneath"])
    c.confirm("Brown flecks with yellowing on lower leaves (Septoria brown spot)",
              [("Round spots with grey centres", "Round pale-centred spots point to frogeye leaf spot.")])
    monitor_only(c, "Choose management", ["Losses are usually low"], "Spray now to clear the flecks")
    plan(c, "Rotation and residue management", "Soybean again on the same residue", "Residue keeps the fungus close.")
    recheck(c, "Spots stay in the lower canopy; the crop stays productive.", "Reassess if spots reach the upper canopy early.")
    c.source(*OKSTATE, REVIEWED, "Oklahoma, USA", "Regional fact sheet.")
    c.source(*CLEMSON, REVIEWED, "South Carolina, USA", "Regional guidance.")
    c.specimen_note = "Supplied model has no roughness map; the default leaf roughness is used."
    MANUAL[("soybean", "septoria_brown_spot")] = (0.3, ["Small angular brown flecks", "Yellowing on lower leaves", "Starts early in the season"],
        [ctl("monitoring", "monitor", 5, 7, 1, 1.2),
         ctl("prevention", "rotation", 6, 10, 1, name="Rotation and residue management"),
         ctl("prevention", "seed", 5, 10, 0, 0.4, cid="seed", required=0, name="Seed quality", desc="High-quality seed for the next crop.")],
        [])

    c = case("soybean", "soybean_rust", "Soybean rust", "Fungal", "Managed",
             "A soybean leaf with tan rust lesions found during scouting before flowering, after rust was reported nearby.",
             "Phakopsora pachyrhizi spores blow in on the wind. Small tan to reddish-brown lesions with pustules underneath can defoliate "
             "a crop quickly in warm, humid weather.",
             "Remaining healthy tissue protected in time. Existing lesions remain.",
             modules=["risk", "planning"], tools=["shears", "water_wand"])
    field_leaf(c, "Rust lesion")
    c.method("scout", "Scouting and regional rust reports", "Times protection to spore arrival", "Requires regular checks.")
    c.method("protect", "Timely fungicide once rust is confirmed nearby", "Protects remaining healthy tissue", "Late protection after heavy defoliation is of little use.")
    c.method("variety", "Earlier-maturing varieties", "Escape late-season rust", "Not an immediate treatment.")
    inspect_pair(c, ["Small tan to reddish-brown lesions"], ["Volcano-shaped pustules on the underside"])
    c.confirm("Small lesions with pustules underneath (soybean rust)",
              [("Grey tufts underneath, yellow spots above", "Grey tufts point to downy mildew.")])
    c.inspect("risk", "risk_card", "Review the rust report", ["Rust confirmed nearby", "Warm, humid forecast"], tool=None)
    protect(c, "Choose management", ["Rust is arriving early in the season"],
            [("Protect the healthy tissue now", True, "protect", "Correct: early protection matters for rust."),
             ("Wait until most leaves have fallen", False, None, "Protection after heavy defoliation is too late.")])
    plan(c, "Scouting plus an earlier-maturing variety", "No scouting next season", "Without scouting, rust is found too late.")
    recheck(c, "Healthy tissue stays protected.", "Keep scouting while humid weather continues.")
    c.source(*CLEMSON, REVIEWED, "South Carolina, USA", "Southern US guidance; rust arrival varies by year.")
    MANUAL[("soybean", "soybean_rust")] = (0.35, ["Small tan to reddish-brown lesions", "Pustules underneath", "Rapid leaf drop in humid weather"],
        [ctl("protect", "protect", 6, 8, 1, 1.3, name="Timely protection coverage"),
         ctl("monitoring", "scout", 6, 8, 1, name="Scouting frequency"),
         ctl("prevention", "variety", 5, 10, 0, 0.4, required=0, name="Variety maturity planning")],
        [rule("late", "monitoring", 0, 2, "Without scouting, protection arrives after the rust has spread.", 0.25, "protect", 6, 10)])

    c = case("soybean", "sudden_death_syndrome", "Sudden death syndrome", "Fungal (soil-borne)", "Replaced",
             "A soybean leaf with interveinal yellowing and browning from a field that was cool and wet at planting.",
             "Fusarium virguliforme infects roots early in the season; a toxin moving to the leaves causes interveinal yellowing and death. "
             "Leaf sprays do not reach it, and affected plants cannot be saved this season.",
             "The next crop starts with a tolerant variety and seed treatment. Affected plants were not cured.",
             modules=["confirmation", "containment", "replacement", "planning"], tools=["sprayer"])
    c.method("contain", "Bag a sample for diagnosis; leave infested soil in place", "Confirms the cause without spreading soil", "Affected plants are not cured.")
    c.method("tolerant", "Tolerant varieties and seed treatment for the next crop", "Main defence", "Prevention, not cure.")
    c.method("drainage", "Better drainage and less compaction", "Less root infection", "Gradual.")
    soil_replacement(c, "Interveinal yellowing and browning",
                     ["Yellow then brown between the veins", "The veins stay green"],
                     "Sudden death syndrome confirmed: root rot with leaf symptoms (simulated).",
                     [("Diagnose, then plan a tolerant variety with seed treatment", True, None, "Correct: the cause is in the roots."),
                      ("Spray the leaves to stop the dying", False, None, "Leaf sprays do not reach a root infection.")],
                     ("Clean soil off tools and boots",),
                     [("A tolerant variety with seed treatment, better drainage", True, None, "Correct."),
                      ("Rotate to corn and expect the problem to vanish", False, None, "Rotating to corn does not typically reduce SDS.")],
                     "Plant the tolerant variety with seed treatment",
                     "The next crop shows no leaf symptoms in this simulation.", "Check for soybean cyst nematode, which worsens SDS.")
    c.source("Crop Protection Network", "https://cropprotectionnetwork.org/publications/an-overview-of-sudden-death-syndrome", REVIEWED,
             "North Central USA and Ontario, Canada", "Regional overview.")
    replaced_manual(("soybean", "sudden_death_syndrome"), 0.5, ["Interveinal yellowing then browning", "Veins stay green", "Leaves drop, petioles stay"],
                    "contain", "Containment (sample, no soil movement)", "Bagging a sample for diagnosis and keeping infested soil in place.",
                    [ctl("replacement", "tolerant", 7, 10, 1, 1.2, name="Tolerant variety with seed treatment"),
                     ctl("prevention", "drainage", 6, 10, 1, 0.6, required=0, name="Drainage and compaction relief")],
                    ["Moving infested soil and plants spreads the fungus to clean fields."])

    c = case("soybean", "target_spot", "Target spot", "Fungal", "Managed",
             "A lower-canopy soybean leaf with ringed spots from a susceptible variety in a hot, humid season.",
             "Corynespora cassiicola causes brown spots with concentric rings and yellow halos, starting in the lower canopy, and can "
             "defoliate susceptible varieties in the southern USA.",
             "Remaining healthy tissue protected where justified. Existing spots remain.",
             modules=["risk", "planning"], tools=["shears", "water_wand"])
    field_leaf(c, "Ringed target spot")
    c.method("resistance", "Less susceptible varieties", "Main defence", "For the next crop.")
    c.method("rotation", "Rotation and residue management", "Less inoculum", "Not an in-season fix.")
    c.method("protect", "Fungicide when stage, variety and weather justify it", "Protects remaining healthy tissue", "Efficacy varies; follow the local label.")
    inspect_pair(c, ["Brown spots with concentric rings", "Yellow halo around each spot"], ["The spots show underneath"])
    c.confirm("Ringed brown spots with halos (target spot)",
              [("Small grey-centred round spots", "Small grey-centred spots point to frogeye leaf spot.")])
    c.inspect("risk", "risk_card", "Review stage and weather", ["Susceptible variety", "Hot, humid forecast"], tool=None)
    protect(c, "Choose management", ["Is protection justified?"],
            [("Yes: protect the remaining healthy tissue", True, "protect", PROTECT_FEEDBACK),
             ("Cut the spots out", False, None, FIELD_STRIP)])
    plan(c, "Rotation and a less susceptible variety", "Soybean again on the same residue", "Residue keeps the fungus close.")
    recheck(c, "Healthy tissue stays protected.", "Reassess if defoliation climbs the canopy.")
    c.source("Crop Protection Network", "https://cropprotectionnetwork.org/encyclopedia/target-spot-of-soybean", REVIEWED,
             "Southern USA", "Importance varies by region.")
    c.source("NC State Extension plant diagnostics", "https://diagnosis.ces.ncsu.edu/soybean/disorder/detail/target-spot", REVIEWED,
             "North Carolina, USA", "Diagnostic reference.")
    MANUAL[("soybean", "target_spot")] = (0.35, ["Brown spots with concentric rings", "Yellow halos", "Lower canopy first"],
        [ctl("protect", "protect", 6, 8, 1, 1.2, name="Foliar protection coverage"),
         ctl("prevention", "rotation", 6, 10, 1, 0.8, name="Rotation and residue management"),
         ctl("prevention", "resistance", 6, 10, 1, 0.6, cid="resistance", required=0, name="Variety choice", desc="A less susceptible variety next season.")],
        [])

    # ==================================================================================  SQUASH (greenhouse room)

    c = case("squash", "powdery_mildew", "Powdery mildew", "Fungal", "Managed",
             "An older squash leaf with powdery mildew in mid-summer from a crowded, well-fertilised planting.",
             "Podosphaera xanthii forms white powdery patches, first on older leaves. It thrives in shade and crowding and develops "
             "resistance to fungicides quickly.",
             "Mildew suppressed and new leaves protected. Distorted, yellowed tissue is not repaired.",
             modules=["ventilation"], tools=["water_wand"])
    leaf(c, "Mildew patch"); new_leaf(c, "A younger leaf")
    c.method("resistance", "Resistant or tolerant varieties", "Main defence", "For later plantings.")
    c.method("airflow", "Spacing, airflow and no excess nitrogen", "Less favourable conditions", "Does not remove mildew alone.")
    c.method("protect", "Protection from the first detection, rotating modes of action", "Suppresses mildew on new leaves", "Resistance develops quickly; follow the local label.")
    c.method("sanitation", "Remove heavily affected old leaves", "Less spore load", "Remaining patches stay.")
    inspect_pair(c, ["White powdery patches on the upper surface"], ["Check the underside for more patches"])
    c.confirm("White powdery patches (powdery mildew)",
              [("Angular yellow spots with grey fuzz underneath", "Grey fuzz underneath indicates downy mildew.")])
    humidity(c, "Shade and crowding favour powdery mildew")
    cut(c, count=1, title="Remove the worst patch")
    protect(c, "Choose protection", ["Mildew found on one in fifty leaves"],
            [("Protect from first detection, rotating modes of action", True, "protect", "Correct: starting early and rotating slows resistance."),
             ("Repeat one product all season", False, None, "The fungus quickly becomes resistant to a single mode of action.")])
    recheck(c, "Younger leaves stay largely clean.", "Keep scouting weekly while fruit develops.", old_finding="Distorted, yellowed tissue remains.")
    c.source("University of Minnesota Extension", "https://extension.umn.edu/disease-management/powdery-mildew-cucurbits", REVIEWED,
             "Upper Midwest, USA", "Product choice follows the regional vegetable production guide.")
    MANUAL[("squash", "powdery_mildew")] = (0.35, ["White powdery patches", "Older leaves first", "Leaves yellow and die early"],
        [ctl("airflow", "airflow", 6, 8, 1, name="Spacing and airflow"), ctl("protect", "protect", 6, 8, 1, 1.2),
         ctl("sanitation", "sanitation", 4, 6, 1, 0.6, name="Removal of the worst leaves"),
         ctl("prevention", "resistance", 6, 10, 0, 0.4, required=0, name="Resistant variety planning")],
        [rule("shaded", "airflow", 0, 2, "In a crowded, shaded planting, protection alone will not hold mildew back.", 0.2, "protect", 6, 10)])

    # ==================================================================================  SUGARCANE (field room)

    LSU = ("LSU AgCenter", "https://www.lsuagcenter.com/portals/communications/publications/management_guides/plant_disease_guide/field-crops-sugarcane")
    TAMU = ("Texas A&M AgriLife Extension (Texas Plant Disease Handbook)", "https://plantdiseasehandbook.tamu.edu/industry-specialty/sugar-crops/sugarcane/")

    c = case("sugarcane", "mosaic", "Mosaic", "Viral", "Replaced",
             "A mottled sugarcane leaf from a stool in a nursery block planted from untested seed cane.",
             "Sugarcane mosaic is caused by potyviruses spread by aphids and, above all, by infected seed cane. Infected stools cannot be cured.",
             "Healthy, tested seed cane of a resistant variety is planted. The infected stool was rogued, not cured.",
             modules=["confirmation", "containment", "replacement"], tools=["sprayer"])
    c.method("contain", "Rogue infected stools from seed-cane nurseries", "Keeps the virus out of seed cane", "Infected stools are not cured.")
    c.method("clean_stock", "Healthy, tested seed cane", "Healthy replacement", "Aphids can still bring the virus in.")
    c.method("resistance", "Resistant varieties", "Main defence", "Resistance can break down to new strains.")
    soil_replacement(c, "Light and dark green mottled stripes",
                     ["Pale green to yellow streaks and stripes", "Mottling is clearest on young leaves"],
                     "Sugarcane mosaic confirmed (simulated).",
                     [("Rogue the stool and plant healthy, tested seed cane", True, None, "Correct: there is no cure."),
                      ("Spray the stool until the stripes fade", False, None, "No spray cures a virus infection.")],
                     ("Clean cane knives before cutting seed cane",),
                     [("Healthy, tested seed cane of a resistant variety", True, None, "Correct."),
                      ("Seed cane cut from this block", False, None, "Infected seed cane carries the virus to the new field.")],
                     "Plant the healthy, tested seed cane",
                     "The new planting shows no mosaic.", "Inspect nurseries regularly and rogue any new mosaic.")
    c.source(*LSU, REVIEWED, "Louisiana, USA", "Regional guidance; strains differ by region.")
    c.source(*TAMU, REVIEWED, "Texas, USA", "Regional handbook.")
    replaced_manual(("sugarcane", "mosaic"), 0.45, ["Light and dark green mottled stripes", "Clearest on young leaves", "Stunted stools"],
                    "contain", "Roguing infected stools", "Removing infected stools from seed-cane nurseries.",
                    [ctl("replacement", "clean_stock", 8, 10, 2, 1.2, name="Healthy, tested seed cane"),
                     ctl("prevention", "resistance", 6, 10, 1, 0.6, required=0, name="Resistant variety choice")],
                    ["Infected stools left in a nursery pass the virus into the seed cane."])

    c = case("sugarcane", "red_rot", "Red rot", "Fungal", "Replaced",
             "A sugarcane leaf with red midrib lesions from a clump whose split stalk shows red tissue with white patches.",
             "Colletotrichum falcatum infects stalks and midribs. Infected stalks rot inside and the clump cannot be saved; "
             "the fungus travels in setts and survives in debris.",
             "Healthy setts of a resistant variety are planted after rotation. The infected clump was removed, not cured.",
             modules=["confirmation", "containment", "replacement", "planning"], tools=["sprayer"])
    c.method("contain", "Rogue and destroy infected clumps and debris", "Removes the inoculum", "Infected clumps are not cured.")
    c.method("clean_setts", "Healthy setts from disease-free nurseries", "Stops the sett-borne start", "Prevention for the next crop.")
    c.method("resistance", "Resistant varieties and rotation", "Main defence", "Resistance can break down to new races.")
    soil_replacement(c, "Red lesion along the midrib",
                     ["Elongated red lesions on the midrib", "The split stalk shows red tissue with white patches"],
                     "Red rot confirmed (simulated).",
                     [("Remove the clump and replant healthy setts of a resistant variety", True, None, "Correct: the rot is inside the stalk."),
                      ("Spray the leaves to stop the rot", False, None, "Leaf sprays do not reach a stalk infection.")],
                     ("Clean cane knives and tools",),
                     [("Rotation, then healthy setts of a resistant variety", True, None, "Correct."),
                      ("Ratoon the same clumps again", False, None, "Ratooning infected clumps keeps the rot in the field.")],
                     "Plant the healthy setts",
                     "The new planting is healthy.", "Avoid waterlogging and drought stress, which worsen red rot.")
    c.source("Vikaspedia (Government of India)", "https://vikaspedia.in/agriculture/crop-production/integrated-pest-managment/ipm-for-commercial-crops/ipm-strategies-for-sugarcane/sugarcane-diseases-and-symptoms",
             REVIEWED, "India", "National IPM summary.")
    c.source(*LSU, REVIEWED, "Louisiana, USA", "Regional guidance.")
    replaced_manual(("sugarcane", "red_rot"), 0.55, ["Red lesions along the midrib", "Red stalk tissue with white patches", "Drying clumps"],
                    "contain", "Roguing infected clumps", "Removing and destroying infected clumps and debris.",
                    [ctl("replacement", "clean_setts", 8, 10, 2, 1.2, name="Healthy setts"),
                     ctl("prevention", "resistance", 6, 10, 1, 0.8, name="Resistant variety and rotation")],
                    ["Infected clumps and debris left in the field keep the red rot going."])

    c = case("sugarcane", "rust", "Rust", "Fungal", "Managed",
             "A sugarcane leaf with orange-brown rust streaks from a moderately resistant variety.",
             "Rust fungi (Puccinia melanocephala / P. kuehnii) form elongated orange to brown pustules; spores blow in on the wind. "
             "Resistant varieties are the main control.",
             "Leaf function kept by variety choice and monitoring. Existing pustules remain.",
             modules=["risk", "planning"], tools=["shears", "sprayer"])
    field_leaf(c, "Rust pustule streak")
    c.method("resistance", "Resistant varieties", "Main defence", "Resistance can break down to new races.")
    c.method("monitor", "Monitoring", "Finds a breakdown early", "Requires follow-up.")
    inspect_pair(c, ["Elongated orange to brown streaks", "Pustules along the veins"], ["Pustules show underneath too"])
    c.confirm("Orange-brown pustule streaks (rust)",
              [("Red lesions along the midrib", "Red midrib lesions point to red rot.")])
    c.inspect("risk", "risk_card", "Assess severity", ["Light rust", "Moderately resistant variety"], tool=None)
    monitor_only(c, "Choose management", ["Light rust on a moderately resistant variety"], "Spray now to clear the pustules")
    plan(c, "Keep resistant varieties and replace any that break down", "Plant a susceptible variety next",
         "Susceptible varieties suffer heavy rust.")
    recheck(c, "Leaf tissue stays functional; rust stays light.", "Report a sudden increase: resistance may have broken down.")
    c.source(*TAMU, REVIEWED, "Texas, USA", "Regional handbook.")
    c.source(*LSU, REVIEWED, "Louisiana, USA", "Regional guidance.")
    MANUAL[("sugarcane", "rust")] = (0.2, ["Elongated orange to brown streaks", "Pustules on both surfaces", "Spores arrive on the wind"],
        [ctl("monitoring", "monitor", 6, 8, 1, 1.2),
         ctl("prevention", "resistance", 6, 10, 1, 0.8, cid="resistance", name="Resistant variety choice", desc="Keeping resistant varieties in production.")],
        [])

    c = case("sugarcane", "yellow_leaf", "Yellow leaf", "Viral", "Replaced",
             "A sugarcane leaf with a yellow midrib from a stressed ratoon crop.",
             "Sugarcane yellow leaf virus is spread by aphids and in infected seed cane. The midrib yellows on the underside first; "
             "symptoms show most under stress. Infected cane cannot be cured.",
             "Healthy (tissue-culture) seed cane is planted. The infected stool was removed, not cured.",
             modules=["confirmation", "containment", "replacement", "planning"], tools=["sprayer"])
    c.method("contain", "Remove infected stools from nurseries", "Keeps the virus out of seed cane", "Infected cane is not cured.")
    c.method("clean_stock", "Healthy, tissue-culture-derived seed cane", "Healthy replacement", "Aphids can reinfect it.")
    c.method("stress", "Good growing conditions", "Fewer symptoms", "Does not remove the virus.")
    soil_replacement(c, "Yellow midrib",
                     ["Yellowing midrib, clearest underneath", "Yellowing spreads into the blade"],
                     "Sugarcane yellow leaf virus confirmed (simulated).",
                     [("Remove the stool and plant healthy seed cane", True, None, "Correct: there is no cure."),
                      ("Fertilise until the midrib turns green", False, None, "The yellowing is caused by a virus, not a nutrient shortage.")],
                     ("Clean cane knives before cutting seed cane",),
                     [("Healthy, tissue-culture-derived seed cane", True, None, "Correct."),
                      ("Seed cane cut from this ratoon", False, None, "Infected seed cane carries the virus.")],
                     "Plant the healthy seed cane",
                     "The new planting has green midribs.", "Keep crops unstressed; symptoms mostly appear under stress.")
    c.source("South African Sugarcane Research Institute (SASRI)", "https://sasri.org.za/download/198/diseases/23717/pest-disease-guide-2022.pdf",
             REVIEWED, "South Africa", "Regional pest and disease guide.")
    c.source(*LSU, REVIEWED, "Louisiana, USA", "Regional guidance.")
    replaced_manual(("sugarcane", "yellow_leaf"), 0.45, ["Yellow midrib, clearest underneath", "Yellowing spreads into the blade", "Worse under stress"],
                    "contain", "Removal of infected stools", "Removing infected stools from seed-cane nurseries.",
                    [ctl("replacement", "clean_stock", 8, 10, 2, 1.2, name="Healthy, tissue-culture seed cane"),
                     ctl("stress", "stress", 6, 8, 1, 0.6, required=0, name="Growing conditions (stress relief)")],
                    ["Infected stools left in a nursery pass the virus into the seed cane."])

    # ==================================================================================  WHEAT (field room)

    c = case("wheat", "blast", "Wheat blast", "Fungal", "Managed",
             "A wheat leaf with blast lesions from a crop heading in warm, humid weather in a blast-affected region.",
             "Magnaporthe oryzae pathotype Triticum causes eye-shaped leaf lesions with pale centres and, far more damaging, bleached spikes. "
             "No variety is immune, and infected spikes do not recover.",
             "Remaining healthy tissue protected through the risk period and the next sowing planned to avoid it. Existing lesions remain.",
             modules=["risk", "planning"], tools=["shears", "water_wand"])
    field_leaf(c, "Eye-shaped lesion", "Flag leaf later in the season")
    c.method("resistance", "Varieties with partial resistance", "Main defence", "No variety is immune.")
    c.method("sowing", "Sowing date that avoids warm, humid heading", "Escapes the risk period", "For the next season.")
    c.method("protect", "Preventive fungicide when the forecast is high-risk", "Protects remaining healthy tissue and spikes", "Does not cure lesions or bleached spikes; follow the local label.")
    c.method("seed", "Clean seed and rotation", "Fewer carry-over sources", "Prevention for the next crop.")
    inspect_pair(c, ["Eye-shaped lesions with pale grey centres", "Dark margins"], ["Lesions show on both surfaces"])
    c.confirm("Eye-shaped pale-centred lesions (wheat blast)",
              [("Orange-brown pustules", "Pustules point to leaf rust.")])
    c.inspect("risk", "risk_card", "Review the forecast", ["Heading now", "Warm, humid week", "Blast reported in the region"], tool=None)
    protect(c, "Choose management", ["High blast risk at heading"],
            [("Protect the healthy tissue and spikes now", True, "protect", PROTECT_FEEDBACK),
             ("Cut the lesions out of the blade", False, None, FIELD_STRIP)])
    plan(c, "Partially resistant variety, clean seed and an earlier sowing date", "The same variety sown at the same time",
         "Heading again in warm, humid weather repeats the risk.")
    recheck(c, "The flag leaf stays protected in this simulation.", "Bleached spikes do not recover; report outbreaks to the extension service.")
    c.source("CIMMYT", "https://www.cimmyt.org/wp-content/uploads/2016/03/Wheat_Blast_Priority_Brief.pdf", REVIEWED,
             "South Asia and South America", "Priority brief; management is still evolving.")
    c.source("CSISA (Cereal Systems Initiative for South Asia)", "https://csisa.org/mitigating-wheat-blast-in-bangladesh", REVIEWED,
             "Bangladesh", "Regional programme guidance.")
    c.specimen_note = "Supplied model was exported under a rice name."
    MANUAL[("wheat", "blast")] = (0.35, ["Eye-shaped lesions with pale centres", "Dark margins", "Bleached spikes in warm, humid weather"],
        [ctl("protect", "protect", 6, 8, 1, 1.2, name="Preventive protection coverage"),
         ctl("prevention", "sowing", 6, 10, 1, 0.8, name="Sowing-date planning", desc="Sowing so heading avoids warm, humid weather."),
         ctl("prevention", "resistance", 6, 10, 1, 0.6, cid="resistance", required=0, name="Variety choice", desc="A partially resistant variety.")],
        [])

    c = case("wheat", "leaf_blight", "Leaf blight (spot blotch)", "Fungal", "Managed",
             "A wheat leaf with dark brown blotches from a late-sown crop in a warm rice-wheat area.",
             "Bipolaris sorokiniana (spot blotch, a main cause of Helminthosporium leaf blight) causes dark brown lesions that merge and dry "
             "the leaf. It is seed-borne, survives on residue, and is worst in warm, humid, stressed crops.",
             "Disease managed: healthy tissue protected and timely sowing and clean seed planned. Existing blotches remain.",
             modules=["risk", "planning"], tools=["shears", "water_wand"])
    field_leaf(c, "Brown blotch", "Flag leaf later in the season")
    c.method("resistance", "Resistant varieties", "Main defence", "For the next crop.")
    c.method("seed", "Clean or treated seed and timely sowing", "Fewer early infections, less heat stress", "Prevention for the next crop.")
    c.method("protect", "Fungicide only when the disease justifies it", "Protects the flag leaf", "Does not cure blotches; follow the local label.")
    inspect_pair(c, ["Dark brown oval blotches", "Blotches merge and dry the leaf"], ["The blotches show on both surfaces"])
    c.confirm("Dark brown merging blotches (spot blotch)",
              [("Eye-shaped pale-centred lesions", "Eye-shaped lesions point to wheat blast.")])
    c.inspect("risk", "risk_card", "Review the crop", ["Late sown", "Warm, humid spell", "Lesions near the flag leaf"], tool=None)
    protect(c, "Choose management", ["Is protection of the flag leaf justified?"],
            [("Yes: protect the flag leaf", True, "protect", PROTECT_FEEDBACK),
             ("Cut the blotches out", False, None, FIELD_STRIP)])
    plan(c, "Resistant variety, clean seed and timely sowing", "Late sowing with saved seed again", "Late sowing and saved seed keep spot blotch severe.")
    recheck(c, "The flag leaf stays protected.", "Reassess if blotches reach the flag leaf early.")
    c.source("Phytopathology (APS), integrated management study, Nepal", "https://apsnet.org/publications/phytopathology/2005/March/Pages/95_3_248.aspx",
             REVIEWED, "Nepal (warm South Asian wheat)", "Research study, not an extension guide.")
    c.specimen_note = "Supplied model was exported under a corn name."
    MANUAL[("wheat", "leaf_blight")] = (0.35, ["Dark brown oval blotches", "Blotches merge and dry the leaf", "Worst in late-sown, warm crops"],
        [ctl("protect", "protect", 5, 7, 1, name="Flag-leaf protection coverage"),
         ctl("prevention", "seed", 6, 10, 1, name="Clean seed and timely sowing"),
         ctl("prevention", "resistance", 6, 10, 1, 0.6, cid="resistance", required=0, name="Resistant variety choice", desc="A resistant variety next season.")],
        [])

    c = case("wheat", "rust", "Leaf rust", "Fungal", "Managed",
             "A wheat leaf with orange-brown pustules from a susceptible variety before heading; volunteer wheat grew nearby.",
             "Puccinia triticina forms small orange-brown pustules on the upper leaf; wind carries spores far, and volunteer wheat bridges "
             "seasons. New races overcome resistance.",
             "Remaining healthy tissue protected and the next crop planned. Existing pustules remain.",
             modules=["risk", "planning"], tools=["shears", "water_wand"])
    field_leaf(c, "Rust pustule", "Flag leaf later in the season")
    c.method("resistance", "Resistant varieties (current ratings)", "Main defence", "Ratings change as new races appear.")
    c.method("volunteer", "Control volunteer wheat", "Breaks the green bridge", "Not an in-season cure.")
    c.method("protect", "Fungicide on susceptible varieties when rust is severe", "Protects the flag leaf", "Does not cure pustules; follow the local label.")
    inspect_pair(c, ["Small orange-brown pustules on the upper surface"], ["Few pustules underneath"])
    c.confirm("Small orange-brown pustules (leaf rust)",
              [("Dark brown merging blotches", "Merging blotches point to spot blotch.")])
    c.inspect("risk", "risk_card", "Review the crop", ["Susceptible variety", "Rust below the flag leaf", "Before heading"], tool=None)
    protect(c, "Choose management", ["Is protection justified?"],
            [("Yes: protect the flag leaf on this susceptible variety", True, "protect", PROTECT_FEEDBACK),
             ("Cut the pustules out", False, None, FIELD_STRIP)])
    plan(c, "A currently resistant variety and no volunteer wheat", "The same susceptible variety with volunteers left",
         "Volunteers carry rust to the new crop.")
    recheck(c, "The flag leaf stays protected.", "Check current variety ratings each season.")
    c.source("Oklahoma State University Extension", "https://extension.okstate.edu/programs/digital-diagnostics/plant-diseases/wheat-leaf-rust.html",
             REVIEWED, "Oklahoma, USA", "Regional guidance.")
    c.source("University of Georgia Extension (C1060)", "https://fieldreport.caes.uga.edu/publications/C1060/identification-and-control-of-leaf-rust-of-wheat-in-georgia/",
             REVIEWED, "Georgia, USA", "Fungicide labels change often.")
    c.specimen_note = "Supplied model was exported under a rice name."
    MANUAL[("wheat", "rust")] = (0.3, ["Small orange-brown pustules", "Upper leaf surface", "Spores travel far on the wind"],
        [ctl("protect", "protect", 6, 8, 1, 1.2, name="Flag-leaf protection coverage"),
         ctl("prevention", "volunteer", 6, 10, 1, 0.8, name="Volunteer wheat control", desc="Removing volunteer wheat that bridges seasons."),
         ctl("prevention", "resistance", 6, 10, 1, 0.6, cid="resistance", required=0, name="Resistant variety choice", desc="A variety with a current resistance rating.")],
        [])
