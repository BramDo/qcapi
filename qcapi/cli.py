from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .client import QiskitRuntimeRestClient
from .config import QcapiConfig
from .exceptions import ConfigError, HttpError


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="qcapi", description="IBM Quantum Qiskit Runtime REST API helper")
    parser.add_argument("--account", help="Account name from ~/.qiskit/qiskit-ibm.json (default: auto)")
    parser.add_argument("--raw", action="store_true", help="Print raw JSON (no formatting)")

    sub = parser.add_subparsers(dest="cmd", required=True)

    sub.add_parser("config", help="Show resolved config (secrets omitted)")
    sub.add_parser("versions", help="GET /versions (no auth)")
    sub.add_parser("backends", help="GET /backends")
    sp = sub.add_parser("backend-status", help="GET /backends/{name}/status")
    sp.add_argument("name")
    sp = sub.add_parser("backend-properties", help="GET /backends/{name}/properties")
    sp.add_argument("name")

    sub.add_parser("programs", help="GET /programs")
    sp = sub.add_parser("program", help="GET /programs/{program_id}")
    sp.add_argument("program_id")

    sp = sub.add_parser("jobs", help="GET /jobs")
    sp.add_argument("--limit", type=int)
    sp.add_argument("--skip", type=int)
    sp.add_argument("--backend")
    sp.add_argument("--program-id")
    sp.add_argument("--pending")
    sp = sub.add_parser(
        "recent-quantum-jobs",
        help="Show recent jobs that ran on a quantum backend (no simulator)",
    )
    sp.add_argument("--limit", type=int, default=5, help="How many jobs to show (default: 5)")

    sp = sub.add_parser("job", help="GET /jobs/{job_id}")
    sp.add_argument("job_id")
    sp = sub.add_parser("job-results", help="GET /jobs/{job_id}/results")
    sp.add_argument("job_id")
    sp = sub.add_parser("job-cancel", help="POST /jobs/{job_id}/cancel")
    sp.add_argument("job_id")

    sp = sub.add_parser("request", help="Arbitrary request (for endpoints not wrapped by this CLI)")
    sp.add_argument("method", help="GET/POST/PUT/PATCH/DELETE")
    sp.add_argument("path", help="API path (e.g. /jobs)")
    sp.add_argument("--no-auth", action="store_true", help="Don't send Authorization header")
    sp.add_argument("--no-crn", action="store_true", help="Don't send Service-CRN header")
    sp.add_argument("--no-api-version", action="store_true", help="Don't send IBM-API-Version header")
    sp.add_argument("--param", action="append", default=[], help="Query param key=value (repeatable)")
    sp.add_argument("--json-file", help="Path to JSON body (object/array/etc)")

    args = parser.parse_args(argv)

    try:
        cfg = QcapiConfig.load(account_name=args.account)
        client = QiskitRuntimeRestClient(cfg)
        out = _run(client, args)
    except (ConfigError, HttpError) as e:
        print(f"error: {e}", file=sys.stderr)
        if isinstance(e, HttpError) and e.status is not None:
            print(f"status: {e.status}", file=sys.stderr)
        if isinstance(e, HttpError) and e.url:
            print(f"url: {e.url}", file=sys.stderr)
        if isinstance(e, HttpError) and e.body is not None:
            # Don't assume this is secret; API errors typically aren't. Still, keep it compact.
            print("body:", file=sys.stderr)
            try:
                print(json.dumps(e.body, indent=2, sort_keys=True), file=sys.stderr)
            except TypeError:
                print(str(e.body), file=sys.stderr)
        return 2

    if args.raw:
        print(json.dumps(out))
    else:
        print(json.dumps(out, indent=2, sort_keys=True))
    return 0


def _run(client: QiskitRuntimeRestClient, args: argparse.Namespace) -> object:
    cmd = args.cmd
    if cmd == "config":
        cfg = client.config
        crn = cfg.service_crn
        return {
            "account_name": cfg.account_name,
            "api_version": cfg.api_version,
            "base_url": cfg.base_url,
            "service_crn_hint": ("crn:...%s" % crn[-12:]) if crn.startswith("crn:") else crn,
        }
    if cmd == "versions":
        return client.get_versions()
    if cmd == "backends":
        return client.list_backends()
    if cmd == "backend-status":
        return client.get_backend_status(args.name)
    if cmd == "backend-properties":
        return client.get_backend_properties(args.name)
    if cmd == "programs":
        return client.list_programs()
    if cmd == "program":
        return client.get_program(args.program_id)
    if cmd == "jobs":
        query = {
            "limit": args.limit,
            "skip": args.skip,
            "backend": args.backend,
            "program_id": args.program_id,
            "pending": args.pending,
        }
        return client.list_jobs(**query)
    if cmd == "recent-quantum-jobs":
        return _recent_quantum_jobs(client, limit=args.limit)
    if cmd == "job":
        return client.get_job(args.job_id)
    if cmd == "job-results":
        return client.get_job_results(args.job_id)
    if cmd == "job-cancel":
        return client.cancel_job(args.job_id)
    if cmd == "request":
        params = _parse_params(args.param)
        body = None
        if args.json_file:
            body = json.loads(Path(args.json_file).read_text(encoding="utf-8"))
        return client.request(
            args.method,
            args.path,
            params=params or None,
            json_body=body,
            need_auth=not args.no_auth,
            need_crn=not args.no_crn,
            include_api_version_header=not args.no_api_version,
        )
    raise AssertionError(f"Unknown cmd: {cmd}")


def _parse_params(items: list[str]) -> dict[str, str]:
    out: dict[str, str] = {}
    for item in items or []:
        if "=" not in item:
            raise SystemExit(f"--param must be key=value, got: {item!r}")
        k, v = item.split("=", 1)
        out[k] = v
    return out


def _recent_quantum_jobs(client: QiskitRuntimeRestClient, *, limit: int = 5) -> list[dict[str, object]]:
    if limit < 1:
        raise SystemExit("--limit must be >= 1")

    backend_items = _extract_items(client.list_backends(), ("backends", "devices", "items", "results", "data"))
    quantum_backends = {
        name
        for backend in backend_items
        for name in [_first_string(backend, ("name", "backend_name", "backend", "id"))]
        if name and not _is_simulator_backend(backend)
    }
    if not quantum_backends:
        return []

    scan_limit = max(limit * 20, limit)
    job_items = _extract_items(
        client.list_jobs(limit=scan_limit, pending="false"),
        ("jobs", "items", "results", "data"),
    )

    out: list[dict[str, object]] = []
    for job in job_items:
        backend_name = _job_backend_name(job)
        if not backend_name or backend_name not in quantum_backends:
            continue

        job_id = _first_string(job, ("id", "job_id", "jobId"))
        if not job_id:
            continue

        row: dict[str, object] = {"id": job_id, "backend": backend_name}
        status = _first_string(job, ("status", "state"))
        if status:
            row["status"] = status
        created = _first_string(job, ("created", "created_at", "creation_date"))
        if created:
            row["created"] = created

        out.append(row)
        if len(out) >= limit:
            break

    return out


def _extract_items(payload: object, candidate_keys: tuple[str, ...]) -> list[dict[str, object]]:
    if isinstance(payload, list):
        return [item for item in payload if isinstance(item, dict)]
    if isinstance(payload, dict):
        for key in candidate_keys:
            value = payload.get(key)
            if isinstance(value, list):
                return [item for item in value if isinstance(item, dict)]
    return []


def _first_string(item: object, keys: tuple[str, ...]) -> str | None:
    if not isinstance(item, dict):
        return None
    for key in keys:
        value = item.get(key)
        if isinstance(value, str) and value:
            return value
    return None


def _job_backend_name(job: dict[str, object]) -> str | None:
    backend = job.get("backend")
    if isinstance(backend, str) and backend:
        return backend
    if isinstance(backend, dict):
        nested = _first_string(backend, ("name", "backend_name", "id"))
        if nested:
            return nested
    return _first_string(job, ("backend_name", "device", "target"))


def _is_simulator_backend(backend: dict[str, object]) -> bool:
    for key in ("simulator", "is_simulator"):
        if key in backend:
            return _as_bool(backend.get(key))
    name = _first_string(backend, ("name", "backend_name", "backend", "id"))
    return bool(name and "simulator" in name.lower())


def _as_bool(value: object) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        return value.strip().lower() in {"1", "true", "yes", "y"}
    if isinstance(value, (int, float)):
        return value != 0
    return False
