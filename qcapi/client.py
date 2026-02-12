from __future__ import annotations

import json
import urllib.error
import urllib.parse
import urllib.request

from .auth import IbmCloudIamTokenProvider
from .config import QcapiConfig
from .exceptions import HttpError


class QiskitRuntimeRestClient:
    def __init__(self, config: QcapiConfig, *, timeout_s: float = 30.0):
        self._cfg = config
        self._timeout_s = timeout_s
        self._token_provider = IbmCloudIamTokenProvider(config.ibm_cloud_api_key, timeout_s=timeout_s)

    @property
    def config(self) -> QcapiConfig:
        return self._cfg

    def get_versions(self) -> object:
        return self._request_json("GET", "/versions", need_auth=False, need_crn=False, include_api_version_header=False)

    def list_backends(self) -> object:
        return self._request_json("GET", "/backends")

    def get_backend_properties(self, backend_name: str) -> object:
        return self._request_json("GET", f"/backends/{urllib.parse.quote(backend_name)}/properties")

    def get_backend_status(self, backend_name: str) -> object:
        return self._request_json("GET", f"/backends/{urllib.parse.quote(backend_name)}/status")

    def list_programs(self) -> object:
        return self._request_json("GET", "/programs")

    def get_program(self, program_id: str) -> object:
        return self._request_json("GET", f"/programs/{urllib.parse.quote(program_id)}")

    def list_jobs(self, **query: object) -> object:
        # Accepts arbitrary query params; callers can pass limit=..., backend=..., program_id=..., pending=true, ...
        return self._request_json("GET", "/jobs", params=query or None)

    def get_job(self, job_id: str) -> object:
        return self._request_json("GET", f"/jobs/{urllib.parse.quote(job_id)}")

    def delete_job(self, job_id: str) -> object:
        return self._request_json("DELETE", f"/jobs/{urllib.parse.quote(job_id)}")

    def cancel_job(self, job_id: str) -> object:
        return self._request_json("POST", f"/jobs/{urllib.parse.quote(job_id)}/cancel")

    def get_job_results(self, job_id: str) -> object:
        return self._request_json("GET", f"/jobs/{urllib.parse.quote(job_id)}/results")

    def get_job_interim_results(self, job_id: str) -> object:
        return self._request_json("GET", f"/jobs/{urllib.parse.quote(job_id)}/interim_results")

    def get_job_metrics(self, job_id: str) -> object:
        return self._request_json("GET", f"/jobs/{urllib.parse.quote(job_id)}/metrics")

    def submit_job(
        self,
        *,
        program_id: str,
        backend: str,
        params: dict,
        **extra_fields: object,
    ) -> object:
        body = {"program_id": program_id, "backend": backend, "params": params}
        body.update(extra_fields)
        return self._request_json("POST", "/jobs", json_body=body)

    def list_sessions(self, **query: object) -> object:
        return self._request_json("GET", "/sessions", params=query or None)

    def get_session(self, session_id: str) -> object:
        return self._request_json("GET", f"/sessions/{urllib.parse.quote(session_id)}")

    def create_session(self, **body: object) -> object:
        return self._request_json("POST", "/sessions", json_body=body or {})

    def close_session(self, session_id: str) -> object:
        return self._request_json("POST", f"/sessions/{urllib.parse.quote(session_id)}/close")

    def request(
        self,
        method: str,
        path: str,
        *,
        params: dict[str, object] | None = None,
        json_body: object | None = None,
        need_auth: bool = True,
        need_crn: bool = True,
        include_api_version_header: bool = True,
    ) -> object:
        return self._request_json(
            method,
            path,
            params=params,
            json_body=json_body,
            need_auth=need_auth,
            need_crn=need_crn,
            include_api_version_header=include_api_version_header,
        )

    def _request_json(
        self,
        method: str,
        path: str,
        *,
        params: dict[str, object] | None = None,
        json_body: object | None = None,
        need_auth: bool = True,
        need_crn: bool = True,
        include_api_version_header: bool = True,
    ) -> object:
        url = self._cfg.base_url.rstrip("/") + "/" + path.lstrip("/")

        if params:
            # Filter out None values so callers can pass optional kwargs easily.
            filtered = {k: v for k, v in params.items() if v is not None}
            if filtered:
                url += "?" + urllib.parse.urlencode(filtered, doseq=True)

        headers: dict[str, str] = {
            "Accept": "application/json",
            "User-Agent": "qcapi/0.1.0",
        }
        if include_api_version_header and self._cfg.api_version:
            headers["IBM-API-Version"] = self._cfg.api_version
        if need_crn:
            headers["Service-CRN"] = self._cfg.service_crn
        if need_auth:
            token = self._token_provider.get_token()
            headers["Authorization"] = f"Bearer {token}"

        data: bytes | None = None
        if json_body is not None:
            headers["Content-Type"] = "application/json"
            data = json.dumps(json_body).encode("utf-8")

        req = urllib.request.Request(url, data=data, headers=headers, method=method.upper())

        try:
            with urllib.request.urlopen(req, timeout=self._timeout_s) as resp:
                raw = resp.read()
                status = getattr(resp, "status", 200)
        except urllib.error.HTTPError as e:
            raw = e.read()
            raise HttpError(getattr(e, "code", None), "HTTP request failed", url=url, body=_maybe_json(raw)) from e
        except urllib.error.URLError as e:
            raise HttpError(None, f"HTTP request failed: {e}", url=url) from e

        if status < 200 or status >= 300:
            raise HttpError(status, "HTTP request failed", url=url, body=_maybe_json(raw))

        return _maybe_json(raw)


def _maybe_json(raw: bytes) -> object:
    if not raw:
        return None
    try:
        return json.loads(raw.decode("utf-8"))
    except Exception:
        return raw.decode("utf-8", errors="replace")
