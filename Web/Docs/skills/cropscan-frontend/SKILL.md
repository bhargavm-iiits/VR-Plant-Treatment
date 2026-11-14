---
name: cropscan-frontend
description: Implement or review the CropScan XR React frontend. Use for the Gemini loading-to-landing homepage, From Field to Leaf pages, shared UI, responsive layouts, and visual matching to the supplied references.
---

# CropScan XR frontend

Use this skill for CropScan XR in `Backend/frontend`. The user request determines whether the task is implementation, a mockup, or review.

## Read what the task needs

- Read the [design system](references/design-system.md) before changing shared tokens, layout, typography or controls. Its accessibility contract applies to every route.
- For the Gemini intro or “From canopy to greenhouse.” hero, use the [Gemini loading and landing layout](references/gemini-landing-layout.md) and its 1280 × 720 stills for the first viewport of Home.
- For the From Field to Leaf atlas or other screens, find the target route and screenshot in the [page specifications](references/page-specifications.md). The [full visual overview](../../design-references/field-to-leaf-overview.jpg) gives context for the page sequence.
- Check the [integration and verification guide](references/integration-and-qa.md) for data wiring, supported states, and checks to run.
- For scroll animation, transitions, render fallbacks and artwork, use the [motion skill](../cropscan-web-motion/SKILL.md). Scope it to the surface being changed.

## Preserve the app contracts

- Screenshots control visual decisions, dimensions and hierarchy. Runtime code and API responses control data, route semantics, visibility and capability. Treat example photos, diagnoses, weather and metrics as reference content.
- Keep the original browser routes and query parameters, localized strings, server-issued session, uploads, result recovery on refresh, user and device eligibility, and WebXR fallback behavior.
- Reproduce the selected reference with live text, controls, and separate media. Share the site header, locale handling, and route behavior. Apply the Gemini peach and olive palette to its Home hero and use the design-system palette for From Field to Leaf surfaces.
- Keep the landing page functional as plain web UI. Route-heavy modules remain lazy in accordance with the repository build budget.

## Complete the requested surface

Check the relevant reference and normal user state, then exercise the route’s empty, pending, failure and fallback states where they apply. Compare at the reference size and at a narrow viewport. Record the actual implementation checks and any verification that still needs a backend or headset.
