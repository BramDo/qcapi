import io
import json
import unittest
from unittest import mock

import urllib.error

from qcapi.auth import IbmCloudIamTokenProvider
from qcapi.exceptions import HttpError


class _FakeResp:
    def __init__(self, body: bytes, status: int = 200):
        self._body = body
        self.status = status

    def read(self) -> bytes:
        return self._body

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        return False


class TestIbmCloudIamTokenProvider(unittest.TestCase):
    def test_get_token_caches(self) -> None:
        provider = IbmCloudIamTokenProvider("k", iam_url="https://example.invalid/token")
        body = json.dumps({"access_token": "t1", "expires_in": 3600}).encode("utf-8")

        with mock.patch("urllib.request.urlopen", return_value=_FakeResp(body)) as urlopen:
            t1 = provider.get_token()
            t2 = provider.get_token()

        self.assertEqual(t1, "t1")
        self.assertEqual(t2, "t1")
        self.assertEqual(urlopen.call_count, 1)

    def test_get_token_http_error_raises(self) -> None:
        provider = IbmCloudIamTokenProvider("k", iam_url="https://example.invalid/token")
        fp = io.BytesIO(b'{"error":"nope"}')
        err = urllib.error.HTTPError(
            provider._iam_url, 401, "Unauthorized", hdrs=None, fp=fp  # type: ignore[arg-type]
        )

        with mock.patch("urllib.request.urlopen", side_effect=err):
            with self.assertRaises(HttpError) as ctx:
                provider.get_token()

        self.assertEqual(ctx.exception.status, 401)

