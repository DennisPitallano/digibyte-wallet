"""Peer crawler for specs/011-p2p-spv-gateway (research R1/R2, tasks T005 and T007).

Resolves the real DigiByte DNS seeds, asks several peers for addresses, probes up to LIMIT addresses with a version
handshake, and prints how many peers advertise the full-history, compact-filter and bloom service bits.
Run from this folder:  python crawl.py [limit]
Uses only the Python standard library. Nothing is sent except a version message and getaddr.
"""
import socket, struct, sys, time, random, hashlib, concurrent.futures as cf
from collections import Counter

MAGIC = bytes.fromhex("fac3b6da")  # 0xDAB6C3FA, little-endian on the wire
SEEDS = ["seed.digibyte.io", "seed.diginode.tools", "seed.digibyte.link", "seed.aroundtheblock.app", "seed.tuyul.cc"]
LIMIT = int(sys.argv[1]) if len(sys.argv) > 1 else 400


def checksum(b): return hashlib.sha256(hashlib.sha256(b).digest()).digest()[:4]
def msg(cmd, payload=b""): return MAGIC + cmd.ljust(12, b"\0") + struct.pack("<I", len(payload)) + checksum(payload) + payload
def netaddr(): return struct.pack("<Q", 0) + b"\0" * 10 + b"\xff\xff" + b"\0\0\0\0" + struct.pack(">H", 0)


def version_msg():
    p = struct.pack("<iQq", 70019, 0, int(time.time())) + netaddr() + netaddr()
    p += struct.pack("<Q", random.getrandbits(64)) + bytes([7]) + b"/probe/" + struct.pack("<i", 0) + b"\x01"
    return msg(b"version", p)


def recv_exact(s, n):
    b = b""
    while len(b) < n:
        c = s.recv(n - len(b))
        if not c:
            raise EOFError
        b += c
    return b


def read_msg(s):
    h = recv_exact(s, 24)
    ln = struct.unpack("<I", h[16:20])[0]
    return h[4:16].rstrip(b"\0"), recv_exact(s, ln)


def parse_version(p):
    ver, svc = struct.unpack("<iQ", p[:12])
    off = 20 + 26 + 26 + 8
    n = p[off]
    ua = p[off + 1:off + 1 + n].decode(errors="replace")
    height = struct.unpack("<i", p[off + 1 + n:off + 5 + n])[0]
    return ver, svc, ua, height


def probe(ip, port=12024, timeout=6):
    s = socket.create_connection((ip, port), timeout=timeout)
    s.settimeout(timeout)
    try:
        s.sendall(version_msg())
        for _ in range(6):
            c, p = read_msg(s)
            if c == b"version":
                return parse_version(p)
    finally:
        s.close()


def get_addrs(ip, port=12024):
    out = []
    s = socket.create_connection((ip, port), timeout=6)
    s.settimeout(6)
    try:
        s.sendall(version_msg())
        t = time.time()
        while time.time() - t < 12:
            c, p = read_msg(s)
            if c == b"version":
                s.sendall(msg(b"verack"))
                s.sendall(msg(b"getaddr"))
            elif c == b"ping":
                s.sendall(msg(b"pong", p))
            elif c == b"addr":
                n, o = p[0], 1
                if n >= 0xfd:
                    n, o = struct.unpack("<H", p[1:3])[0], 3
                for i in range(n):
                    rec = p[o + i * 30:o + (i + 1) * 30]
                    if len(rec) < 30:
                        break
                    a = rec[12:28]
                    port_ = struct.unpack(">H", rec[28:30])[0]
                    if a[:12] == b"\0" * 10 + b"\xff\xff":
                        out.append((socket.inet_ntoa(a[12:]), port_))
                if len(out) > 300:
                    break
    except Exception:
        pass
    finally:
        s.close()
    return out


def main():
    ips = set()
    for h in SEEDS:
        try:
            r = {x[4][0] for x in socket.getaddrinfo(h, 12024, socket.AF_INET, socket.SOCK_STREAM)}
            print(f"seed {h}: {len(r)} addresses")
            ips |= r
        except Exception as e:
            print(f"seed {h}: DNS failed ({e})")
    seen = {(ip, 12024) for ip in ips}
    # ask a few seed peers for more addresses
    for ip in list(ips)[:6]:
        seen |= set(get_addrs(ip))
    print("candidate addresses:", len(seen))
    targets = list(seen)[:LIMIT]
    results = []

    def pr(a):
        try:
            return a, probe(a[0], a[1])
        except Exception:
            return a, None

    with cf.ThreadPoolExecutor(40) as ex:
        for a, r in ex.map(pr, targets):
            if r:
                results.append((a, r))
    n = len(results)
    print(f"reachable: {n} of {len(targets)} probed")
    if not n:
        return
    full = sum(1 for _, r in results if r[1] & 1)
    limited = sum(1 for _, r in results if r[1] & 0x400)
    witness = sum(1 for _, r in results if r[1] & 8)
    filt = sum(1 for _, r in results if r[1] & 64)
    bloom = sum(1 for _, r in results if r[1] & 4)
    v2 = sum(1 for _, r in results if r[1] & 0x800)
    print(f"full history (NETWORK bit): {full}")
    print(f"limited only (no NETWORK):  {n - full}")
    print(f"witness: {witness}, compact filters: {filt}, bloom: {bloom}, v2 transport: {v2}")
    print("user agents:", Counter(r[2] for _, r in results).most_common(8))
    print("service bit sets:", Counter(hex(r[1]) for _, r in results).most_common(6))
    heights = [r[3] for _, r in results]
    print("reported heights: min", min(heights), "max", max(heights))


if __name__ == "__main__":
    main()
