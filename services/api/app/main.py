"""CropScan XR API. One FastAPI service, one Postgres.

No Redis, no Celery, no queue, no microservices.
"""

from __future__ import annotations

import asyncio
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles

from app.config import get_settings
from app.cycle import load_cycles
from app.inference import load_model
from app.routers import activity, diseases, health, ledger, legacy, orders, scans, treatment, videos, webhooks, xr
from app.treatment import load_catalog

log = logging.getLogger("cropscan")


@asynccontextmanager
async def lifespan(app: FastAPI):
    # Parse the knowledge base at startup. Malformed content should stop the
    # deploy, not surface as a blank panel in a headset.
    cycles = load_cycles()
    log.info("loaded %d disease cycles", len(cycles))
    _, treatment_cases, _, _ = load_catalog()
    log.info("loaded %d treatment cases", len(treatment_cases))

    # The ONNX session loads once. A missing model is not fatal at boot -- the
    # rest of the API is still useful and /healthz reports degraded -- but
    # /api/scans returns 503 rather than inventing a prediction.
    try:
        model = load_model()
        log.info("model %s ready, %d classes", model.version, len(model.labels))
    except Exception as exc:
        # Deliberately broad. The design is that a missing model degrades to a
        # 503 on /api/scans while the rest of the API stays up -- but catching
        # only ModelUnavailable meant a corrupt .onnx, a meta.json missing
        # num_classes, or onnxruntime not installed killed startup outright,
        # which is the opposite of degrading.
        log.warning("inference unavailable: %s: %s", type(exc).__name__, exc)

    yield


def create_app() -> FastAPI:
    settings = get_settings()
    app = FastAPI(
        title="CropScan XR API",
        version="2.0.0",
        summary="Diagnosis, disease cycle, and a hash-chained record of both.",
        lifespan=lifespan,
    )
    app.state.scan_slots = asyncio.Semaphore(settings.scan_concurrency)

    origins = ([o.strip() for o in settings.cors_origins.split(",")]
               if settings.cors_origins != "*" else ["*"])
    app.add_middleware(
        CORSMiddleware,
        allow_origins=origins,
        allow_credentials=False,
        allow_methods=["*"],
        allow_headers=["*"],
    )

    for module in (health, scans, diseases, ledger, videos, orders, webhooks, xr, activity, treatment):
        app.include_router(module.router)

    # Local development only (LOCAL_MEDIA_DIR): serve stored media without MinIO.
    if settings.local_media_dir is not None:
        settings.local_media_dir.mkdir(parents=True, exist_ok=True)
        app.mount("/media", StaticFiles(directory=settings.local_media_dir), name="media")
    app.include_router(legacy.router)   # 308 shims, delete one release after cutover

    return app


app = create_app()
