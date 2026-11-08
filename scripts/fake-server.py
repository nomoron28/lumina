"""A tiny web server for end-to-end tests of network playback.

  python scripts/fake-server.py <port> <folder> [--user alice --password secret]

Serves files from <folder> with Range support (so mpv can seek). Paths under /private/ need HTTP Basic auth when
--user is given. /missing.mkv is always 404. /slow/<file> waits 4 s before answering. /trickle/<file> sends the file slowly
(about 800 KB/s) so it behaves like a live stream that stays open, which is what recording needs. Prints READY when listening,
and appends one line per request to <folder>/../fake-server.log so tests can check what was asked.
"""
import base64, os, sys, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

port = int(sys.argv[1]); root = os.path.abspath(sys.argv[2])
user = password = None
if "--user" in sys.argv: user = sys.argv[sys.argv.index("--user") + 1]
if "--password" in sys.argv: password = sys.argv[sys.argv.index("--password") + 1]
log_path = os.path.join(os.path.dirname(root), "fake-server.log")


class H(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):  # silence default stderr
        pass

    def note(self, status):
        with open(log_path, "a", encoding="utf8") as f:
            f.write(f"{self.command} {self.path} {status} auth={'yes' if self.headers.get('Authorization') else 'no'}\n")

    def reply(self, status, body=b"", headers=None):
        self.note(status)
        self.send_response(status)
        for k, v in (headers or {}).items(): self.send_header(k, v)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if self.command != "HEAD": self.wfile.write(body)

    def authorised(self):
        if not self.path.startswith("/private/") or user is None: return True
        want = "Basic " + base64.b64encode(f"{user}:{password}".encode()).decode()
        return self.headers.get("Authorization") == want

    def do_HEAD(self): self.do_GET()

    def do_GET(self):
        path = self.path.split("?")[0]
        trickle = path.startswith("/trickle/")
        if trickle: path = path[len("/trickle"):]
        if path.startswith("/slow/"):
            time.sleep(4); path = path[len("/slow"):]
        if path.startswith("/private/"):
            if not self.authorised():
                return self.reply(401, b"login needed", {"WWW-Authenticate": 'Basic realm="Lumina test"'})
            path = path[len("/private"):]
        local = os.path.normpath(os.path.join(root, path.lstrip("/")))
        if not local.startswith(root) or not os.path.isfile(local):
            return self.reply(404, b"not found")
        size = os.path.getsize(local)
        start, end, status = 0, size - 1, 200
        rng = self.headers.get("Range")
        if rng and rng.startswith("bytes="):
            a, _, b = rng[6:].partition("-")
            start = int(a) if a else max(0, size - int(b))
            end = int(b) if (b and a) else size - 1
            end = min(end, size - 1); status = 206
        length = end - start + 1
        self.note(status)
        self.send_response(status)
        self.send_header("Content-Type", "video/x-matroska")
        self.send_header("Accept-Ranges", "bytes")
        if status == 206: self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        self.send_header("Content-Length", str(length))
        self.end_headers()
        if self.command == "HEAD": return
        try:
            with open(local, "rb") as f:
                f.seek(start); left = length
                while left > 0:
                    chunk = f.read(min(49152 if trickle else 65536, left))
                    if not chunk: break
                    self.wfile.write(chunk); left -= len(chunk)
                    if trickle: self.wfile.flush(); time.sleep(0.06)
        except (BrokenPipeError, ConnectionResetError):
            pass


srv = ThreadingHTTPServer(("127.0.0.1", port), H)
print("READY", flush=True)
srv.serve_forever()
