# Integration and verification

Use this guide when the task includes implementation. The current files take precedence for API details, supported states and navigation. The design pack gives target appearance and behavior.

## Workspace and entry points

| Area | Current entry point or owner |
| --- | --- |
| App and route tree | `Backend/frontend/src/main.jsx`, `src/App.jsx` |
| Shared nav, visual system and locales | `src/components/Navbar.jsx`, `src/index.css`, `src/context/LanguageContext.jsx`, `src/locales/` |
| Capture and validated file submission | `src/pages/Detect.jsx`, `src/lib/api.js` |
| Results and installed Windows VR | `src/pages/Result.jsx`, `src/components/VrLaunchCard.jsx`, `src/lib/api.js` |
| Field list, scene and conditions | `src/pages/Field.jsx`, `src/field/FieldScene.jsx`, `src/field/cycleMachine.js`, current video and XR helpers |
| Quest standalone demo | `src/pages/QuestDemo.jsx`, `src/lib/questDemo.js` |
| About and 404 | `src/pages/About.jsx`, `src/pages/NotFound.jsx` |
| Bundle and browser checks | `scripts/check-budget.mjs`, `playwright.config.js`, `e2e/happy-path.spec.js` |

Reuse the Vite, React Router, language provider, browser session, and existing frontend test setup. Keep route/UI changes local to `Backend/frontend`. Let shared layout and controls reuse the same semantic primitives where their actual behavior matches. Preserve endpoint requests and environment variables. Do not make backend, model, Unity or deployment changes to approximate a design state.

## Data and navigation invariants

1. **Home and return navigation.** Route primary and secondary actions to the live app. Preserve the current location and query semantics. Let desktop and mobile navigation share one route-activity source. Keep natural history behavior when leaving Home and using browser Back, particularly after entering the Field.
2. **Upload.** Keep the file-input, drag/drop, size and image-validation path. The current frontend cap is 10 MB. Prefer accurate localized UX copy consistent with formats and limits actually accepted. Preserve correct crop and locale in the scan request, browser-session ownership, refresh recovery, and the existing duplicate-submit prevention. A submission is one user-triggered upload. Use existing state guards when preview decode or submissions complete after another action.
3. **Result.** Preserve `/result?scan=…`, cached navigation state, and API-backed deep-link fetch. Keep diagnosed, healthy, uncertain, not-a-leaf, wrong-crop, model-unavailable, missing and connection states distinguishable. Keep the original photo and optional model attention independent. A confidence bar represents confidence for the given scan where available. A severity indicator represents supplied disease info. Use existing recovery links and translations.
4. **Field and handoff.** Preserve `/field?scan=…`, `/field?disease=…`, selected case back navigation and the list view. Bind filters, case choices and footage to the current API. Apply `canEnterField`, `canEnterVr` and their actual reason text independently. Keep pairing, launch status, timeout and recorded treatment behavior. Browser Field and installed VR use separate names or context, even where they share an entry section.
5. **Cycle.** Preserve case-specific ranges, values, conditions, reset, stage order, completed/stopped/not reached distinctions, existing playback and the video projection rules. Feed the DOM and renderer from the same data. WebGL, HLS or immersive capability failure must leave the ordinary nav, case context, and any loaded cycle data accessible.
6. **Quest.** Preserve `/quest-demo`, the existing Android intent and installed link, browser detection and optional download. Preserve HTTPS validation for Send page to Meta Quest. A visited page or applied scroll animation must never trigger a device handoff.
7. **About and 404.** Bind About to its model metadata endpoint and show the returned version, current dataset splits and measured values. Maintain localized loading and unavailable states. Render a route failure with actual recovery links and escaped path context.
8. **Localizations.** Route new visible copy and ARIA labels through the localization layer. Maintain parity in English, Telugu, Hindi, Tamil and Kannada. Test selection persistence, differing text widths, Indic line heights and the relevant API locale. Status, media and captions must remain comprehensible in the selected language.

## Visual proof

Use the ten reference frames named in the [page specification](page-specifications.md). Load the reference PNG itself, then compare with the same viewport or device-independent CSS width. Capture after critical assets are ready, with the filename and underlying route/state recorded. Preserve a 1586 × 992 capture for each numbered frame when the route can be exercised. Standardize how far the Home walkthrough is scrolled so “How it works” starts below the pinned header. Use separate, clearly identified captures for skeletons and reduced motion.

| Check | Evidence to inspect |
| --- | --- |
| Shell alignment | Header height, baseline, logo, active underline, side gutters, shared input and action sizes |
| Hero reading | Headline starts near target x/y and scale, actions appear before fold, greenhouse anchor stays on its roof |
| Art continuity | Atlas greenhouse and preview correspond, route goes from map to aisle, crop cutout crops stay recognizable |
| Paired evidence | Original and model attention align, correct labels, matching crop and photo ratio, no overlay from another result |
| Operational Field | View and control rail match intended balance, actual inputs are readable, timeline shows actual semantics, VR and picking a case remain reachable |
| Narrow layout | Heading and next step appear early, panels recompose, no horizontal document overflow, focus stays in view |

At a matched desktop viewport, use roughly 8 px tolerance for the shared header and small layout anchors, and 16–24 px for hero and content alignment when fonts and content have equivalent geometry. Prioritize the same hierarchy, composition and functional access where real strings or assets require differences. Compare each screen visually, especially critical actions and first-fold cropping. The generated references use illustrative photos and small typography variations. Match them with one shared UI system and actual web content.

Check 1440 × 900, 1024 × 768, 768 × 1024 and 390 × 844. Verify no overflow at 320 CSS px and at high zoom. Run through keyboard focus order, Enter, Space, Escape, visible focus inside panels, associated form labels, language changes, and hover-independent touch interactions. Verify range sliders and motion controls with keyboard. Announce status transitions without announcing every animation frame. Check the color contrast of text on the rendered background and of important visual boundaries. Check reduced motion with a still start-to-finish route.

## Accessibility verification

Verify the [accessibility contract](design-system.md#accessibility-contract) on each route that changes.

| Check | Evidence to inspect |
| --- | --- |
| Automated scan | An axe run in Playwright on Home, Detect (empty, selected, error), Result (diagnosed, uncertain), Field (list, active), Quest demo, About and 404 reports no serious or critical violations |
| Lint | `eslint-plugin-jsx-a11y` recommended rules pass with no new disables |
| Route change | Document title updates, focus lands on the new `h1`, the skip link reaches `main`, Back restores the previous position |
| Language | `<html lang>` matches each of the five locales after switching, and after a reload |
| Contrast | Control edges and the focus ring reach 3:1 on canvas, surface, selection, teal and footage. No terracotta text below 24 px |
| Forms | The file input and crop select are reachable by label. A rejected file and a server error are announced once and are tied to their control. Focus stays on the submit button while analysing |
| Evidence | The attention pane has its text equivalent. Decorative art is silent in the accessibility tree |
| Field | Each range announces value and unit. A stage change is announced once. The scene has a name or is hidden with its description exposed. All controls work with the renderer blocked |
| Screen reader | One pass of scan to result to Field with NVDA on Windows, and one on a phone with TalkBack or VoiceOver |
| Display modes | Windows forced colors keeps focus, selection and control edges visible. 200% zoom and 320 CSS px show no clipped controls |

If `@axe-core/playwright` and `eslint-plugin-jsx-a11y` are not already installed, add them as dev dependencies only, so the 180 KB entry budget is unaffected. Record screen reader and forced-colors passes separately from automated proof, and say which were not run.

## Flow and failure verification

- From Home, enter Detect, choose a valid file, optionally select a crop, analyse, and return to the original Result by refreshing and via Back. Exercise invalid file, remove, failure/retry and busy state. Exercise a healthy result, confirmed disease, wrong crop and a not-a-leaf or uncertain result.
- Compare real evidence and treatment disclosure with and without model attention. Confirm the two eligible Field paths have separate outcomes. Verify backed eligibility states and use a headset only when it is available to test.
- Open Field with no selection, direct `?disease` and scan-linked `?scan`. Change one condition, reset, switch footage when available, and confirm timeline semantics match the current engine. Verify poster/fallback behavior with WebGL blocked, slow or no footage and unavailable renderer.
- View the Meta Quest demo in conventional browser and, when available, in the headset browser. Check that the send or download action appears only under its existing conditions and that the installed app link is reachable. Record hardware-dependent behavior separately from actual browser proof.
- Open About with success, pending and unavailable data. Open an unknown URL. Check every core route also works as a direct visit.
- Verify initial render under slow media, back/forward, a resize while the map is selected, reduced motion, and a repeated route transition. If using browser fixtures to reach visual states, use isolated development or Playwright fixtures and identify those screenshots. Keep fixtures out of runtime bundles.

## Repository checks

From `Backend`, use the workspace's existing package manager and scripts.

```powershell
pnpm --filter crop-disease-detector lint
pnpm --filter crop-disease-detector test
pnpm --filter crop-disease-detector build
pnpm --filter crop-disease-detector e2e
```

The production build calls the budget script. Preserve the 180 KB gzip entry limit and keep Three.js out of the entry and initial preloads. Use the existing configured stack and test fixtures for Playwright. Read the test setup for required API or model inputs. Record real passes, conditional skips and unmet dependencies separately. The source design includes motion and fallback targets, while final verification requires actual implementation and browser captures.
