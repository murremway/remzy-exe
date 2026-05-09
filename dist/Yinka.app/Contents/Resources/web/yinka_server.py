#!/usr/bin/env python3
"""Yinka — tiny static HTTP server with control endpoints.

Adds two routes on top of the stdlib `SimpleHTTPRequestHandler`:
  GET /__alive  → 200 "ok"  (used by the launcher to detect a running instance)
  GET /__quit   → 200, then schedules a clean shutdown on a background thread
                  so the .app process can exit when the user clicks Quit.

Usage: python3 yinka_server.py <port> <web_root>
"""

from __future__ import annotations

import http.server
import os
import socket
import socketserver
import sys
import threading


class YinkaHandler(http.server.SimpleHTTPRequestHandler):
    server_version = "Yinka/1.0"

    def do_GET(self):  # noqa: N802 (stdlib name)
        path = self.path.split("?", 1)[0]
        if path == "/__alive":
            self._reply(200, b"ok")
            return
        if path == "/__quit":
            self._reply(200, b"Yinka server stopping. Goodbye.")

            def _stop():
                try:
                    self.server.shutdown()
                finally:
                    os._exit(0)

            threading.Thread(target=_stop, daemon=True).start()
            return
        super().do_GET()

    def _reply(self, status: int, body: bytes) -> None:
        self.send_response(status)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):  # noqa: A002, N802
        # Keep stderr quiet — the .app launcher discards it anyway.
        return


class ThreadedServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


def main(argv: list[str]) -> int:
    port = int(argv[1]) if len(argv) > 1 else 8731
    web_root = argv[2] if len(argv) > 2 else os.getcwd()
    if not os.path.isdir(web_root):
        print(f"yinka_server: web root not found: {web_root}", file=sys.stderr)
        return 2
    os.chdir(web_root)

    try:
        with ThreadedServer(("127.0.0.1", port), YinkaHandler) as httpd:
            httpd.serve_forever()
    except OSError as err:
        print(f"yinka_server: bind failed on 127.0.0.1:{port} ({err})", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        return 0
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
