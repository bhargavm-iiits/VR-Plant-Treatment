# CropScan XR: Unity client contract (proposed, confirm with backend)

Unity implements the client side of guide sections 2–6 against the **proposed** routes below. Every path lives in
`Assets/_Project/Scripts/Runtime/XR/XrApiRoutes.cs` and every payload in `XrModels.cs`. If the backend picks other
names, change them there. JSON is camelCase because Unity reads it with `JsonUtility`. The one exception is the
published `ScanOut`, whose fields Unity reads as they are.

## Enter the field (same laptop)

```
result page ──cropscanxr://open-field?scan=<uuid>──▶ CropScanLauncher.exe ──┬─ player running:  pipe "OPEN <uuid>"
                                                                              └─ player closed:  PlantDiseaseVR.exe --crop-scan-id <uuid> -single-instance
PlantDiseaseVR ──HTTPS (device credential)──▶ POST /api/xr/sessions {scanId} ──▶ session ──▶ Farm specimen
```

**Frontend.** In the frontend repo, change the **Enter the field** CTA on `/result?scan=…`. Keep **Field** in the navbar for the browser viewer.

```jsx
<a href={`cropscanxr://open-field?scan=${encodeURIComponent(scan.id)}`}>Enter the field</a>
```

Render the link only for a supported scan (the VR eligibility in §2). Add an **Install CropScan XR for Windows** hint beside it.

**Launcher** (`Tools/CropScanLauncher`, .NET Framework 4.8, so nothing needs installing on Windows 10/11):

* It accepts exactly one `cropscanxr://open-field?scan=<uuid>` and refuses any other host, path, query key, repeated key, user info, port, or fragment. The value must be a UUID, which it lower-cases.
* It talks to the running player over `\\.\pipe\CropScanXR.OpenField.<hash of DOMAIN\user>`. The protocol is one line per connection, `PING` or `OPEN <uuid>`, and the answer is `OK` or `ERR <reason>`.
* A per-user mutex serialises clicks. It is held until a newly started player answers `PING`, so a second click while the player is starting is forwarded to it instead of starting another player.
* It starts `PlantDiseaseVR.exe` from its own folder by direct process execution. The validated UUID is the only variable argument.
* It logs to `%LOCALAPPDATA%\CropScanXR\launcher.log`. Setting `CROPSCANXR_LAUNCHER_QUIET=1` logs instead of showing dialogs, for scripted tests.
* The link validation lives in `XrScanLink.cs`, which the launcher compiles from the player's own source.

**Install** (current user, no admin rights):

```powershell
dotnet build Tools\CropScanLauncher -c Release
.\Tools\CropScanLauncher\Install-CropScanXR.ps1 -PlayerBuild <Windows x64 build folder> -ApiBaseUrl https://<api> -SessionMode paired   # or preview
# undo: .\Tools\CropScanLauncher\Uninstall-CropScanXR.ps1 [-RemoveFiles]
```

The installer copies the build and the launcher to `%LOCALAPPDATA%\Programs\CropScanXR`. It writes
`CropScanXR.config.json` (`apiBaseUrl`, `sessionMode`) next to the player, because the link carries only the scan ID.
It also registers `HKCU\Software\Classes\cropscanxr` → `"…\CropScanLauncher.exe" "%1"`.

**Session modes** (`sessionMode` in the config):

| Mode | Scan → session | Results |
| --- | --- | --- |
| `paired` (default) | `POST /api/xr/sessions {scanId}` with the device credential. The backend checks access, and an unpaired or foreign device gets an access error. A scan clicked before pairing waits and opens after pairing. | Backend |
| `preview` | The published `GET /api/scans/{id}` plus the case map. This is the staging proof of concept (guide §5.3.2) and works today. It is labelled **STAGING PREVIEW** in the headset. | This PC only (`xr_preview_store.json`) |

Repeated clicks on a scan that is already opening are coalesced. If XR isn't running yet (Quest Link off), the Farm
opens in the desktop view and the launch status stays `opening` with "Waiting for Quest Link". The player retries
OpenXR every 15 s (or on **Retry headset**), refocuses the specimen, and only then reports `opened`.

## Shared data

| File | Owner | Purpose |
| --- | --- | --- |
| `Assets/_Project/Resources/XR/xr_case_map.json` | Shared (backend keeps a copy) | Exact backend `disease_id` → Unity `plantId`, `diseaseId`, `specimenId`, `recipeId`. `mapVersion` 1 has three rows: Tomato and Potato Early Blight, Bell Pepper Bacterial Spot. |
| `Assets/_Project/Resources/XR/xr_specimens.json` | Unity | Farm specimen → row plant and authored standing distance. |
| `Assets/_Project/Resources/XR/Fixtures/xr-test-001.json` | Shared | The agreed first session fixture (guide §3.3). |
| `Docs/xr_recipe_seed.json` | Generated | **BTP → XR → Export Recipe Seed for Backend** writes the recipe for every mapped case from `treatment_content.sql`. |

VR eligibility is true only for a scan whose `disease_id` is a row in the map. Healthy, uncertain, and unmapped
results stay on the web with a reason. Unity runs the same check again (`XrSessionValidator`) and refuses anything that
disagrees.

## Device auth

`POST /api/xr/pairings/redeem` with `{code, deviceName, platform:"windows-openxr", appVersion}`. The response is
`{deviceId, deviceToken, apiBaseUrl, expiresUtc}`. Every later request sends `Authorization: Bearer <deviceToken>`, and
so does the socket upgrade. A `401` means "pair again" and a `429` means "too many code attempts". The pairing window
appears on the PC monitor (F8).

## HTTPS

| Route | Unity sends | Unity expects |
| --- | --- | --- |
| `POST /api/xr/sessions` | `{scanId}`, `Idempotency-Key: session:<scanId>` | `200/201` with the session (fixture shape) for this learner and scan, created or reopened. `403/404` means an access error, and `409/422` carries `{"detail": "<reason>"}` for scans that cannot open in VR. |
| `POST /api/xr/sessions/{id}/launches` | `{scanId, source:"app-link"}`, `Idempotency-Key: app-link:<scanId>:<activation>` | `{launchId, xrSessionId, status}` |
| `POST /api/xr/launches/{launchId}/status` | `{status:"opening"|"opened"|"failed", detail, xrReady}` | 2xx. The result page reads it through `GET /api/xr/launches/{launchId}`. |
| `GET /api/xr/sessions/{id}` | – | `xrSessionId, scanId, backendDiseaseId, plantId, diseaseId, specimenId, recipeId, recipeVersion, revision, treatmentResult` |
| `GET /api/xr/sessions/{id}/recipe` | – | `XrRecipe` (same shape as the seed): `recipeId, recipeVersion, plantId, diseaseId, contentVersion, initialSeverity, initialHealth, maxSeverityDrop, maxHealthGain, maxWorsen, controls[], rules[], assetKeys[]` |
| `POST /api/xr/sessions/{id}/treatment-result` | `Idempotency-Key: xr:{xrSessionId}:m{localSessionId}:r{expectedRevision}`, body `XrTreatmentSubmission` | `200/201` with the canonical session (new `revision`, `treatmentResult`). On `409` the body is the current session (or Unity reloads it). `400/403/404/422` means refused. |
| `GET /api/xr/launches/pending` | – | `{"items":[…]}`. Only used by the optional remote launch flow over the socket. |

Server rules the Unity local store already enforces, and the backend must enforce too:

* A repeated `Idempotency-Key` returns the original write and never writes again.
* `recipeId`/`recipeVersion` must equal the version pinned on the session.
* `expectedRevision` must equal the current revision, otherwise the response is `409`.
* Values must lie in 0..1, and `outcome` must be one of `Managed, PartlyManaged, PestSuppressed, PartlySuppressed, Contained, PoorlyContained, NotContained, NotControlled, NotTreated`.

## Socket `WSS /ws/xr` (status channel)

* Unity → server: `{"type":"device.hello"|"device.heartbeat"|"pong","deviceId","xrReady","display","appVersion"}`, with a heartbeat every 15 s.
* Server → Unity: `{"type":"ping"}`, plus `{"type":"vr.launch","launchId","xrSessionId"}` for a later remote-launch flow (deduplicated by `launchId`). The same-laptop button never needs it.
* Reconnect backoff runs from 1 s up to 30 s with ±20% jitter.

## Running the player directly

| Command line | Mode |
| --- | --- |
| *(none, no config)* | The offline demo, unchanged. A scan that arrives shows a setup message. |
| `--crop-scan-id <uuid>` | Opens that scan using the installed config. This is what the launcher passes. |
| `-xr-fixture default` | **FIXTURE MODE** (labelled). Opens `xr-test-001`. F9 replays the last launch, which must not move the user, and F10 sends a new one. Add `-xr-fixture-drop-response` (the first save's response is lost, so Unity retries with the same key) or `-xr-fixture-reset`. |
| `-xr-api https://<api>` | Overrides the config's API address and uses paired mode. |

In the Editor, **BTP → XR → Fixture Mode (Play)** turns fixture mode on for Play without any flags.
