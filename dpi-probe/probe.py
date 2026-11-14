#!/usr/bin/env python3
"""Network reachability probe for diagnosing IP/SNI/volume-based blocking.

Run it from the network you want to test (e.g. a laptop tethered to the SIM,
or Termux on the phone) and compare with a second network (Wi-Fi).
Stages per target:  tcp -> tls(SNI) -> transfer (volume cut-off).
Stdlib only. Results are appended to results.csv for later comparison.

Examples:
  python3 probe.py --label tele2-moscow-4g 203.0.113.10 198.51.100.7
  python3 probe.py --sni ya.ru --sni www.microsoft.com --path /bytes/65536 HOST
"""
import argparse, csv, socket, ssl, sys, time, os

def stage_tcp(host, port, timeout):
    t = time.time()
    try:
        s = socket.create_connection((host, port), timeout=timeout)
        return s, "ok", round(time.time() - t, 3)
    except socket.timeout:
        return None, "timeout", timeout
    except ConnectionResetError:
        return None, "reset", round(time.time() - t, 3)
    except OSError as e:
        return None, f"error:{e.errno}", round(time.time() - t, 3)

def stage_tls(sock, host, sni, timeout):
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    sock.settimeout(timeout)
    t = time.time()
    try:
        tls = ctx.wrap_socket(sock, server_hostname=sni)
        return tls, "ok", round(time.time() - t, 3)
    except socket.timeout:
        return None, "timeout", timeout
    except (ConnectionResetError, ssl.SSLError, OSError) as e:
        return None, f"{type(e).__name__}", round(time.time() - t, 3)

def stage_transfer(tls, sni, path, limit, timeout):
    """Request `path` and count bytes until stall/close. Reveals ~16KB-style cut-offs."""
    tls.settimeout(timeout)
    req = f"GET {path} HTTP/1.1\r\nHost: {sni}\r\nConnection: close\r\n\r\n".encode()
    got, t = 0, time.time()
    try:
        tls.sendall(req)
        while got < limit:
            chunk = tls.recv(16384)
            if not chunk:
                return got, "closed"
            got += len(chunk)
        return got, "complete"
    except socket.timeout:
        return got, "stalled"
    except (ConnectionResetError, ssl.SSLError, OSError) as e:
        return got, f"{type(e).__name__}"

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("hosts", nargs="+")
    ap.add_argument("--port", type=int, default=443)
    ap.add_argument("--sni", action="append", help="SNI to present (repeatable). Default: no-SNI-change = host")
    ap.add_argument("--path", default="/", help="path to request for the transfer test")
    ap.add_argument("--limit", type=int, default=65536)
    ap.add_argument("--timeout", type=float, default=6.0)
    ap.add_argument("--label", default="unlabelled", help="network/region label, e.g. tele2-spb-4g")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "results.csv"))
    a = ap.parse_args()

    new = not os.path.exists(a.out)
    with open(a.out, "a", newline="") as f:
        w = csv.writer(f)
        if new:
            w.writerow(["ts", "label", "host", "port", "sni", "tcp", "tcp_s", "tls", "tls_s", "bytes", "transfer"])
        for host in a.hosts:
            for sni in (a.sni or [host]):
                s, tcp, tcp_s = stage_tcp(host, a.port, a.timeout)
                tls_r, tls_s, nbytes, xfer = "-", "-", "-", "-"
                if s:
                    t, tls_r, tls_s = stage_tls(s, host, sni, a.timeout)
                    if t:
                        nbytes, xfer = stage_transfer(t, sni, a.path, a.limit, a.timeout)
                        t.close()
                    else:
                        s.close()
                row = [int(time.time()), a.label, host, a.port, sni, tcp, tcp_s, tls_r, tls_s, nbytes, xfer]
                w.writerow(row)
                print(f"{host}:{a.port} sni={sni:<22} tcp={tcp:<8} tls={tls_r:<14} bytes={nbytes} {xfer}")

    print("\nReading the result:")
    print("  tcp=timeout            -> IP/CIDR dropped (whitelist or null-route). DPI tricks can't help.")
    print("  tcp=ok, tls=timeout    -> SNI/handshake filtering, try other SNI / Reality.")
    print("  tcp=ok, tls=Reset      -> DPI injected RST on this SNI.")
    print("  tls=ok, transfer=stalled at ~14-25KB -> volume cut-off on that destination.")

if __name__ == "__main__":
    sys.exit(main())
