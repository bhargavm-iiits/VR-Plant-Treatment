# CropScan XR implementation guide

Use the existing **Enter the field** button on `/result?scan=…` to start or activate the installed Windows Unity app on the laptop. Pass the current scan ID so the app opens at the corresponding Farm plant. Start with `Tomato___Early_blight` and a Quest connected through Quest Link.

**VR:** Upload → Diagnosis → **Enter the field** → Windows Unity app → Farm → Lab → Treatment → **Update Treatment in Lab** → Lab → **Return to Farm**  
**AR:** Diagnosis → **AR View** → Phone notification or QR → Same diagnosis in the AR app

[Go to the result button and Windows setup](#5-open-the-installed-windows-app-from-enter-the-field).

## 1. Set up a staging workspace

1. **Unity owner:** Open this project in Unity `6000.5.5f1`. Confirm the Windows x64 build starts at `XRBootstrap` and includes `Envirornment` (the Farm scene, with this exact spelling), `Lab`, and `Treatment`. Run the existing Farm → Lab → Treatment flow once. [Unity version](../ProjectSettings/ProjectVersion.txt) · [Scene list](../ProjectSettings/EditorBuildSettings.asset)
2. **Web and backend owners:** Share the frontend and API source repos, agree on staging URLs, and identify where the backend stores scan ownership, diagnosis records, and treatment data. Use the deployed sites to confirm the existing flow. [CropScan Detect](https://crop-scan-xr-frontend.vercel.app/detect) · [CropScan API](https://cropscan-api-ovmb.onrender.com/) · [API schema](https://cropscan-api-ovmb.onrender.com/openapi.json)
3. **AR owner:** Confirm the target phone platform, how the app pairs with a web session, and which verified link it can handle. Test the web and Unity path with a fixture while teammate repos or staging services are being set up. Label the fixture mode visibly.

The published API already provides these routes. Confirm the current status values and ownership behavior in staging before wiring eligibility.

| Existing route | Use |
| --- | --- |
| `GET /healthz` | Check whether the API is awake and responding. |
| `POST /api/scans` | Upload one image using the multipart field `file`. A successful response is `201` with a scan record, including `id`, `status`, `disease_id`, `confidence`, and `model_version`. The classification fields can be null. |
| `GET /api/scans/{scan_id}` | Reopen the saved scan result. |
| `GET /api/diseases` | Get exact backend class IDs and healthy flags. |
| `GET /api/diseases/{disease_id}` | Get disease details and available advice. The advice fields are text. |

[Published API schema](https://cropscan-api-ovmb.onrender.com/openapi.json) · [Live disease catalog](https://cropscan-api-ovmb.onrender.com/api/diseases)

## 2. Map backend diagnoses to Unity cases

1. Create a shared, versioned mapping keyed by the exact backend `disease_id`. Each row supplies the Unity `plantId`, Unity `diseaseId`, and a supported Farm target and recipe. Start with the three examples below. Use the live catalog and Unity’s `DiseaseClasses` when adding rows. The published catalog listed 68 classes during inspection. Unity has 24 disease cases across nine crops. [Unity disease classes](../Assets/_Project/Scripts/Runtime/Plants/DiseaseClasses.cs) · [Live disease catalog](https://cropscan-api-ovmb.onrender.com/api/diseases)
2. Return a Unity VR eligibility value and reason from the backend. Require a scan accepted for XR, an exact case mapping, a resolvable Farm target, and a recipe the build supports. Keep `can_enter_field` scoped to the existing browser Field viewer. A supported scan can offer **Enter the field** while the Windows player is closed. For healthy, uncertain, and unsupported results, show the diagnosis and a clear reason why the treatment flow is unavailable. Evaluate AR availability independently.
3. Keep the identities separate. `scanId` is the backend scan `id`. `xrSessionId` identifies the learner’s run for that scan. `specimenId` identifies the virtual plant to focus and update. `(plantId, diseaseId)` identifies reusable Unity teaching content. Reopen the same session for the same learner and scan. A new scan gets its own session and progress even when its diagnosis matches.

| Backend class | Unity plant | Unity disease |
| --- | --- | --- |
| `Tomato___Early_blight` | `tomato` | `early_blight` |
| `Potato___Early_blight` | `potato` | `early_blight` |
| `Pepper,_bell___Bacterial_spot` | `bell_pepper` | `bacterial_spot` |

**Check:** Tomato and Potato Early Blight resolve to different Unity cases. An unknown backend class stays visible on the web with a reason instead of entering Unity’s default case.

## 3. Agree on and implement the XR API

The routes below are **proposed additions**. Confirm the names and payloads with the backend owner before hardcoding clients. Use the backend database for session, launch, and confirmed treatment state.

| Proposed operation | Behavior |
| --- | --- |
| `POST /api/xr/pairings` and `POST /api/xr/pairings/redeem` | The website issues a short-lived, one-time code. The user enters it in the Unity desktop player. Redemption binds that player to the same web session and issues a scoped device credential. Rate-limit code attempts. |
| `GET /api/xr/devices` | List the caller’s paired devices and their current socket and headset readiness. Mark readiness stale when the heartbeat expires. |
| `POST /api/xr/sessions` | Take the selected `scanId` from the web or from the paired Unity player after it receives the app link. Verify access and create or reopen the session. Resolve the Unity case and specimen when available. An AR-only session can carry the diagnosis with empty Unity fields. |
| `GET /api/xr/sessions/{xrSessionId}` | Return the resolved case, specimen, pinned recipe version, confirmed result and revision, and the latest launch status when the result page displays it. |
| `POST /api/xr/sessions/{xrSessionId}/launches` | After accepting an app link, Unity can create a launch record for web status. Return its `launchId` and deduplicate retries with an idempotency key. |
| `WSS /ws/xr` | Once Unity is running, use the socket for device readiness and scoped launch or treatment status updates. Use HTTPS for session and recipe data. |
| `GET /api/xr/launches/{launchId}` and `POST /api/xr/launches/{launchId}/status` | If the result page displays live status, let it read the authorized session’s latest launch and let Unity report `opening`, `opened`, or `failed`. |
| `GET /api/xr/sessions/{xrSessionId}/recipe` | Return the session’s pinned, structured treatment recipe. |
| `POST /api/xr/sessions/{xrSessionId}/treatment-result` | Validate and save the assessed outcome. Return the canonical result and revision. |
| `POST /api/xr/sessions/{xrSessionId}/ar-handoff` | Issue an expiring phone handoff reference and request push for the paired phone when available. |

1. Bind scans and XR sessions to a signed-in user or a server-issued browser session. Verify scan ownership before creating XR sessions and verify device authorization on each launch, lookup, and write. Configure HTTPS and WSS and allow the intended web origins in API CORS.
2. For the same-laptop button, Windows delivers the scan to the player through the app link. Use the WebSocket to keep the browser informed once the player starts. Keep the durable status in the API so a reconnect can recover it. If a separate remote launch flow is added later, deliver its event only to the paired device and deduplicate by `launchId`. [WebSockets on Render](https://render.com/docs/websocket)
3. Agree on the first session fixture and use it on both sides. The following values are illustrative. `specimenId` must resolve to an authored target in the Unity build.

```json
{
  "xrSessionId": "xr-test-001",
  "scanId": "scan-test-001",
  "backendDiseaseId": "Tomato___Early_blight",
  "plantId": "tomato",
  "diseaseId": "early_blight",
  "specimenId": "farm-tomato-early-blight-01",
  "recipeId": "tomato-early-blight",
  "recipeVersion": 1,
  "revision": 0,
  "treatmentResult": null
}
```

**Check:** The paired player can open a scan owned by its web session. An unpaired player or one paired to a different web session receives an access error.

## 4. Make Unity open the correct Farm plant from the fixture

1. Put the active session and launch coordinator in `XRBootstrap`. Reuse its XR Origin, input setup, and `EnvironmentController`. Keep Unity API calls on the main thread.
2. On the first Farm disease row, assign a stable `specimenId` and an authored standing anchor facing a selected plant. Place the anchor on clear ground outside its colliders. Connect it to the existing `SelectablePlant` row so the highlight and label follow the specimen. [Farm selection](../Assets/_Project/Scripts/Runtime/Farm/SelectablePlant.cs)
3. Resolve and validate the session, then call `SelectionState.SelectCase(plantId, diseaseId)`. Apply the complete case before loading the scene views. If another case is active, preserve its pending result and let the user confirm before switching. [Shared plant selection](../Assets/_Project/Scripts/Runtime/Core/SelectionState.cs)
4. Use the existing additive transition when entering Farm from another scene. Add a faded focus action for an already-loaded Farm, because `SwitchEnvironment` returns early when the target is the current scene. Place the XR Origin at the anchor while faded, then show the selected specimen and a clear **Enter Lab** action. Report success after the focus and transition finish. [Environment transitions](../Assets/_Project/Scripts/Runtime/Core/EnvironmentController.cs)
5. In Lab and Treatment, read the same active session on entry. Lab uses the existing leaf view for the chosen case. Keep the connected case bound while the user is treating it. Use the existing manual case selection in offline mode. [Lab plant display](../Assets/_Project/Scripts/Runtime/Lab/LabPlantDisplay.cs)

**Check:** Loading the fixture from both an open Farm and an open Lab leads to the same Farm plant. Entering Lab and Treatment shows Tomato Early Blight in both scenes.

## 5. Open the installed Windows app from Enter the field

### 5.1 Change the existing button on the result page

In the frontend repo, find **Enter the field** on `/result?scan=…`. It currently takes the user to `/field?scan=…`. Change this CTA to a native link whose target comes from the **loaded scan record**. Keep **Field** in the navbar for the browser viewer and place **AR View** beside the result CTA when the AR handoff is ready. [Existing browser Field viewer](https://crop-scan-xr-frontend.vercel.app/field)

```jsx
<a href={`cropscanxr://open-field?scan=${encodeURIComponent(scan.id)}`}>
  Enter the field
</a>
```

Render the link when a supported scan is loaded. Trigger it directly from the click so the browser can ask the user to open CropScan XR. Put a short **Install CropScan XR for Windows** hint near the CTA for first-time users. The app link targets the computer running the browser.

### 5.2 Register the Windows link once on the laptop

1. Target the Windows x64 OpenXR build of `PlantDiseaseVR.exe`. Ship a small `CropScanLauncher.exe` next to the player. Use the installer to associate `cropscanxr:` with the launcher for the current Windows user. The registration below illustrates the values for an example install path. Use the actual path selected by the installer. [Win32 protocol activation](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/cross-device-resume) · [Per-user protocol registration example](https://learn.microsoft.com/en-us/microsoft-scout/managed-troubleshoot)

```reg
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\cropscanxr]
@="URL:CropScan XR"
"URL Protocol"=""

[HKEY_CURRENT_USER\Software\Classes\cropscanxr\shell\open\command]
@="\"C:\\Apps\\CropScanXR\\CropScanLauncher.exe\" \"%1\""
```

2. In the launcher, accept one `cropscanxr://open-field` URI with one `scan` value. Validate the value as a UUID. Start the player from its fixed install path with `--crop-scan-id <uuid>`, passing the flag and ID as separate process arguments. Use direct process execution. The URL supplies only the scan ID.
3. If the player is already open, forward the validated ID to the existing player over a local named pipe scoped to the same Windows user. Serialize activation requests while the player starts, and wait for the receiver before sending subsequent clicks. Unity’s `-single-instance` can focus a window. Use the pipe to deliver the new scan ID to that window. [Unity Windows player arguments](https://docs.unity.com/en-us/engine/6000.5/manual/unity-editor/command-line-arguments/player)

### 5.3 Load the scan when Unity receives it

1. In `XRBootstrap`, read `--crop-scan-id` from `System.Environment.GetCommandLineArgs()` at startup. Send IDs from both startup and the local receiver into the same case-opening method on Unity’s main thread. Rebuild the Windows player and test the new flag with a staging scan from PowerShell before wiring the browser.

```powershell
& 'C:\Apps\CropScanXR\PlantDiseaseVR.exe' '--crop-scan-id' '<staging-scan-uuid>'
```
2. For a staging proof of concept, use a test scan from `GET /api/scans/{scan_id}` and the mapping in step 2. For the integrated flow, have the paired Unity player create or reopen the session over HTTPS so the backend verifies that the device can access the scan. A scan UUID identifies a record. The paired device credential authorizes access.
3. Call the Farm focus flow from step 4. Coalesce repeated clicks for a scan while it is opening. If the headset is already in Quest Link, verify the Farm appears there with head tracking and controller input. If the app starts before Link is active, show **Activate Quest Link** and keep the case available. Publish **Opened in headset** only after XR and the Farm target are ready. [Meta Quest Link setup](https://developers.meta.com/vr/documentation/unity/unity-deploy-and-test/)

**Check:** Close Unity. Click **Enter the field** on a supported result in Edge and accept the app prompt if shown. The installed player opens directly at the correct Farm specimen. Then click from a different supported result with Unity already running and verify the existing window loads that scan.

## 6. Load the recipe and complete treatment

1. **Backend:** Store versioned simulation recipes in the database. Seed the first one from Unity’s `treatment_content.sql`, including its case profile, control definitions, validation rules, and Unity asset keys. Keep the published disease advice available for the web result. Pin a recipe version when creating the session so an edit to the catalog cannot change a lesson in progress. [Existing treatment data](../Assets/_Project/Data/Treatment/treatment_content.sql)
2. **Unity:** Fetch the pinned recipe before enabling **Enter Treatment**. Validate its case, version, control ranges, rules, and references against the local catalog. Adapt it through `TreatmentServices` for `TreatmentController` and `ManualTreatmentController`. Keep SQLite as the source for the offline demo. Store connected drafts and progress by `xrSessionId` so two scans of one disease cannot share a saved assessment. [Treatment services](../Assets/_Project/Scripts/Runtime/Treatment/TreatmentServices.cs) · [Recipe validation](../Assets/_Project/Scripts/Runtime/Treatment/TreatmentCaseValidator.cs)
3. After the learner applies treatment and finishes the final assessment, show **Update Treatment in Lab** in the existing manual results panel. On press, submit the assessment for the active session with `recipeVersion`, `expectedRevision`, and an `Idempotency-Key` header. The server validates the submission against the pinned recipe, writes it once, and returns the saved outcome and new revision. If the response is lost, retry the same key. If there is a revision conflict, reload the saved result before allowing another save. [Manual results view](../Assets/_Project/Scripts/Runtime/Treatment/Manual/ManualTreatmentView.cs) · [Treatment controller](../Assets/_Project/Scripts/Runtime/Treatment/Manual/ManualTreatmentController.cs)
4. While saving, show **Saving treatment**. On failure, retain the assessment and offer **Retry**. After confirmation, switch to Lab and display the saved assessment. Distinguish simulated improvement, partial improvement, and no improvement using the outcome the recipe produces.
5. On **Return to Farm**, reuse the specimen focus and apply the confirmed result to that specimen’s visuals. Rebind from session state when entering the scene and after restarting the player. The existing `FarmRecoveryState` tracks demo weeks by crop, so give the connected specimen its own visual state. Use a per-renderer override or per-instance material so the treatment stays scoped to that plant. [Farm demo recovery state](../Assets/_Project/Scripts/Runtime/Farm/FarmRecoveryState.cs)

**Check:** After a successful save, Lab and Farm show the same confirmed result. Lose the response after a save, retry, and verify the backend has one result. Reopen the session after restarting Unity and verify the result and specimen are restored.

## 7. Connect the AR handoff

1. Pair the phone with the same web user or browser session. Agree with the AR teammate on the registered app link, the push payload, and how the app acknowledges opening the case.
2. On **AR View**, create a handoff for the selected scan and its session. The backend sends a notification to the paired phone with an opaque, expiring reference. The AR app opens the diagnosis when the user taps the notification and resolves the reference over HTTPS. Share the same specimen ID where both clients support it.
3. Show a QR link on the laptop when push is unavailable or the phone is unpaired. Let the phone open the installed app through the verified link, or show setup instructions on the web fallback. Show **Opened in AR** only after the app confirms it opened the case. [Android App Links](https://developer.android.com/training/app-links/about) · [Android notification launch behavior](https://developer.android.com/guide/components/activities/secure-bal) · [Apple Universal Links](https://developer.apple.com/documentation/xcode/supporting-universal-links-in-your-app)

**Check:** Tap the phone notification and scan the fallback QR in separate runs. Both should open the chosen scan on the paired phone.

## 8. Run the staging demo and record the results

1. Install the Windows build and register the app link on the laptop. Pair the player with the staging site once, close the player, and start Quest Link.
2. Upload an approved test image and confirm the expected scan result. Click **Enter the field** on the results page and accept the browser’s app prompt if shown. Check that Unity starts and shows the highlighted specimen in the headset, then go to Lab and Treatment.
3. Complete the assessment, click **Update Treatment in Lab**, verify the saved result, and select **Return to Farm**. Verify the same plant shows the simulated outcome. Restart Unity and reopen the same scan to check restoration.
4. With Unity still running, try a second scan with the same diagnosis, then Potato Early Blight and an unsupported result. Check that the existing Unity instance receives the new scan and that each scan keeps its own progress.
5. Try the button without Quest Link, interrupt the status socket, and interrupt a result save. Check the headset setup message, status recovery, and that a retried save creates one outcome.
6. Verify the AR notification and QR flow with the teammate’s app. Run the offline Unity demo once more. Record the Unity build, backend revision, test scan IDs, and which tests passed on physical hardware.

| Symptom | First check |
| --- | --- |
| The web page stays on analysis | Check `/healthz` and show a startup message during a Render cold start. [Render service behavior](https://render.com/docs/faq) |
| The button still opens the browser Field page | Check that the deployed result button points to `cropscanxr:` and uses the loaded scan’s ID. |
| Edge cannot open CropScan XR | Check the per-user protocol registration and the installed launcher path. |
| The app opens on the laptop but the headset shows no Farm | Check the active Quest Link session, Windows OpenXR runtime, and Unity XR display state. |
| Unity loads the wrong plant or stays at the Farm entrance | Check the exact backend class mapping, session `specimenId`, and authored Farm anchor. |
| Treatment runs but the Farm plant looks unchanged | Check the confirmed result revision, the active session and specimen, and the per-specimen renderer update. |

Once the first case passes, expand the mapping one crop at a time. For the demo, keep a small case badge in Lab and add a before-and-after view driven by the saved assessment.

