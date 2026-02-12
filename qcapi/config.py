from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path

from .exceptions import ConfigError


DEFAULT_API_VERSION = "2026-02-01"
DEFAULT_BASE_URL_US = "https://quantum.cloud.ibm.com/api/v1"
DEFAULT_BASE_URL_EU_DE = "https://eu-de.quantum.cloud.ibm.com/api/v1"


def _infer_base_url_from_crn(service_crn: str) -> str:
    # CRN format includes the region (e.g. "...:eu-de:...")
    if ":eu-de:" in service_crn:
        return DEFAULT_BASE_URL_EU_DE
    return DEFAULT_BASE_URL_US


def _qiskit_config_path() -> Path:
    override = os.environ.get("QCAPI_QISKIT_CONFIG_PATH")
    if override:
        return Path(override).expanduser()
    return Path.home() / ".qiskit" / "qiskit-ibm.json"


def _load_qiskit_accounts(path: Path) -> dict[str, dict]:
    try:
        raw = path.read_text(encoding="utf-8")
    except FileNotFoundError as e:
        raise ConfigError(f"Qiskit config not found: {path}") from e
    try:
        obj = json.loads(raw)
    except json.JSONDecodeError as e:
        raise ConfigError(f"Invalid JSON in Qiskit config: {path}") from e

    if not isinstance(obj, dict):
        raise ConfigError(f"Unexpected Qiskit config shape (expected object): {path}")

    accounts: dict[str, dict] = {}
    for name, cfg in obj.items():
        if isinstance(cfg, dict):
            accounts[str(name)] = cfg
    if not accounts:
        raise ConfigError(f"No accounts found in Qiskit config: {path}")
    return accounts


def _select_ibm_cloud_account(accounts: dict[str, dict], account_name: str | None) -> tuple[str, dict]:
    if account_name:
        cfg = accounts.get(account_name)
        if not cfg:
            raise ConfigError(f"Account {account_name!r} not found in Qiskit config")
        return account_name, cfg

    # Prefer is_default_account if present.
    for name, cfg in accounts.items():
        if cfg.get("is_default_account") is True:
            return name, cfg

    # Then prefer a conventional name.
    for preferred in ("default-ibm-cloud", "default"):
        if preferred in accounts:
            return preferred, accounts[preferred]

    # Finally, first account.
    name = next(iter(accounts.keys()))
    return name, accounts[name]


@dataclass(frozen=True)
class QcapiConfig:
    ibm_cloud_api_key: str
    service_crn: str
    base_url: str
    api_version: str = DEFAULT_API_VERSION
    account_name: str | None = None

    @classmethod
    def from_env(cls) -> QcapiConfig | None:
        api_key = os.environ.get("IBM_CLOUD_API_KEY")
        crn = os.environ.get("QCAPI_SERVICE_CRN")
        if not api_key and not crn:
            return None
        if not api_key:
            raise ConfigError("Missing env var IBM_CLOUD_API_KEY")
        if not crn:
            raise ConfigError("Missing env var QCAPI_SERVICE_CRN")

        base_url = os.environ.get("QCAPI_BASE_URL") or _infer_base_url_from_crn(crn)
        api_version = os.environ.get("QCAPI_API_VERSION") or DEFAULT_API_VERSION
        return cls(
            ibm_cloud_api_key=api_key,
            service_crn=crn,
            base_url=base_url,
            api_version=api_version,
            account_name=None,
        )

    @classmethod
    def from_qiskit(cls, *, account_name: str | None = None) -> QcapiConfig:
        path = _qiskit_config_path()
        accounts = _load_qiskit_accounts(path)
        env_account = os.environ.get("QCAPI_QISKIT_ACCOUNT")
        name, cfg = _select_ibm_cloud_account(accounts, env_account or account_name)

        channel = cfg.get("channel")
        if channel != "ibm_cloud":
            raise ConfigError(
                "Selected Qiskit account is not an IBM Cloud account. "
                f"account={name!r} channel={channel!r}. "
                "Pick an account with channel 'ibm_cloud' or set IBM_CLOUD_API_KEY/QCAPI_SERVICE_CRN env vars."
            )

        api_key = cfg.get("token")
        service_crn = cfg.get("instance")
        if not isinstance(api_key, str) or not api_key.strip():
            raise ConfigError(f"Missing/invalid token in Qiskit account {name!r}")
        if not isinstance(service_crn, str) or not service_crn.strip():
            raise ConfigError(f"Missing/invalid instance (Service CRN) in Qiskit account {name!r}")

        base_url = os.environ.get("QCAPI_BASE_URL") or _infer_base_url_from_crn(service_crn)
        api_version = os.environ.get("QCAPI_API_VERSION") or DEFAULT_API_VERSION
        return cls(
            ibm_cloud_api_key=api_key,
            service_crn=service_crn,
            base_url=base_url,
            api_version=api_version,
            account_name=name,
        )

    @classmethod
    def load(cls, *, account_name: str | None = None) -> QcapiConfig:
        env_cfg = cls.from_env()
        if env_cfg:
            return env_cfg
        return cls.from_qiskit(account_name=account_name)

