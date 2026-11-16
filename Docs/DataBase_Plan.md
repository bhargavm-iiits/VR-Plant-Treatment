# Plant Disease Database and VR Treatment Plan

## 1. Summary

Build an **offline Unity treatment database covering the project's 9 plants and 24 disease/pest classes**, with guided, hands-on VR lessons for every case.

The current project already provides:

- The complete disease list in [DiseaseClasses.cs](<D:/Unity Projects/Plant Disease/BTP_Project/Assets/_Project/Scripts/Runtime/Plants/DiseaseClasses.cs>).
- A shared illustrative six-week recovery table in [TomatoRecoveryData.cs](<D:/Unity Projects/Plant Disease/BTP_Project/Assets/_Project/Scripts/Runtime/Lab/TomatoRecoveryData.cs>).
- A detached-leaf inspection display in [LabPlantDisplay.cs](<D:/Unity Projects/Plant Disease/BTP_Project/Assets/_Project/Scripts/Runtime/Lab/LabPlantDisplay.cs>).
- Gloves, pruning shears, gardening tools, and spray/watering asset archives that can support future interactions.

**The main change:** recovery will depend on completing appropriate treatment tasks and follow-up inspections. Pressing "Next Week" will no longer make every disease disappear.

Use three clearly explained outcomes:

| Outcome | What the learner sees |
|---|---|
| **Disease managed** | Existing damage remains, but remaining foliage is protected and healthy new growth develops where appropriate. |
| **Pest suppressed** | Pest activity decreases; subsequent growth shows less damage. |
| **Healthy replacement established** | The infected plant is removed and a separate healthy specimen is planted after controlling the source of spread. |

Confirmed preferences: realistic outcomes, integrated methods, offline data, hands-on guidance, and general educational guidance without pesticide doses.

**Implementation status: plan only.** This document describes future work; saving it does not implement the database, treatment interactions, or changes to existing scenes and scripts.

## 2. Database structure

Use **one shared catalog containing 24 treatment records**, rather than creating a separate database system for every disease. Implement the catalog and records as Unity ScriptableObjects.

Use the existing combination of `plantId + diseaseId` as the unique key. Disease names such as `black_rot` occur on multiple plants and must remain separate records.

Each record should contain:

| Data group | Required contents |
|---|---|
| Identity | Plant ID, disease ID, display name, cause category, relevant pathogen name and aliases. |
| Recognition | Main symptoms, affected plant parts, similar-looking problems, and when confirmation is needed. |
| Spread and conditions | Transmission route, infection sources, and conditions favouring the problem. |
| Management methods | Sanitation, environmental changes, resistant planting material, physical control, supported biological control, and appropriate pesticide categories. |
| Purpose and limitations | Whether each method prevents infection, suppresses spread, protects healthy tissue, or supports replacement. |
| VR instructions | Ordered steps, required tools, valid targets, prerequisites, completion conditions, and corrective feedback. |
| Monitoring and outcome | What to inspect later, signs of improvement or failure, escalation actions, and the correct visual endpoint. |
| Evidence | Source URL, publisher, publication/review date, region, and limitations of the evidence. |

Methods should be presented as **appropriate alternatives and combinations**. Learners should not have to apply every available treatment to finish a lesson.

Add these interfaces:

- `TreatmentCatalog.TryGet(plantId, diseaseId)` retrieves the correct treatment record.
- `TreatmentSession.TryPerform(actionId, targetId)` validates an attempted action.
- `TreatmentSession.StateChanged` updates instructions, tools, plant appearance, and progress.
- Outcome values distinguish `Managed`, `PestSuppressed`, and `Replaced`.

Keep treatment content separate from learner progress. Preserve progress for each plant-disease case while switching plants and environments; restarting the application resets progress in v1.

Pesticide entries should explain the applicable treatment category and timing. Product choice, dose, application intervals, harvest intervals, and re-entry intervals remain dependent on the applicable local label.

## 3. Shared VR lesson design

Each lesson follows this sequence:

1. **Inspect the specimen.** Rotate the existing leaf model, examine both surfaces, and inspect the relevant whole-plant parts.
2. **Confirm the teaching case.** Match symptoms and spread clues to the supplied diagnosis. Virus lessons include a simulated confirmation result.
3. **Prepare.** Select suitable equipment and complete the required hygiene or protective-equipment steps.
4. **Reduce the infection source.** Perform the case-specific sanitation, pest containment, or removal task.
5. **Correct growing conditions.** Adjust irrigation, airflow, spacing, support, or other relevant conditions.
6. **Choose additional management.** Apply a justified protective treatment, use supported biological control, or select monitoring when treatment is unnecessary.
7. **Advance to a simulated follow-up.** Inspect whether new symptoms or active pests remain.
8. **Explain the result.** Identify what improved, what damage remains, and how recurrence will be reduced.

### Treatment station and interactions

Keep the detached leaf as an inspection reference. Add a **whole-plant treatment specimen** with individually selectable leaves, shoots, fruit, soil controls, and replacement locations.

Existing whole-plant models can remain the visual base. Add detachable treatment pieces and colliders instead of requiring runtime cutting of their original meshes.

| Interaction | What counts as completion |
|---|---|
| Pruning/removal | The learner selects an authorised target, removes it with the appropriate tool, and deposits it in the disposal container. |
| Tool hygiene | The contaminated tool completes the prescribed cleaning/disinfection interaction before touching another specimen. |
| Irrigation | The learner positions the emitter at the root zone and selects the appropriate watering arrangement. |
| Ventilation | The learner changes vents/fans and checks the environmental-risk display. |
| Protective spraying | The learner selects the appropriate treatment category and covers the designated surfaces within its permitted scenario conditions. |
| Biological control | The learner identifies the pest and places an appropriate predator-release item in the hotspot. |
| Replacement | The learner contains/removes the infected specimen, completes sanitation or vector control, then plants clean stock. |
| Monitoring | The learner examines designated sites and records the follow-up finding. |

Provide controller grabbing and hand interactions, plus an equivalent seated/ray-control action for every task.

Add the remaining teaching props: magnifier, disposal container, sanitation station, drip emitter, trellis/support, ventilation controls, predator-release container, insect mesh, clean planting stock, and a trunk/tuber inspection cutaway.

## 4. Treatment plans for all 24 cases

The management guidance below supplies the lesson content. The VR sequences are proposed teaching activities based on that guidance.

### Apple - 3 diseases

| Disease | Management methods | VR steps and realistic result |
|---|---|---|
| **1. Apple scab - fungal** | Reduce infected fallen-leaf material; improve canopy drying; use resistant varieties for future planting. Where justified, protect susceptible growth with a locally approved scab fungicide timed to infection risk. Existing scabs remain. [UC IPM](https://ipm.ucanr.edu/agriculture/apple/apple-scab/) | Inspect olive-dark lesions → collect fallen leaves → improve canopy airflow → examine a spring wet-weather risk card → select timely protection → inspect later foliage. **Result:** reduced new infection, with old scabs still visible. |
| **2. Black rot - fungal** | Remove mummified fruit, dead wood, and cankers; reduce tree stress and injuries. Appropriate fungicides can protect fruit, but sanitation is central. Extensive structural damage needs specialist assessment. [Penn State Extension](https://extension.psu.edu/pome-fruit-disease-black-rot-and-frogeye-leaf-spot) | Inspect fruit, leaves, and a canker → collect fruit mummies → prune marked affected branches → dispose of debris → clean equipment → select fruit protection if indicated → revisit healthy tissue. **Result:** infection sources removed and healthy parts protected; rotten fruit never becomes sound fruit. |
| **3. Cedar apple rust - fungal** | Use resistant varieties; manage accessible juniper galls before spore release where practical; use timely spring protection for susceptible apples when warranted. Nearby-host management reduces local risk but cannot exclude windborne spores. Spraying established spots does not repair them. [UMN Extension](https://extension.umn.edu/garden-and-home/yard-and-garden/gardening-in-minnesota/yard-and-garden-problems/cedar-apple-rust) | Inspect orange apple spots → inspect the alternate juniper host → remove a marked gall in the appropriate seasonal stage → select resistance/protection for the next risk period → inspect a later season. **Result:** fewer new infections; existing spotted leaves remain damaged. |

### Bell pepper - 1 disease

| Disease | Management methods | VR steps and realistic result |
|---|---|---|
| **4. Bacterial spot - bacterial** | Start with clean seed/transplants and resistant varieties; avoid wet handling and overhead irrigation; sanitise equipment and remove infected material. Copper-based protection has limitations, including resistance, and is not a reliable cure for established infection. [UC IPM](https://ipm.ucanr.edu/agriculture/peppers/bacterial-spot/), [UMN Extension](https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/bacterial-spot-of-tomato-and-pepper) | Inspect leaf and fruit spots → reject a diseased transplant → remove limited marked affected foliage from the established specimen → sanitise tools → install drip irrigation → assess whether protective treatment is justified → monitor. **Result:** spread reduced and healthier growth protected; badly affected young plants are replaced. |

### Cherry - 1 disease

| Disease | Management methods | VR steps and realistic result |
|---|---|---|
| **5. Powdery mildew - fungal** | Scout susceptible shoots and fruit; improve canopy conditions; use a cherry-labelled mildew treatment where needed. Options include appropriate conventional fungicides and some sulfur or potassium-bicarbonate products. Suitability depends on growth stage, temperature, and compatibility. [UC IPM](https://ipm.ucanr.edu/agriculture/cherry/powdery-mildew/) | Examine powdery growth on inner shoots → improve canopy access → read the treatment-stage card → select a compatible mildew treatment → cover the targets → revisit shoots and fruit. **Result:** mildew activity is suppressed and subsequent growth protected; damaged or distorted tissue is not instantly repaired. |

### Corn - 3 diseases

**Corn lessons should protect functional foliage and crop development. Routine stripping of diseased leaves should not be taught as the treatment.**

| Disease | Management methods | VR steps and realistic result |
|---|---|---|
| **6. Gray leaf spot - fungal** | Use resistant hybrids, crop rotation, and suitable residue management. Consider foliar fungicide only when crop stage, susceptibility, disease development, and conditions justify it. [UMN Extension](https://extension.umn.edu/agriculture/crop-production/corn/gray-leaf-spot-on-corn) | Inspect rectangular lesions → check the ear leaf and upper canopy → review crop stage/weather → choose monitoring or justified protection → plan a non-corn rotation and resistant hybrid → revisit. **Result:** remaining healthy foliage is protected; dead rectangles remain. |
| **7. Common rust - fungal** | Resistant hybrids are the main defence. Mild disease may need monitoring only; early, justified fungicide treatment can help susceptible crops. Windborne infection means local debris cleanup alone is insufficient. [UMN Extension](https://extension.umn.edu/agriculture/crop-production/corn/common-rust-on-corn) | Inspect pustules on both leaf surfaces → assess severity and crop stage → select monitoring for the default mild case → examine a higher-risk treatment example → choose a resistant hybrid for the next crop. **Result:** the learner avoids unnecessary spraying and protects crop function. |
| **8. Northern leaf blight - fungal** | Use resistant hybrids, rotation, and appropriate residue practices. Early disease in a susceptible crop can justify fungicide protection; existing cigar-shaped lesions remain dead tissue. [UMN Extension](https://extension.umn.edu/agriculture/crop-production/corn/northern-corn-leaf-blight) | Identify elongated lesions → inspect progression toward the upper canopy → assess stage and susceptibility → choose protection when indicated → configure next-season rotation/resistance → revisit. **Result:** less disease progression into functional foliage, rather than immediate regeneration of damaged leaves. |

### Grape - 3 diseases

| Disease | Management methods | VR steps and realistic result |
|---|---|---|
| **9. Black rot - fungal** | Remove mummified berries and infected material; prune/train for canopy drying; protect susceptible tissue during relevant growth stages with an appropriate fungicide programme where needed. [Iowa State Extension](https://yardandgarden.extension.iastate.edu/encyclopedia/black-rot-grape) | Inspect leaf spots and berry mummies → collect mummies from vine and ground → remove marked affected material → adjust trellis/canopy → choose appropriately timed protection → inspect later clusters. **Result:** healthy clusters remain protected; infected berries are removed. |
| **10. Esca / black measles - trunk-disease complex** | Established wood infections cannot be eradicated by foliar fungicide. Management includes pruning-wound prevention, removal of affected wood, specialist retraining/surgery in suitable vines, and replacement of severely affected vines. [UC IPM: Esca](https://ipm.ucanr.edu/agriculture/grape/esca-black-measles/), [trunk-disease management](https://ipm.ucanr.edu/agriculture/grape/eutypa-dieback/) | Inspect striped leaves → inspect a trunk cutaway → distinguish superficial symptoms from wood infection → identify the severe specimen for replacement → remove it through an assisted interaction → establish clean stock → demonstrate future pruning-wound protection. **Result:** a healthy replacement vine; the original infected trunk is not shown as cured. |
| **11. Leaf blight / Isariopsis leaf spot - fungal** | Protect foliage against premature loss, including after harvest where relevant. Use cultivar-appropriate management and treatments supported for this particular disease. Do not assume a generic copper/Bordeaux treatment is effective; Embrapa reports poor efficacy from Bordeaux mixture in the studied context. [Embrapa](https://www.infoteca.cnptia.embrapa.br/infoteca/bitstream/doc/1060452/1/ComunicadoTecnico1891.pdf) | Inspect leaf blotches and premature drop → check the susceptible-host case notes → review remaining canopy health → choose disease-specific protection → revisit the post-harvest canopy. **Result:** retained functional foliage and better protection of the following growth cycle; old lesions persist. |

For the Isariopsis lesson, explicitly author a susceptible grape-host scenario rather than assuming that every grape cultivar has the same susceptibility.

### Peach - 1 disease

| Disease | Management methods | VR steps and realistic result |
|---|---|---|
| **12. Bacterial spot - bacterial** | Prioritise resistant varieties, sound nutrition, reduced stress, and appropriate cultural care. Chemical control is difficult. Copper-based options require particular attention to crop stage and label suitability. Established fruit pits and leaf holes do not heal. [Clemson Extension](https://hgic.clemson.edu/factsheet/peach-diseases/) | Inspect shot-hole leaves and pitted fruit → assess tree stress → correct the supplied irrigation/nutrition problem → evaluate whether the treatment window permits protection → select resistance for future planting → monitor later tissue. **Result:** improved tree condition and reduced new damage, with existing scars retained. |

### Potato - 2 diseases

| Disease | Management methods | VR steps and realistic result |
|---|---|---|
| **13. Early blight - fungal** | Use clean planting material, rotation away from related hosts, volunteer control, appropriate nutrition and irrigation, and protection where disease pressure warrants it. Avoid wet foliage and unnecessary canopy removal. [UMN Extension](https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/early-blight-in-tomato-and-potato) | Inspect older leaves for concentric lesions → correct water stress → remove volunteer hosts → set root-zone irrigation → select justified protective treatment → plan rotation → revisit. **Result:** remaining foliage is protected and disease progression reduced; old lesions remain. |
| **14. Late blight - oomycete** | Rapidly contain affected plants; eliminate infected tubers, cull sources, and volunteers; use certified seed potatoes. Protect threatened healthy plants with treatments active against late blight where warranted. Ordinary fungicide selection cannot be assumed suitable. [UMN Extension](https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/late-blight) | Inspect foliage and a tuber cutaway → trigger a simulated confirmation/adviser step → contain the local infected specimen and tubers → remove volunteer sources → protect neighbouring healthy hosts → select clean seed for a later crop. **Result:** outbreak containment and a healthy subsequent crop. |

### Strawberry - 1 disease

| Disease | Management methods | VR steps and realistic result |
|---|---|---|
| **15. Leaf scorch - fungal** | Use suitable resistant planting material, spacing, weed control, drip irrigation, and removal of old foliage/residue at the appropriate post-harvest renovation stage. Fungicide need depends on production system and pressure; mild annual-system cases may require no treatment. [NC State Extension](https://content.ces.ncsu.edu/leaf-scorch-of-strawberry) | Inspect purple lesions and scorched margins → identify the lesson's perennial-bed setting → clear weeds and adjust irrigation → advance to post-harvest renovation → remove marked old foliage without damaging crowns → review later leaves. **Result:** healthier subsequent foliage and lower inoculum, rather than immediate repair of scorched leaves. |

### Tomato - 9 diseases/pest classes

| Disease or pest | Management methods | VR steps and realistic result |
|---|---|---|
| **16. Bacterial spot - bacterial** | Use clean planting material; avoid wet handling, overhead irrigation, and contaminated equipment. Remove affected material selectively. Chemical options are limited and copper resistance is documented. Established infection is managed, rather than reliably cured. [UMN Extension](https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/bacterial-spot-of-tomato-and-pepper) | Inspect small spots and fruit lesions → remove marked affected foliage without stripping the plant → disinfect tools → install drip irrigation → evaluate the protective-treatment option and its limitations → monitor new tissue. **Result:** reduced spread; heavily affected transplants are replaced. |
| **17. Early blight - fungal** | Stake or trellis plants, mulch against soil splash, water at the base, remove limited infected lower foliage, rotate crops, and use resistant/tolerant varieties. Protective fungicide is an additional option where justified. [UMN Extension](https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/early-blight-in-tomato-and-potato) | Inspect concentric spots → remove designated lower leaves → dispose and clean tools → stake the plant → apply mulch → position drip irrigation → assess protection → revisit. **Result:** healthier subsequent growth; remaining old lesions stay visible. |
| **18. Late blight - oomycete** | Confirm promptly, contain infected plants, inspect neighbouring tomatoes and potatoes, remove infection sources, and use appropriate late-blight protection for threatened healthy plants when warranted. [UMN Extension](https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/late-blight) | Inspect rapidly expanding lesions, stems, and fruit → obtain the simulated confirmation → contain/remove the local infected specimen → inspect neighbouring hosts → choose appropriate protection → establish clean stock in a later planting stage. **Result:** containment and healthy replacement/subsequent planting. |
| **19. Leaf mold - fungal** | Reduce prolonged high humidity through ventilation and spacing; avoid wetting foliage; use suitable resistant varieties and sanitation. Protective fungicides may supplement environmental management. Resistance can depend on pathogen race. [UMN Extension](https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/tomato-leaf-mold) | Turn the leaf over to inspect olive mold → check greenhouse humidity → open vents and adjust fans → improve spacing → change irrigation → remove marked affected material → assess protection → revisit. **Result:** reduced new infection and healthier growth after environmental correction. |
| **20. Septoria leaf spot - fungal** | Remove limited infected lower foliage; mulch, stake, keep foliage dry, manage infected residue, and rotate crops. Appropriate protective fungicides can supplement these measures. [UMN Extension](https://extension.umn.edu/garden-and-home/yard-and-garden/gardening-in-minnesota/yard-and-garden-problems/tomato-leaf-spot-diseases) | Examine small spots and dark fruiting bodies with the magnifier → remove selected affected leaves → dispose and sanitise → mulch and stake → set root-zone watering → assess protection → revisit upper foliage. **Result:** reduced upward spread; old spots are retained. |
| **21. Two-spotted spider mite - pest** | Confirm live mites; correct water stress and dusty conditions; conserve or release suitable predatory mites. Alternative controls include directed water washing, labelled insecticidal soaps/oils, or selective miticides. Avoid incompatible treatments that harm predators. [UC IPM](https://ipm.ucanr.edu/home-and-landscape/spider-mites/) | Inspect undersides for live mites/webbing → correct water stress → select either a compatible predator route or a justified contact-treatment route → treat the hotspot → inspect again for active mites and renewed damage. **Result:** lower pest activity and healthier subsequent growth; old stippling remains. |
| **22. Target spot - fungal** | Use clean transplants, manage weeds/volunteers/residue, rotate crops, and reduce prolonged canopy wetness. Effective protective fungicides are important under high pressure; resistance affects product performance. Do not automatically reuse an early-blight resistance claim. [UF/IFAS](https://ask.ifas.ufl.edu/publication/PP351) | Inspect inner-canopy lesions and fruit → review the supplied diagnosis → remove marked infected material → improve canopy drying → select an appropriate treatment programme with resistance-management guidance → revisit. **Result:** protected healthy tissue and reduced progression; advanced damage remains. |
| **23. Yellow leaf curl virus - viral** | Infected plants have no curative treatment. Use clean/resistant transplants, manage whitefly vectors, protect planting material with appropriate mesh, remove isolated infected plants, and control relevant volunteers/weeds. [UC IPM](https://ipm.ucanr.edu/agriculture/tomato/tomato-yellow-leaf-curl/), [UMN: tomato viruses](https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/tomato-viruses) | Inspect curled yellow leaves and whiteflies → read simulated confirmation → contain vectors before moving the plant → remove the infected specimen → complete vector/source management → install mesh → plant clean resistant stock → monitor. **Result:** a healthy replacement, with continued vector monitoring. |
| **24. Mosaic virus - viral** | Author this as a confirmed tomato-mosaic/tobamovirus case. Remove infected plants; use clean planting material and suitable resistance; clean contaminated tools, hands, supports, and work surfaces with validated procedures. Tobamoviruses spread mechanically; insecticide is not the cure. [UC IPM](https://ipm.ucanr.edu/agriculture/tomato/tobacco-mosaic/), [UMN Extension](https://extension.umn.edu/agriculture/specialty-crops/vegetable-farming/disease-management/tomato-viruses) | Inspect mottling and deformation → obtain simulated confirmation → contain/remove the plant → complete virus-appropriate sanitation → replace contaminated handling materials as prescribed → establish clean stock → inspect later. **Result:** a distinct healthy replacement; the infected plant never turns healthy through spraying. |

## 5. Implementation and validation

### Implementation order

1. **Author the catalog and evidence.** Create all 24 records using the existing IDs, with management choices, VR steps, monitoring, and outcomes.
2. **Build the shared lesson engine.** Implement action validation, prerequisites, alternative treatment routes, corrective feedback, and per-case progress.
3. **Build reusable treatment interactions.** Add inspection, pruning, disposal, sanitation, irrigation, spraying, biological release, and replacement.
4. **Connect the Lab and Farm.** Preserve the selected disease and treatment state across transitions; show the same outcome in both environments.
5. **Replace universal recovery behaviour in treatment lessons.** Retain the original recovery asset during migration, but remove its control over treatment outcomes.
6. **Complete all 24 lessons and review them.** Check agricultural accuracy, visual outcomes, accessibility, and headset performance.

### Visual and progression rules

- Preserve necrotic lesions and permanent scars on retained tissue.
- Show improvement through protected foliage, appropriate new growth, reduced pest activity, or replacement.
- Clearly distinguish the original plant from a replacement specimen.
- Use crop-stage or follow-up checkpoints instead of a universal six-week cure.
- Label time compression as a simulation.
- Replace invented chlorophyll/health percentages with task progress and observed-condition descriptions.
- Require completed actions and monitoring before awarding lesson completion. Timeline scrubbing alone cannot complete a treatment.

### Acceptance tests

- All **24 canonical plant-disease pairs** resolve to the correct treatment record, without cross-plant ID collisions.
- Every record has a source, management limitations, a complete action sequence, monitoring, and an explicit endpoint.
- Wrong tools, wrong targets, premature actions, and incompatible choices produce feedback without advancing progress.
- Valid alternative methods can complete their intended routes; unnecessary spraying is never required.
- Corn lessons do not reward stripping foliage.
- Virus and severe-Esca lessons cannot finish by greening the infected specimen.
- Late-blight lessons distinguish water-mould management from generic fungal treatment.
- Switching diseases or scenes preserves the correct case and progress.
- Reset affects only the selected lesson.
- Every action works through hands/controllers and the seated/ray alternative.
- Generated scenes retain treatment wiring after rebuilding.
- Quest 2 testing meets the project's existing **72 FPS target**.
- A final content review verifies that prevention, suppression, and replacement are described accurately.

**Defaults:** educational training cases; one diagnosed problem per specimen; offline content; session-only progress; no accounts, backend, real-time diagnosis, pesticide mixing instructions, or universal recovery guarantee.
