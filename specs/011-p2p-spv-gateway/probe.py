import socket, struct, hashlib, time, random, sys
MAGIC = bytes.fromhex("fac3b6da")  # 0xDAB6C3FA little-endian on the wire
def sha(b): return hashlib.sha256(hashlib.sha256(b).digest()).digest()[:4]
def msg(cmd, payload=b""):
    return MAGIC + cmd.ljust(12, b"\0") + struct.pack("<I", len(payload)) + sha(payload) + payload
def addr(): return struct.pack("<Q", 0) + b"\0"*10 + b"\xff\xff" + b"\0\0\0\0" + struct.pack(">H", 0)
def version():
    p = struct.pack("<iQq", 70017, 0, int(time.time())) + addr() + addr()
    p += struct.pack("<Q", random.getrandbits(64)) + bytes([len(b"/probe/")]) + b"/probe/" + struct.pack("<i", 0) + b"\x01"
    return msg(b"version", p)
def recv_exact(s, n):
    b = b""
    while len(b) < n:
        c = s.recv(n - len(b))
        if not c: raise EOFError
        b += c
    return b
def read_msg(s):
    h = recv_exact(s, 24)
    ln = struct.unpack("<I", h[16:20])[0]
    return h[4:16].rstrip(b"\0"), recv_exact(s, ln)
def probe(ip, port=12024):
    s = socket.create_connection((ip, port), timeout=6); s.settimeout(6)
    s.sendall(version())
    for _ in range(6):
        c, p = read_msg(s)
        if c == b"version":
            ver, svc = struct.unpack("<iQ", p[:12])
            off = 20 + 26 + 26 + 8
            n = p[off]; ua = p[off+1:off+1+n].decode(errors="replace")
            h = struct.unpack("<i", p[off+1+n:off+5+n])[0]
            s.close(); return ver, svc, ua, h
    s.close()
hosts = sys.argv[1:]
ips = set()
for h in hosts:
    try:
        for r in socket.getaddrinfo(h, 12024, socket.AF_INET, socket.SOCK_STREAM): ips.add(r[4][0])
    except Exception as e: print("dns fail", h, e)
print(len(ips), "ips")
ok = 0; cf = 0; ua_count = {}
for ip in list(ips)[:40]:
    try:
        r = probe(ip)
        if not r: continue
        ver, svc, ua, h = r; ok += 1
        has = bool(svc & (1<<6)); cf += has
        ua_count[ua] = ua_count.get(ua,0)+1
        print(ip, "ver", ver, "svc", hex(svc), "CF" if has else "--", "bf" if svc&4 else "", "wit" if svc&8 else "", "ltd" if svc&(1<<10) else "", ua, "h", h)
    except Exception as e:
        print(ip, "fail", type(e).__name__)
print("reachable", ok, "with compact filters", cf); print(ua_count)
