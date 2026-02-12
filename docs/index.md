---
layout: default
title: qcapi
---

# qcapi

Kleine Python client + CLI voor de IBM Quantum Qiskit Runtime REST API.

Met `qcapi` kun je snel endpoints testen zoals:
- `versions`
- `backends`
- `programs`
- `jobs`

## Snelle start

```bash
python3 -m qcapi config
python3 -m qcapi versions
python3 -m qcapi backends
python3 -m qcapi recent-quantum-jobs
```

## Credentials

Gebruik een account uit `~/.qiskit/qiskit-ibm.json` (met `channel: ibm_cloud`) of zet:

```bash
export IBM_CLOUD_API_KEY="..."
export QCAPI_SERVICE_CRN="crn:..."
```

## Windows tray app

Er is ook een eenvoudige Windows tray app beschikbaar in de map `windows/`.
