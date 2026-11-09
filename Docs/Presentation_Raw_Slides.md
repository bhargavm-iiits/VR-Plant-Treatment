# Plant Disease VR Experience — raw presentation slides

**Draft for Bhargav M to review. No PowerPoint or presentation video has been made.**

The supplied brief gives two duration ranges: **8–10 minutes for the uploaded video** and **10–12 minutes for the presentation**. This draft targets their shared boundary, **10:00 total**. Confirm the rule with the evaluator before recording if a small timing difference could affect submission.

The previous evaluation was a **360° VR Movie Player**, completed as a separate project. The current Plant Disease VR Experience is new. Do not describe the earlier player as a module of the current application.

**PPT style after approval:** Use a clean light theme: off-white or very pale green backgrounds, dark charcoal text, soft green and cyan accents, large readable headings, and little text per slide. Use **only screenshots from the built Unity scenes**. Do not use the farm/lab concept images, stock images, or image-generation images in the PPT.

## Slide 1 — Introduction: Plant Disease VR Experience (0:25)

**On-slide text**

- Plant Disease VR Experience
- Explore plants in a virtual farm and inspect an illustrative disease scenario in a lab
- Bhargav M · BTP project

**Visual:** Split view using the farm and lab Unity editor previews. Caption both as *Unity editor previews*.

**Speaker cue:** Introduce the project as an interactive VR prototype. State that the presentation covers its motivation, related work, implementation, and what has been verified so far.

## Slide 2 — Introduction: problem and motivation (0:50)

**On-slide text**

- Plant pests and diseases reduce global crop yields by an estimated 20–40% each year. [1]
- Leaf symptoms and disease progression can be difficult to communicate using static pictures alone.
- Project objective: let a learner inspect 3D plants and explore one visual recovery example in VR.

**Visual:** The built farm scene screenshot below, labeled *Unity editor preview*, beside a small crop-loss statistic.

**Speaker cue:** Explain why plant health matters and why spatial exploration may help communicate visible symptoms. Make clear that the current build is an educational visualization, not an automated diagnostic tool.

![Built farm scene, Unity editor preview](../Tools/out/previews/scene_spawn.png)

## Slide 3 — Literature Review / Related Work (0:55)

**On-slide text**

- Plant-image research shows that disease recognition is possible, but results can change outside controlled image conditions. [2]
- Plant-learning research has explored immersive VR as a way to engage learners. [3]
- Our project currently uses VR to explore plants and visualize one example disease journey. Automated classification is future work.

**Visual:** A small crop from the built lab plant screenshot, labeled *Unity editor preview*. Keep this slide mostly text.

**Speaker cue:** Summarize the two studies in plain language. The current application does not train or run an image classifier, and it has not yet measured learning outcomes.

## Slide 4 — Quick Recap: previous evaluation (0:45)

**On-slide text**

- Previous evaluation: **360° VR Movie Player** for playing VR movies.
- Status: completed, as confirmed by the presenter.
- Current evaluation: a new, real-time Plant Disease VR project with selectable 3D plants.

**Visual:** A text-only comparison headed *Previous project* and *Current project*. Use no earlier-project image, so every image in the PPT comes from the current built Unity scenes.

**Speaker cue:** Spend less than a minute on the earlier evaluation. Explain the change from viewing 360° video to interacting with a real-time Unity environment. Do not claim code reuse or test results from the earlier project without evidence.

## Slide 5 — Methodology / Proposed Approach (1:05)

**On-slide text**

1. Start in the farm and inspect a labeled plant.
2. Select a plant and enter the lab through a world-space button.
3. Inspect its 3D model and information panel.
4. For Tomato, move through a six-week Early Blight visualization, then return to the farm.

**Design boundary:** The six-week values are illustrative sample data. The experience does not infer disease from a camera image.

**Visual:** Use built farm and lab scene screenshots with the short labels *Farm*, *Select plant*, *Lab*, and *Tomato demo*. Do not use concept images.

**Speaker cue:** Describe the journey from the learner's perspective before discussing software. Mention that the source list has nine species, while seven are integrated into the current selector.

## Slide 6 — Implementation Details: Unity and XR architecture (0:55)

**On-slide text**

- Unity 6000.5.5f1 with the Universal Render Pipeline
- OpenXR, XR Interaction Toolkit, and XR Hands packages
- One persistent XR bootstrap scene; farm and lab load as separate environment scenes
- Scene changes use a fade and keep the selected plant

**Visual:** Use the built farm and lab editor previews with three simple PowerPoint text labels: *XRBootstrap*, *Farm (Envirornment.unity)*, and *Lab*. Do not add an external diagram or reference image.

**Speaker cue:** Explain why one XR rig remains active while the environment changes: it preserves the camera and interaction setup. Controller interaction, teleport locations, and world-space UI are implemented in the scenes. Avoid saying they have been validated on a headset.

## Slide 7 — Implementation Details: plants and asset workflow (1:05)

**On-slide text**

- **Plants used now:** Apple, Bell Pepper, Cherry, Corn (Maize), Potato, Strawberry, and Tomato.
- I started with supplied FBX/GLB plant models, reduced heavy meshes in Blender, and imported them into Unity. The Tomato farm mesh went from about 1.62 million to 8,000 triangles.
- I applied textures and URP materials, then made separate farm and lab prefabs.
- Farm prefabs switch to lightweight billboard versions at a distance.

**Visual:** Use the actual Unity farm editor preview below. Caption: *Farm scene, Unity editor preview*. Put the seven current plant names in a simple readable strip.

![Farm scene, Unity editor preview](../Tools/out/previews/scene_specimens.png)

**Speaker cue:** Describe the sequence: source model, Blender optimization, Unity texture and material setup, then separate Farm and Lab prefabs. The Tomato triangle reduction is documented in the asset-processing log. Grape and Peach source models have now been added, but their Unity prefabs and selector entries are still pending.

## Slide 8 — Implementation Details: lab and Tomato demo (1:00)

**On-slide text**

- The lab uses a central plant pedestal and world-space selection, information, and recovery panels.
- The Tomato example shows Early Blight across six selectable weeks.
- Week 3 is labeled **Demo treatment applied**; health bars and plant appearance change by week.
- The interface states **Illustrative prototype. Not diagnostic or treatment advice.**

**Visual:** Use the two built lab scene images below side by side: a close view of the plant and a wider view showing the plant-selection panel. Caption both *Unity editor preview*. Do not use the static lab front preview's 0% metric bars as proof of runtime values; its UI had not initialized when that image was captured.

![Lab plant, Unity editor preview](../Tools/out/previews/scene_lab_plant.png)

![Lab selection panel, Unity editor preview](../Tools/out/previews/scene_lab_left.png)

**Speaker cue:** Describe the fixed example values and before/current visual comparison. Other selectable species can be inspected, but their panel redirects to the Tomato demo rather than inventing species-specific recovery data.

## Slide 9 — Results and Analysis: verified project outputs (1:05)

**On-slide text**

| Verified in the project folder | Evidence |
|---|---|
| Three enabled scenes | XR bootstrap, farm, and lab in Build Settings |
| Seven integrated crops | Plant catalog plus 14 farm/lab prefabs |
| Tomato six-week example | Recovery data asset and UI code |
| **16 of 16 EditMode tests passed** | Unity Editor test log |
| Quest APK and Windows executable created | Files in Builds/Quest and Builds/Windows |

**Visual:** Farm and lab editor previews side by side, with a small **16/16 EditMode tests** callout.

**Speaker cue:** State what each result actually proves. The tests cover catalog/selection rules and timeline data/logic. Build artifacts show packaging completed; they do not establish headset comfort, rendering speed, or interaction success on device.

## Slide 10 — Results and Analysis: limits of the current evaluation (0:55)

**On-slide text**

- No recorded Quest 2, Quest 3, or PC headset performance results are in the folder.
- Controller and hand interaction still need on-device usability checks.
- Grape and Peach source models exist but are not integrated into the selector.
- The Tomato percentages are sample values; disease classification and treatment guidance are not implemented.

**Visual:** Two-column *Verified now* / *Next validation* table. Keep the distinction visible throughout the spoken explanation.

**Speaker cue:** Explain why the current evidence supports calling this a functional Unity prototype but not a validated plant diagnosis system. Do not report an FPS figure or user-study outcome without collecting it.

## Slide 11 — Conclusion and Future Work (0:40)

**On-slide text**

- **Current result:** a farm-to-lab VR prototype with seven selectable plants and one illustrative Tomato example.
- **Next validation:** test on Quest 2/3 and PC VR; measure frame rate, interaction, and learner usability.
- **Next plants:** optimize and integrate the new Grape and Peach source models.
- **Future disease system:** classify five verified diseases for each supported plant, plus a healthy class.
- **Future interactive care:** let users choose treatments; where appropriate, choose fertilizer type and proportion, then view a simulated response.

**Visual:** One farm editor preview transitioning to the lab editor preview, both labeled as previews.

**Speaker cue:** Keep the current prototype and future work clearly separate. Before giving fertilizer or treatment recommendations, obtain reliable plant data and review choices with agricultural experts. Fertilizer is relevant to some care problems but is not a general cure for every plant disease.

## Slide 12 — References (0:20)

**On-slide references**

1. FAO. “Understanding the context: Pest and Pesticide Management.” [1]
2. Mohanty, S. P., Hughes, D. P., and Salathé, M. (2016). “Using Deep Learning for Image-Based Plant Disease Detection.” *Frontiers in Plant Science*, 7, 1419. [2]
3. Cheng, K.-H. (2024). “Development of an immersive virtual reality system for learning about plants in primary education.” *Educational Technology Research and Development*, 72, 845–867. [3]
4. Unity project source, assets, editor test log, and build outputs, reviewed 25 September 2026.

**Speaker cue:** Give a short attribution to the research and the project’s own evidence. Full links can appear in the PPT notes or a small references appendix.

---

## Source links for the presenter and later PPT

- [1] [FAO estimate of crop yield losses](https://www.fao.org/pest-and-pesticide-management/about/understanding-the-context/en/)
- [2] [Mohanty et al. (2016), original paper](https://www.frontiersin.org/journals/plant-science/articles/10.3389/fpls.2016.01419/full)
- [3] [Cheng (2024), original article](https://link.springer.com/article/10.1007/s11423-023-10300-6)
- Project scene list: ../ProjectSettings/EditorBuildSettings.asset
- Plant catalog: ../Assets/_Project/Data/PlantCatalog.asset
- Plant asset workflow: ../Assets/_Project/Scripts/Editor/PlantAssetPipeline.cs and ../Tools/decimate_all.ps1
- Tomato optimization figures: ../Tools/logs/decimate.log
- Tomato example data: ../Assets/_Project/Data/TomatoRecoveryData.asset
- Passing test result: ../Logs/Editor-prev.log, line containing “Run finished: 16 total, 16 passed, 0 failed”
- Build outputs: ../Builds/Quest/PlantDiseaseVR.apk and ../Builds/Windows/PlantDiseaseVR.exe
- Actual environment preview images: ../Tools/out/previews/scene_specimens.png, ../Tools/out/previews/scene_lab_plant.png, and ../Tools/out/previews/scene_lab_left.png

## Recording and submission checklist from the supplied brief

- Keep Bhargav M’s face clearly visible throughout the 10-minute presentation video.
- Upload the video and every other required project material through the stated form before its deadline. The deadline and form URL were not supplied here.
- If other students are part of the group, use the same video and clearly identify each person’s contribution.
- Rehearse to the 10:00 target because the brief states both 8–10 and 10–12 minutes.

**Approval point:** Confirm this slide content and any institute/guide details before it is converted into a PPT.
