# qcapi

Kleine Python client + CLI om de **IBM Quantum Qiskit Runtime REST API** aan te roepen.

Doel: snel endpoints testen (bijv. `versions`, `backends`, `programs`, `jobs`) zonder meteen de volledige Qiskit SDK te hoeven gebruiken.

## Credentials

Deze client gebruikt de **IBM Cloud** flow:

1. IBM Cloud API key -> IAM bearer token
2. Bearer token + `Service-CRN` header -> Qiskit Runtime REST API calls

Je `~/.qiskit/qiskit-ibm.json` bevat al accounts. Als je default account `channel: ibm_cloud` heeft, kan qcapi die direct gebruiken.

## Gebruik (CLI)

```bash
cd /home/bram/qcapi
python3 -m qcapi config
python3 -m qcapi versions
python3 -m qcapi backends
python3 -m qcapi programs
python3 -m qcapi jobs --limit 5
python3 -m qcapi recent-quantum-jobs
python3 -m qcapi request GET /versions --no-auth --no-crn --no-api-version
```

Optioneel (als je niet uit `~/.qiskit/qiskit-ibm.json` wilt lezen):

```bash
export IBM_CLOUD_API_KEY="..."
export QCAPI_SERVICE_CRN="crn:..."
python3 -m qcapi backends
```

Handige env vars:

- `QCAPI_QISKIT_ACCOUNT`: account-name uit `~/.qiskit/qiskit-ibm.json` (anders wordt default account gekozen)
- `QCAPI_API_VERSION`: default `2026-02-01`
- `QCAPI_BASE_URL`: override (default `https://quantum.cloud.ibm.com/api/v1`, of `https://eu-de.quantum.cloud.ibm.com/api/v1` als je CRN `eu-de` bevat)
- `QCAPI_QISKIT_CONFIG_PATH`: override pad naar `qiskit-ibm.json` (handig voor tests)

## Gebruik (Python)

```python
from qcapi import QcapiConfig, QiskitRuntimeRestClient

cfg = QcapiConfig.from_qiskit()
client = QiskitRuntimeRestClient(cfg)

print(client.get_versions())
print(client.list_backends()[:2])
```

## Tests

```bash
cd /home/bram/qcapi
python3 -m unittest discover -q
```

## Windows tray app (prototype)

Zie `windows/README.md`.
