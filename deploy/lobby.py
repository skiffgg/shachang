#!/usr/bin/env python3
"""Tiny room directory for ShaChang.

Every match process posts a heartbeat; the client asks for the list. Rooms that stop
reporting disappear after ROOM_TTL seconds, so a crashed match cleans itself up.

    POST /heartbeat  {"port":7777,"players":3,"max":12,"mode":"tdm","state":"playing","name":"1 号房"}
    GET  /rooms      -> [{"host":"1.2.3.4","port":7777, ...}]

Environment:
    HOST  public address handed to clients (defaults to the machine's outbound IP)
    PORT  listen port (default 8080)
"""
import json, os, socket, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOM_TTL = 20.0
rooms = {}          # port -> dict


def public_host():
    if os.environ.get("HOST"):
        return os.environ["HOST"]
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("8.8.8.8", 80))
        return s.getsockname()[0]
    except Exception:
        return "127.0.0.1"
    finally:
        s.close()


HOST = public_host()


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def _send(self, code, payload):
        body = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path.startswith("/rooms"):
            now = time.time()
            live = [r for r in rooms.values() if now - r["seen"] < ROOM_TTL]
            self._send(200, [{k: v for k, v in r.items() if k != "seen"} for r in live])
        else:
            self._send(404, {"error": "not found"})

    def do_POST(self):
        if not self.path.startswith("/heartbeat"):
            return self._send(404, {"error": "not found"})
        try:
            n = int(self.headers.get("Content-Length", 0))
            d = json.loads(self.rfile.read(n) or b"{}")
            port = int(d.get("port", 0))
            if not port:
                return self._send(400, {"error": "port required"})
            rooms[port] = {
                "host": HOST,
                "port": port,
                "name": d.get("name") or ("房间 " + str(port)),
                "mode": d.get("mode", "tdm"),
                "players": int(d.get("players", 0)),
                "max": int(d.get("max", 12)),
                "state": d.get("state", "waiting"),
                "seen": time.time(),
            }
            self._send(200, {"ok": True})
        except Exception as e:
            self._send(400, {"error": str(e)})

    def log_message(self, *a):     # keep the journal quiet
        pass


if __name__ == "__main__":
    port = int(os.environ.get("PORT", 8080))
    print("lobby on %s:%d, advertising host %s" % ("0.0.0.0", port, HOST), flush=True)
    ThreadingHTTPServer(("0.0.0.0", port), Handler).serve_forever()
