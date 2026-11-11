r"""Serves the web version locally: web/ at /, and the app data src/Resources at /data/ (as the Pages build does).

Usage:
  python -I tools/serve_web.py [port]      (default 8080, then open http://localhost:8080)
"""
import functools
import http.server
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..'))
WEB = os.path.join(ROOT, 'web')
DATA = os.path.join(ROOT, 'src', 'Resources')


class Handler(http.server.SimpleHTTPRequestHandler):
    def translate_path(self, path):
        clean = path.split('?', 1)[0].split('#', 1)[0]
        if clean == '/favicon.ico':
            return os.path.join(DATA, 'app.ico')
        if clean.startswith('/data/'):
            rel = clean[len('/data/'):]
            return os.path.join(DATA, *[p for p in rel.split('/') if p not in ('', '.', '..')])
        return super().translate_path(path)

    def end_headers(self):
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
    handler = functools.partial(Handler, directory=WEB)
    with http.server.ThreadingHTTPServer(('127.0.0.1', port), handler) as srv:
        print('IRR Companion web: http://localhost:%d  (Ctrl+C to stop)' % port)
        srv.serve_forever()


if __name__ == '__main__':
    main()
