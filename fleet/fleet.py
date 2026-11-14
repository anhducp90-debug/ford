#!/usr/bin/env python3
"""Spread users across several Xray (VLESS+Reality) nodes and generate configs.

Stdlib only. Run on YOUR machine; no credentials leave it.

  python3 fleet.py plan                       # allocate + write out/
  python3 fleet.py plan --dead n2             # node n2 died: move only its users
  python3 fleet.py plan --dead n2 --batch 5   # ...but move at most 5 users per run
  python3 fleet.py status                     # show load per node

Files:
  fleet.json   nodes + keys (SECRET, keep local)   see fleet.example.json
  users.csv    one user name per line (header: name)
  state.json   generated UUIDs + assignment (SECRET, keep local)
Output in out/:
  server-<node>.json   Xray config to install on that VPS
  users/<name>.txt     vless:// links, primary first then backup (import into Happ)
"""
import argparse, base64, csv, json, os, sys, uuid
from urllib.parse import quote

HERE = os.path.dirname(os.path.abspath(__file__))
P = lambda *a: os.path.join(HERE, *a)

def load(path, default=None):
    if not os.path.exists(path):
        if default is not None:
            return default
        sys.exit(f"missing {path}")
    with open(path) as f:
        return json.load(f)

def save(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        json.dump(obj, f, indent=2)
    os.chmod(path, 0o600)

def read_users():
    with open(P("users.csv"), newline="") as f:
        return [r["name"].strip() for r in csv.DictReader(f) if r.get("name", "").strip()]

def allocate(users, nodes, state, dead, batch):
    """Keep existing assignments; only (re)assign users whose primary/backup is dead or missing.
    Primary and backup are always different nodes. Balance by primary load, then backup load."""
    alive = [n["name"] for n in nodes if n["name"] not in dead]
    if len(alive) < 2:
        sys.exit("need at least 2 alive nodes (primary + backup)")
    cap = max(1, -(-len(users) // len(alive)))  # ceil(users/alive)
    cap = int(load(P("fleet.json")).get("cap", cap))
    assign = state.setdefault("assign", {})
    for gone in [u for u in assign if u not in users]:
        del assign[gone]                        # user removed
    pl = {n: 0 for n in alive}                  # primary load
    bl = {n: 0 for n in alive}                  # backup load
    pending = []
    for u in users:
        a = assign.get(u)
        if a and a["primary"] in alive and a["backup"] in alive:
            pl[a["primary"]] += 1; bl[a["backup"]] += 1
        else:
            pending.append(u)
    moved = 0
    for u in pending:
        a = assign.get(u)
        # a user with a still-alive primary keeps it; only the backup is refilled
        if a and a["primary"] in alive:
            prim = a["primary"]; pl[prim] += 1
        else:
            if batch and moved >= batch:
                continue                         # leave for the next run
            prim = min(alive, key=lambda n: (pl[n], n)); pl[prim] += 1; moved += 1
        back = min((n for n in alive if n != prim), key=lambda n: (bl[n], pl[n], n))
        bl[back] += 1
        assign[u] = {"primary": prim, "backup": back}
    if pl and max(pl.values()) > cap:
        print(f"warning: a node exceeds cap {cap}: {pl}", file=sys.stderr)
    return pl, bl, [u for u in users if u not in assign]

def xray_server(node, clients):
    return {
        "log": {"loglevel": "warning"},
        "inbounds": [{
            "listen": "0.0.0.0", "port": node.get("port", 443), "protocol": "vless",
            "settings": {"clients": [{"id": c["id"], "flow": "xtls-rprx-vision", "email": c["name"]} for c in clients],
                         "decryption": "none"},
            "streamSettings": {"network": "tcp", "security": "reality",
                "realitySettings": {"show": False, "dest": node["dest"], "xver": 0,
                                    "serverNames": [node["sni"]], "privateKey": node["private_key"],
                                    "shortIds": [node["short_id"]]}},
            "sniffing": {"enabled": True, "destOverride": ["http", "tls"]}}],
        "outbounds": [{"protocol": "freedom", "tag": "direct"}, {"protocol": "blackhole", "tag": "block"}],
    }

def vless_link(node, uid, label):
    q = (f"encryption=none&flow=xtls-rprx-vision&security=reality&sni={node['sni']}"
         f"&fp=chrome&pbk={node['public_key']}&sid={node['short_id']}&type=tcp")
    return f"vless://{uid}@{node['host']}:{node.get('port', 443)}?{q}#{quote(label)}"

def cmd_plan(a):
    fleet = load(P("fleet.json")); nodes = fleet["nodes"]
    by = {n["name"]: n for n in nodes}
    for d in a.dead:
        if d not in by: sys.exit(f"unknown node {d}")
    users = read_users()
    state = load(P("state.json"), {"uuids": {}, "assign": {}})
    for u in users:
        state["uuids"].setdefault(u, str(uuid.uuid4()))   # one UUID per user = revocable
    pl, bl, waiting = allocate(users, nodes, state, set(a.dead), a.batch)
    save(P("state.json"), state)
    # server configs: node must know users that use it as primary OR backup
    for n in nodes:
        if n["name"] in a.dead: continue
        cl = [{"id": state["uuids"][u], "name": u} for u, s in state["assign"].items()
              if n["name"] in (s["primary"], s["backup"])]
        save(P("out", f"server-{n['name']}.json"), xray_server(n, cl))
    for u, s in state["assign"].items():
        uid = state["uuids"][u]
        links = [vless_link(by[s["primary"]], uid, f"{u}-main"), vless_link(by[s["backup"]], uid, f"{u}-backup")]
        os.makedirs(P("out", "users"), exist_ok=True)
        with open(P("out", "users", f"{u}.txt"), "w") as f:
            f.write("\n".join(links) + "\n")
        os.chmod(P("out", "users", f"{u}.txt"), 0o600)
    print("primary load:", pl); print("backup  load:", bl)
    if waiting: print("not yet reassigned (batch limit):", ", ".join(waiting))
    print("wrote out/ (install server-<node>.json on each VPS, send users/<name>.txt to each person)")

def cmd_status(a):
    state = load(P("state.json"))
    cnt = {}
    for s in state["assign"].values():
        cnt.setdefault(s["primary"], [0, 0])[0] += 1
        cnt.setdefault(s["backup"], [0, 0])[1] += 1
    for n, (p, b) in sorted(cnt.items()): print(f"{n}: primary={p} backup={b}")

if __name__ == "__main__":
    ap = argparse.ArgumentParser(); sp = ap.add_subparsers(dest="c", required=True)
    p = sp.add_parser("plan"); p.add_argument("--dead", nargs="*", default=[]); p.add_argument("--batch", type=int, default=0)
    p.set_defaults(f=cmd_plan); s = sp.add_parser("status"); s.set_defaults(f=cmd_status)
    a = ap.parse_args(); a.f(a)
