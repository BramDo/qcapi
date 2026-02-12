import json
import os
import tempfile
import unittest
from pathlib import Path

from qcapi.config import QcapiConfig
from qcapi.exceptions import ConfigError


class TestConfig(unittest.TestCase):
    def setUp(self) -> None:
        self._env_backup = dict(os.environ)

    def tearDown(self) -> None:
        os.environ.clear()
        os.environ.update(self._env_backup)

    def test_from_qiskit_picks_default_account(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "qiskit-ibm.json"
            p.write_text(
                json.dumps(
                    {
                        "a": {"channel": "ibm_cloud", "token": "k1", "instance": "crn:v1:...:us-east:a/..."},
                        "b": {
                            "channel": "ibm_cloud",
                            "token": "k2",
                            "instance": "crn:v1:...:eu-de:a/...",
                            "is_default_account": True,
                        },
                    }
                ),
                encoding="utf-8",
            )
            os.environ["QCAPI_QISKIT_CONFIG_PATH"] = str(p)

            cfg = QcapiConfig.from_qiskit()
            self.assertEqual(cfg.account_name, "b")
            self.assertEqual(cfg.ibm_cloud_api_key, "k2")
            self.assertIn("eu-de.quantum.cloud.ibm.com", cfg.base_url)

    def test_from_qiskit_rejects_non_ibm_cloud_channel(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "qiskit-ibm.json"
            p.write_text(
                json.dumps(
                    {
                        "default-ibm-quantum": {
                            "channel": "ibm_quantum",
                            "token": "t",
                            "instance": "ibm-q/open/main",
                            "is_default_account": True,
                        }
                    }
                ),
                encoding="utf-8",
            )
            os.environ["QCAPI_QISKIT_CONFIG_PATH"] = str(p)

            with self.assertRaises(ConfigError):
                QcapiConfig.from_qiskit()

    def test_load_prefers_env(self) -> None:
        os.environ["IBM_CLOUD_API_KEY"] = "env-k"
        os.environ["QCAPI_SERVICE_CRN"] = "crn:v1:...:us-east:a/..."

        cfg = QcapiConfig.load()
        self.assertEqual(cfg.ibm_cloud_api_key, "env-k")
        self.assertEqual(cfg.service_crn, "crn:v1:...:us-east:a/...")
        self.assertIsNone(cfg.account_name)

