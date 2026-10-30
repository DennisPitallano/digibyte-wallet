"""Checks the data on the sponsors branch. Run by the validate-sponsors workflow on every pull request.

Exit code 0 = valid. Prints one line per problem. Standard library only.

    python scripts/validate.py [repo-root]
"""
import json
import sys
from pathlib import Path

import sponsor_data as sd


def validate(root):
    root = Path(root)
    problems = []
    entries_dir = root / "data" / "entries"
    images_dir = root / "data" / "images"
    claims_dir = root / "data" / "donations"

    entries = {}
    for path in sorted(entries_dir.glob("*.json")) if entries_dir.exists() else []:
        try:
            entry = json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            problems.append(f"{path.name}: not valid JSON")
            continue
        for problem in sd.entry_problems(entry, path.stem):
            problems.append(f"entries/{path.name}: {problem}")
        entries[path.stem] = entry

    if len(entries) > sd.MAX_ENTRIES:
        problems.append(f"more than {sd.MAX_ENTRIES} entries")

    # Duplicates: same name (case-insensitive), link and image.
    seen = {}
    for stem, entry in sorted(entries.items()):
        if not isinstance(entry, dict):
            continue
        key = sd.duplicate_key(entry)
        if key in seen:
            problems.append(f"entries/{stem}.json: duplicate of {seen[key]}.json")
        else:
            seen[key] = stem

    # Pictures: referenced files exist; every picture is referenced; type, size and dimensions.
    referenced = {e["image"] for e in entries.values() if isinstance(e, dict) and isinstance(e.get("image"), str)}
    present = {p.name for p in images_dir.iterdir() if p.is_file() and p.name != ".gitkeep"} if images_dir.exists() else set()
    for name in sorted(referenced - present):
        problems.append(f"images/{name}: referenced by an entry but missing")
    for name in sorted(present - referenced):
        problems.append(f"images/{name}: not referenced by any entry")
    for name in sorted(present & referenced):
        data = (images_dir / name).read_bytes()
        kind = sd.detect_image_type(data)
        extension = name.rsplit(".", 1)[-1].lower().replace("jpeg", "jpg")
        if kind is None:
            problems.append(f"images/{name}: not a PNG, JPEG or WebP picture")
            continue
        if kind != extension:
            problems.append(f"images/{name}: contents are {kind} but the name says {extension}")
        if len(data) > sd.IMAGE_MAX_BYTES:
            problems.append(f"images/{name}: larger than 200 KB")
        size = sd.image_size(data)
        if size is None:
            problems.append(f"images/{name}: dimensions could not be read")
        elif size[0] > sd.IMAGE_MAX_SIDE or size[1] > sd.IMAGE_MAX_SIDE:
            problems.append(f"images/{name}: {size[0]}x{size[1]} is larger than 512x512")

    # Donation claims: one per entry, each naming an existing entry.
    claimed = {}
    for path in sorted(claims_dir.glob("*.txt")) if claims_dir.exists() else []:
        if not sd.TXID_RE.match(path.stem):
            problems.append(f"donations/{path.name}: file name must be a 64-character lowercase hex transaction ID")
            continue
        target = path.read_text(encoding="utf-8").strip()
        if target not in entries:
            problems.append(f"donations/{path.name}: names entry '{target}' which does not exist")
            continue
        claimed.setdefault(target, []).append(path.name)
    for stem in sorted(entries):
        count = len(claimed.get(stem, []))
        if count == 0:
            problems.append(f"entries/{stem}.json: has no donation claim file")
        elif count > 1:
            problems.append(f"entries/{stem}.json: has {count} donation claim files")

    return problems


def main(argv):
    root = argv[1] if len(argv) > 1 else "."
    problems = validate(root)
    for problem in problems:
        print(problem)
    print("OK" if not problems else f"{len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
