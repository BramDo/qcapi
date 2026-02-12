from __future__ import annotations

import json
import time
import urllib.error
import urllib.parse
import urllib.request

from .exceptions import HttpError


class IbmCloudIamTokenProvider:
    def __init__(self, api_key: str, *, iam_url: str = "https://iam.cloud.ibm.com/identity/token", timeout_s: float = 30.0):
        self._api_key = api_key
        self._iam_url = iam_url
        self._timeout_s = timeout_s

        self._access_token: str | None = None
        self._expires_at_unix_s: float = 0.0

    def get_token(self) -> str:
        # Refresh with some slack so long-running requests don't race expiry.
        if self._access_token and (time.time() < (self._expires_at_unix_s - 60)):
            return self._access_token

        form = urllib.parse.urlencode(
            {
                "grant_type": "urn:ibm:params:oauth:grant-type:apikey",
                "apikey": self._api_key,
            }
        ).encode("ascii")

        req = urllib.request.Request(
            self._iam_url,
            data=form,
            headers={
                "Accept": "application/json",
                "Content-Type": "application/x-www-form-urlencoded",
            },
            method="POST",
        )

        try:
            with urllib.request.urlopen(req, timeout=self._timeout_s) as resp:
                raw = resp.read()
                status = getattr(resp, "status", 200)
        except urllib.error.HTTPError as e:  # noqa: F841 - urllib stores body on the exception
            raw = e.read()
            raise HttpError(getattr(e, "code", None), "IAM token request failed", url=self._iam_url, body=_maybe_json(raw)) from e
        except urllib.error.URLError as e:
            raise HttpError(None, f"IAM token request failed: {e}", url=self._iam_url) from e

        if status < 200 or status >= 300:
            raise HttpError(status, "IAM token request failed", url=self._iam_url, body=_maybe_json(raw))

        obj = _maybe_json(raw)
        if not isinstance(obj, dict) or "access_token" not in obj:
            raise HttpError(status, "IAM token response missing access_token", url=self._iam_url, body=obj)

        access_token = obj["access_token"]
        expires_in = int(obj.get("expires_in", 3600))

        self._access_token = str(access_token)
        self._expires_at_unix_s = time.time() + expires_in
        return self._access_token


def _maybe_json(raw: bytes) -> object:
    try:
        return json.loads(raw.decode("utf-8"))
    except Exception:
        try:
            return raw.decode("utf-8", errors="replace")
        except Exception:
            return None
