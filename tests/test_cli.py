import argparse
import unittest

from qcapi import cli


class _FakeClient:
    def __init__(self, *, backends: object, jobs: object):
        self._backends = backends
        self._jobs = jobs
        self.list_jobs_calls: list[dict[str, object]] = []

    def list_backends(self) -> object:
        return self._backends

    def list_jobs(self, **query: object) -> object:
        self.list_jobs_calls.append(query)
        return self._jobs


class TestCliRecentQuantumJobs(unittest.TestCase):
    def test_filters_simulators_and_applies_limit(self) -> None:
        client = _FakeClient(
            backends={
                "backends": [
                    {"name": "ibm_torino", "simulator": False},
                    {"name": "ibm_brisbane", "simulator": False},
                    {"name": "ibmq_qasm_simulator", "simulator": True},
                ]
            },
            jobs={
                "jobs": [
                    {"id": "job-sim", "backend": "ibmq_qasm_simulator", "status": "DONE"},
                    {"id": "job-1", "backend": "ibm_torino", "status": "COMPLETED"},
                    {"id": "job-2", "backend": "ibm_brisbane", "status": "COMPLETED"},
                    {"id": "job-3", "backend": "ibm_torino", "status": "COMPLETED"},
                ]
            },
        )

        args = argparse.Namespace(cmd="recent-quantum-jobs", limit=2)
        out = cli._run(client, args)

        self.assertEqual(
            out,
            [
                {"id": "job-1", "backend": "ibm_torino", "status": "COMPLETED"},
                {"id": "job-2", "backend": "ibm_brisbane", "status": "COMPLETED"},
            ],
        )
        self.assertEqual(client.list_jobs_calls, [{"limit": 40, "pending": "false"}])

    def test_supports_nested_backend_and_job_id_key(self) -> None:
        client = _FakeClient(
            backends=[{"name": "ibm_torino", "simulator": False}],
            jobs=[{"job_id": "abc123", "backend": {"name": "ibm_torino"}, "state": "DONE"}],
        )

        args = argparse.Namespace(cmd="recent-quantum-jobs", limit=5)
        out = cli._run(client, args)

        self.assertEqual(out, [{"id": "abc123", "backend": "ibm_torino", "status": "DONE"}])

    def test_returns_empty_when_no_quantum_backends(self) -> None:
        client = _FakeClient(
            backends={"backends": [{"name": "ibmq_qasm_simulator", "simulator": True}]},
            jobs={"jobs": [{"id": "job-1", "backend": "ibmq_qasm_simulator"}]},
        )

        args = argparse.Namespace(cmd="recent-quantum-jobs", limit=5)
        out = cli._run(client, args)

        self.assertEqual(out, [])
        self.assertEqual(client.list_jobs_calls, [])

    def test_limit_must_be_positive(self) -> None:
        client = _FakeClient(backends=[], jobs=[])
        args = argparse.Namespace(cmd="recent-quantum-jobs", limit=0)

        with self.assertRaises(SystemExit):
            cli._run(client, args)
