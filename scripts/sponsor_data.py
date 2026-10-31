"""Shared rules for the sponsors branch: the same limits the wallet and the submission API enforce.

Everything here is standard library only. See specs/006-sponsors-page/data-model.md on the main branch of
DennisPitallano/digibyte-wallet for the source of these rules.
"""
import hashlib
import json
import re
import struct
from pathlib import Path

NAME_MAX = 60
LINK_MAX = 200
IMAGE_MAX_BYTES = 200 * 1024
IMAGE_MAX_SIDE = 512
MAX_ENTRIES = 1000

ID_RE = re.compile(r"^[a-f0-9]{12}$")
TXID_RE = re.compile(r"^[a-f0-9]{64}$")
IMAGE_NAME_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,63}\.(png|jpe?g|webp)$")
LINK_RE = re.compile(r"^https://[^\s/@]+(/[^\s]*)?$")
ENTRY_FIELDS = {"id", "name", "image", "link"}


def has_control_chars(text):
    return any(ord(c) < 32 or ord(c) == 127 or 0x80 <= ord(c) < 0xA0 for c in text)


def detect_image_type(data):
    """Returns 'png', 'jpg', 'webp' or None, from the signature bytes (never from the file name)."""
    if data[:8] == b"\x89PNG\r\n\x1a\n":
        return "png"
    if data[:3] == b"\xff\xd8\xff":
        return "jpg"
    if len(data) >= 12 and data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "webp"
    return None


def image_size(data):
    """Returns (width, height) for PNG, JPEG or WebP, or None if it cannot be read."""
    kind = detect_image_type(data)
    try:
        if kind == "png":
            return struct.unpack(">II", data[16:24])
        if kind == "jpg":
            i = 2
            while i + 9 < len(data):
                if data[i] != 0xFF:
                    i += 1
                    continue
                marker = data[i + 1]
                if marker in (0xD8, 0x01) or 0xD0 <= marker <= 0xD7:
                    i += 2
                    continue
                length = struct.unpack(">H", data[i + 2:i + 4])[0]
                if 0xC0 <= marker <= 0xCF and marker not in (0xC4, 0xC8, 0xCC):
                    h, w = struct.unpack(">HH", data[i + 5:i + 9])
                    return (w, h)
                i += 2 + length
            return None
        if kind == "webp":
            chunk = data[12:16]
            if chunk == b"VP8X":
                w = 1 + int.from_bytes(data[24:27], "little")
                h = 1 + int.from_bytes(data[27:30], "little")
                return (w, h)
            if chunk == b"VP8 ":
                w, h = struct.unpack("<HH", data[26:30])
                return (w & 0x3FFF, h & 0x3FFF)
            if chunk == b"VP8L":
                bits = int.from_bytes(data[21:25], "little")
                return ((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1)
    except (struct.error, IndexError):
        return None
    return None


def entry_problems(entry, stem):
    """Rule violations for one entry file's parsed JSON; an empty list means the entry is valid."""
    problems = []
    if not isinstance(entry, dict):
        return ["entry is not an object"]
    extra = set(entry) - ENTRY_FIELDS
    if extra:
        problems.append("unknown fields: " + ", ".join(sorted(extra)))
    if not isinstance(entry.get("id"), str) or not ID_RE.match(entry["id"]):
        problems.append("id must be 12 lowercase hex characters")
    elif entry["id"] != stem:
        problems.append("id must equal the file name")
    name = entry.get("name")
    if not isinstance(name, str) or not (1 <= len(name.strip()) <= NAME_MAX) or has_control_chars(name):
        problems.append("name must be 1-60 characters with no control characters")
    if "image" in entry and (not isinstance(entry["image"], str) or not IMAGE_NAME_RE.match(entry["image"])):
        problems.append("image must be a plain png/jpg/jpeg/webp file name")
    if "link" in entry and (
        not isinstance(entry["link"], str) or len(entry["link"]) > LINK_MAX or not LINK_RE.match(entry["link"])
    ):
        problems.append("link must be an https URL of at most 200 characters without credentials")
    return problems


def duplicate_key(entry):
    return (entry.get("name", "").strip().lower(), entry.get("link", ""), entry.get("image", ""))


def load_entries(root):
    """Returns {stem: parsed entry} for data/entries/*.json under root (a Path)."""
    entries = {}
    folder = Path(root) / "data" / "entries"
    if folder.exists():
        for path in sorted(folder.glob("*.json")):
            entries[path.stem] = json.loads(path.read_text(encoding="utf-8"))
    return entries


def count_donations(root):
    """Returns {entry id: number of claim files naming it} for data/donations/*.txt under root (a Path)."""
    counts = {}
    folder = Path(root) / "data" / "donations"
    if folder.exists():
        for path in sorted(folder.glob("*.txt")):
            target = path.read_text(encoding="utf-8").strip()
            if ID_RE.match(target):
                counts[target] = counts.get(target, 0) + 1
    return counts


def entry_id(name, link, image_bytes=b""):
    """Same id the wallet and the API compute: first 12 hex of SHA-256(lower-cased name, link, picture bytes)."""
    head = (name.strip().lower() + "\n" + (link or "") + "\n").encode("utf-8")
    return hashlib.sha256(head + image_bytes).hexdigest()[:12]
