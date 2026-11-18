"""Controllable fake 'release notes' site for outage scenarios.

GET /product-a, /product-b         -> the page (or an outage, depending on mode)
GET /admin/mode?page=X&mode=Y      -> Y in:
      up          normal page
      down        HTTP 503
      maintenance HTTP 200 "down for maintenance" page
      gone        HTTP 404
      soft404     HTTP 200 "page not found" page
      moved       301 to /X/v2 (same releases, slightly different page)
      home        302 to the site's homepage
      redesign    same releases, completely different layout
GET /admin/publish?page=X&text=T   -> publish a new release (visible only when mode=up)
"""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

PAGES = {}  # page -> {"mode": str, "releases": [str]}

def page(name):
    return PAGES.setdefault(name, {"mode": "up", "releases": []})

def render(releases):
    return "".join(f"<h2>{r.split(':')[0]}</h2><p>{r.split(':', 1)[1].strip()}</p>" for r in releases)

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

        name = url.path.strip("/")
        if name == "":
            return self.send(200, "<html><body><h1>Welcome to Example Corp</h1><p>We build developer tools.</p><ul><li>Products</li><li>Pricing</li><li>Careers</li></ul></body></html>")
        if name.endswith("/v2"):
            releases = render(page(name[:-3])["releases"])
            return self.send(200, f"<html><body><p>Docs v2</p><h1>Product release notes</h1>{releases}</body></html>")

        p = page(name)
        if p["mode"] == "gone":
            return self.send(404, "<html><body>Not Found</body></html>")
        if p["mode"] == "soft404":
            return self.send(200, "<html><body><h1>Page not found</h1><p>Sorry, the page you are looking for does not exist or has been moved.</p></body></html>")
        if p["mode"] == "moved":
            return self.redirect(301, f"/{name}/v2")
        if p["mode"] == "home":
            return self.redirect(302, "/")
        if p["mode"] == "redesign":
            rows = "".join(f"<tr><td>{r.split(':')[0]}</td><td>{r.split(':', 1)[1].strip()}</td></tr>" for r in p["releases"])
            return self.send(200, f"<html><body><header><nav><a>Home</a> <a>Docs</a> <a>Blog</a> <a>Community</a></nav></header><main><h1>What's new</h1><p>Every release, all in one place. Subscribe to our newsletter!</p><table><tr><th>Version</th><th>Highlights</th></tr>{rows}</table></main><footer>Example Corp - Privacy - Terms</footer></body></html>")
        if p["mode"] == "down":
            return self.send(503, "<html><body>Service Unavailable</body></html>")
        if p["mode"] == "maintenance":
            return self.send(200, "<html><body><h1>Down for maintenance</h1><p>We are performing scheduled maintenance. Please check back soon.</p></body></html>")
        return self.send(200, f"<html><body><h1>Product release notes</h1>{render(p['releases'])}</body></html>")

    def redirect(self, code, location):
        self.send_response(code)
        self.send_header("Location", location)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def log_message(self, *args):
        pass

ThreadingHTTPServer(("localhost", 5098), Site).serve_forever()
