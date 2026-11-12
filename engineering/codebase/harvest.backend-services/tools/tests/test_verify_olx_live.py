import importlib.util
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from threading import Thread
import unittest

spec = importlib.util.spec_from_file_location("verify_olx_live", Path(__file__).parents[1] / "verify-olx-live.py")
verification = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verification)


class VerificationClientTests(unittest.TestCase):
    def setUp(self):
        self.calls = []
        calls = self.calls

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                calls.append((self.path, self.headers.get("X-Api-Key")))
                self.send_response(302 if self.path == "/redirect" else 403)
                if self.path == "/redirect":
                    self.send_header("Location", "/should-not-receive-credentials")
                self.end_headers()
                self.wfile.write(b'{"message":"synthetic-operator-key"}')

            def log_message(self, *args):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.request = verification.create_requester(
            "http://127.0.0.1:" + str(self.server.server_port), "synthetic-operator-key")

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)

    def test_redirect_does_not_forward_operator_credentials(self):
        with self.assertRaisesRegex(RuntimeError, "HTTP 302"):
            self.request("/redirect")
        self.assertEqual(self.calls, [("/redirect", "synthetic-operator-key")])

    def test_error_response_bodies_are_not_echoed(self):
        with self.assertRaises(RuntimeError) as result:
            self.request("/failure")
        self.assertNotIn("synthetic-operator-key", str(result.exception))


if __name__ == "__main__":
    unittest.main()
