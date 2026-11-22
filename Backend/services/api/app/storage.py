"""Media links for S3-compatible storage and database-backed scans."""

from __future__ import annotations

import hmac
from functools import lru_cache
from time import time
from urllib.parse import urlencode

import boto3
from botocore.config import Config

from app.config import get_settings

DATABASE_SCAN_PREFIX = "db:scans/"


def database_scan_key(scan_id: str, filename: str) -> str:
    return f"{DATABASE_SCAN_PREFIX}{scan_id}/{filename}"


def _scan_signature(key: str, expires: int) -> str:
    secret = get_settings().scan_media_signing_key.get_secret_value()
    return hmac.digest(secret.encode(), f"{key}\n{expires}".encode(), "sha256").hex()


def valid_scan_signature(key: str, expires: int, signature: str) -> bool:
    settings = get_settings()
    return (
        bool(settings.scan_media_signing_key.get_secret_value())
        and expires > int(time())
        and hmac.compare_digest(_scan_signature(key, expires), signature)
    )


@lru_cache(maxsize=1)
def _client():
    s = get_settings()
    return boto3.client(
        "s3",
        endpoint_url=s.s3_endpoint_url or None,
        aws_access_key_id=s.s3_access_key_id or None,
        aws_secret_access_key=s.s3_secret_access_key or None,
        region_name=s.s3_region,
        config=Config(signature_version="s3v4"),
    )


def _local_dir():
    """The local media folder in development (LOCAL_MEDIA_DIR), else None."""
    return get_settings().local_media_dir


def _local_path(key: str):
    root = _local_dir().resolve()
    path = (root / key).resolve()
    # Keys are built by the server, but a key must never escape the media folder.
    if root not in path.parents:
        raise ValueError(f"Invalid media key: {key}")
    return path


def put(key: str, data: bytes, content_type: str) -> str:
    if _local_dir() is not None:
        path = _local_path(key)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return key
    _client().put_object(
        Bucket=get_settings().s3_bucket, Key=key, Body=data, ContentType=content_type
    )
    return key


# Prefixes served without a signature.
#
# HLS cannot use presigned URLs. The master playlist references child playlists
# and segments by RELATIVE path, so the player resolves them against the signed
# parent and requests them WITHOUT the signature -- every one 403s. Signing only
# the master breaks playback entirely.
#
# The split is by what actually needs protecting. A scan image is a photograph
# somebody took on their own farm, so it stays signed and short-lived. Video is
# curated footage we publish on purpose; signing it would be security theatre
# that happens to break the format.
PUBLIC_PREFIXES = ("videos/",)


def is_public(key: str) -> bool:
    return key.startswith(PUBLIC_PREFIXES)


def public_url(key: str) -> str:
    s = get_settings()
    return f"{s.s3_endpoint_url.rstrip('/')}/{s.s3_bucket}/{key}"


def url_for(key: str | None, ttl: int | None = None) -> str | None:
    """Public link for curated media, signed link for private media."""
    if not key:
        return None
    s = get_settings()
    if key.startswith(DATABASE_SCAN_PREFIX):
        if not s.scan_media_signing_key.get_secret_value():
            return None
        expires = int(time()) + (ttl if ttl is not None else s.signed_url_ttl_seconds)
        query = urlencode({"expires": expires, "sig": _scan_signature(key, expires)})
        path = key.removeprefix(DATABASE_SCAN_PREFIX)
        base = (s.render_external_url or s.public_base_url).rstrip("/")
        return f"{base}/api/scans/media/{path}?{query}"
    if _local_dir() is not None:
        return f"{s.public_base_url.rstrip('/')}/media/{key}"
    if is_public(key):
        return public_url(key)

    return _client().generate_presigned_url(
        "get_object",
        Params={"Bucket": s.s3_bucket, "Key": key},
        ExpiresIn=ttl or s.signed_url_ttl_seconds,
    )


# Kept so existing callers keep working; url_for is the one to use.
signed_url = url_for


def healthy() -> bool:
    if _local_dir() is not None:
        return _local_dir().is_dir()
    try:
        _client().head_bucket(Bucket=get_settings().s3_bucket)
        return True
    except Exception:
        return False
