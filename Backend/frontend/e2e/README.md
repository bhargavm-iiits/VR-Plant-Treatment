# CropScan XR browser tests

Run these commands from the Unity project root (`BTP_Project`). The `--prefix` option tells npm where to find the website's `package.json`. Playwright starts Vite on loopback port 5299 and uses local fixtures for diagnoses, disease notes and error responses.

```powershell
$env:E2E_BROWSER_CHANNEL = 'msedge'
npm.cmd --prefix .\Backend\frontend run e2e
```

To use Playwright's Chromium instead of installed Microsoft Edge, clear `E2E_BROWSER_CHANNEL` and run `npm.cmd --prefix .\Backend\frontend exec -- playwright install chromium` once. To run one workflow, use one of these commands.

```powershell
npm.cmd --prefix .\Backend\frontend run e2e -- --project=chromium landing.spec.js
npm.cmd --prefix .\Backend\frontend run e2e -- --project=chromium scan.spec.js
npm.cmd --prefix .\Backend\frontend run e2e -- --project=chromium field.spec.js
npm.cmd --prefix .\Backend\frontend run e2e -- --project=chromium navigation.spec.js
npm.cmd --prefix .\Backend\frontend run e2e -- --project=mobile
```

For browser-to-API integration, install the FastAPI service requirements into `Backend/services/api/.venv` or set `E2E_PYTHON` to the Python executable that has those dependencies. The server uses a fresh SQLite database and temporary photo directory on loopback port 8299. For real inference, put the exported model and its metadata in `Backend/services/api/model`. The integration fixture is the existing `public/leaves/apple_apple_scab.webp` specimen. It exercises the named diagnosis, stored photo, attention image, browser Field handoff, and scan ledger entry.

```powershell
$env:E2E_INTEGRATION = '1'
npm.cmd --prefix .\Backend\frontend run e2e
npm.cmd --prefix .\Backend\frontend run e2e -- --project=local-api
npm.cmd --prefix .\Backend\frontend test
npm.cmd --prefix .\Backend\frontend run build
```

Run the existing non-VR API tests from `Backend/services/api`.

```powershell
$env:XR_LOCAL_LAUNCH = 'false'
$env:LEDGER_ANCHOR_ENABLED = 'false'
.venv/Scripts/python.exe -m pytest -q --ignore=tests/test_xr.py -k 'not test_demo_ and not test_the_unity_app_logs'
```

The HTML report is written to `Backend/.local/playwright-report/index.html`. It includes snapshots of the settled hero, doorway crossing, leaf close-up and scan view. Failure screenshots and traces are under `Backend/.local/playwright-results`.
