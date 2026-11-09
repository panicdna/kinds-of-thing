"""Test builder freshness against a local HTTP fixture, not internet poetry."""
import importlib.util
import os
from pathlib import Path
from http.server import BaseHTTPRequestHandler, HTTPServer
import tempfile
import threading
import unittest

spec = importlib.util.spec_from_file_location('poetry_builder', Path(__file__).resolve().parents[1] / 'tools' / 'build_poetry.py')
builder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(builder)


class RefreshTests(unittest.TestCase):
    def test_refresh_does_not_silently_use_a_previous_process_cache(self):
        class Source(BaseHTTPRequestHandler):
            requests = 0

            def do_GET(self):
                Source.requests += 1
                self.send_response(200)
                self.end_headers()
                self.wfile.write(b'fresh local test fixture')

            def log_message(self, *_args):
                pass

        with tempfile.TemporaryDirectory(dir=os.environ.get('TMPDIR')) as cache:
            original_cache = builder.CACHE
            builder.CACHE = Path(cache)
            server = HTTPServer(('127.0.0.1', 0), Source)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            try:
                url = f'http://127.0.0.1:{server.server_port}/source'
                (builder.CACHE / (builder.digest(url) + '.html')).write_text('stale local test fixture', encoding='utf-8')
                self.assertEqual(builder.get(url), 'fresh local test fixture')
                self.assertEqual(Source.requests, 1, 'a refresh must retrieve current source data')
                self.assertEqual(builder.get(url), 'fresh local test fixture')
                self.assertEqual(Source.requests, 1, 'reuse is allowed within this same import run')
            finally:
                server.shutdown()
                server.server_close()
                thread.join()
                builder.CACHE = original_cache


if __name__ == '__main__':
    unittest.main()
