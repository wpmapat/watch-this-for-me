"""Controllable fake 'release notes' site for outage scenarios.

GET /product-a, /product-b         -> the page (or an outage, depending on mode)
GET /admin/mode?page=X&mode=Y      -> Y in: up | down (HTTP 503) | maintenance (HTTP 200 maintenance page)
GET /admin/publish?page=X&text=T   -> publish a new release (visible only when mode=up)
"""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

PAGES = {}  # page -> {"mode": str, "releases": [str]}

def page(name):
    return PAGES.setdefault(name, {"mode": "up", "releases": []})

class Site(BaseHTTPRequestHandler):
    def send(self, code, body):
        data = body.encode()
        self.send_response(code)
        self.send_header("Content-Type", "text/html")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        url = urlparse(self.path)
        q = {k: v[0] for k, v in parse_qs(url.query).items()}
        if url.path == "/admin/mode":
            page(q["page"])["mode"] = q["mode"]
            return self.send(200, "ok")
        if url.path == "/admin/publish":
            page(q["page"])["releases"].insert(0, q["text"])
            return self.send(200, "ok")

        p = page(url.path.strip("/"))
        if p["mode"] == "down":
            return self.send(503, "<html><body>Service Unavailable</body></html>")
        if p["mode"] == "maintenance":
            return self.send(200, "<html><body><h1>Down for maintenance</h1><p>We are performing scheduled maintenance. Please check back soon.</p></body></html>")
        releases = "".join(f"<h2>{r.split(':')[0]}</h2><p>{r.split(':', 1)[1].strip()}</p>" for r in p["releases"])
        return self.send(200, f"<html><body><h1>Product release notes</h1>{releases}</body></html>")

    def log_message(self, *args):
        pass

ThreadingHTTPServer(("localhost", 5098), Site).serve_forever()
