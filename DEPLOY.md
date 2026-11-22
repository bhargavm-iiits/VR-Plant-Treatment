# Deploying CropScan (free tier)

The frontend goes on **Vercel**. **Render** hosts the API and the PostgreSQL
database, including scan photos and heatmaps.

## 1. API and database on Render

1. Render → **New** → **Blueprint** → choose the `CropScan-XR` repo.
   `render.yaml` creates the API and a free Postgres database and generates
   the scan-image signing key.
2. After deployment, open `https://<your-api>.onrender.com/healthz` and check
   that the model, database and storage are all ready.

The API runs database migrations at startup. Database-backed photos use signed,
expiring links. For S3-compatible scan storage, set `SCAN_MEDIA_BACKEND=s3`
and configure the endpoint, bucket, region, and credentials on the service.

## 2. Website on Vercel

1. Vercel → your project → **Settings → Git** → connect `CropScan-XR`
   (or **Add New → Project** to make a fresh one).
2. **Root Directory**: `frontend`. Framework: **Vite**.
3. **Environment Variables**: `VITE_API_URL` = `https://<your-api>.onrender.com`
   (no trailing slash).
4. **Redeploy**. The variable is baked in at build time, so changing it later
   needs another redeploy.

### Native Quest demo

- In the Unity project, use **BTP → Build → Quest Demo APK**. The build writes `Builds/Quest/PlantDiseaseVR.apk` and starts the game at the Farm through `XRBootstrap`.
- Install the APK once on a developer-mode headset with Meta Quest Developer Hub or `adb install -r PlantDiseaseVR.apk`. Open the website in Quest Browser and press **See VR Demo**. If the browser stays open, try **Open installed app on Quest** on `/quest-demo`, or open **Plant Disease VR** from **Unknown Sources**.
- From a computer or phone, `/quest-demo` can send the HTTPS page to the headset using Meta Web Launch. Press the launch button again in Quest Browser.
- To show an APK download link, host the signed APK and set `VITE_QUEST_APK_URL` on the frontend before building. Use the same package ID (`com.btp.plantdiseasevr`) for builds that the site should launch.

## Good to know

- Free Render services sleep after 15 minutes idle; the first request then
  takes about a minute. Free Render Postgres expires after 30 days unless upgraded.
- The old `crop-disease-detector` Render service and its `/predict` API are
  not used by this version. Delete it once the new site works.
- Payments (Razorpay) and the VR video catalogue are off until their own keys
  and footage are added; scanning, languages and the field simulator work
  without them.
- To ship a new model: run `ml/eval_baseline.py` and `ml/export.py` (see the
  README), commit `services/api/model/`, and Render redeploys.
