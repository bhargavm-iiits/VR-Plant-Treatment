---
name: cropscan-web-motion
description: Implement or review CropScan XR web motion and 3D scenes, including the Gemini pot-to-greenhouse homepage intro, From Field to Leaf transitions, loading states, and scene assets.
---

# CropScan XR motion and 3D

Follow the [frontend skill](../cropscan-frontend/SKILL.md) for UI and route contracts.

- For the Gemini reference, “Loading the field.” or “From canopy to greenhouse.” on Home, start with [pot-to-greenhouse animation](references/gemini-intro-animation.md). The 1280 × 720 Gemini frames govern this intro and its settled hero, including framing, lighting, motion, and timing. Use the app's existing intro and renderer as the implementation entry points.
- For the From Field to Leaf atlas and other motion, use the [numbered visual overview](../../design-references/field-to-leaf-overview.jpg) to keep terrain and greenhouse imagery consistent across scales.
- Read [motion, loading and 3D](references/motion-loading-and-3d.md) for the atlas scroll sequence, route-specific loading, and Field fallback targets.
- Read [art generation prompts](references/asset-prompts.md) when creating From Field to Leaf artwork. Use the Gemini animation guide's master prompt for the pot-to-greenhouse assets.

Keep Home UI in HTML. Use the static poster path for the atlas or fallback and keep decorative 3D lazy on Home and Field. Tie live loading feedback to actual readiness. Compare the scene at the requested reference beats, and exercise the controls with reduced motion and with media or WebGL unavailable.
