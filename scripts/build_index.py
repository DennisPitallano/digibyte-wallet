"""Regenerates data/sponsors.json from data/entries/*.json. Run by the build-sponsors-index workflow after every push.

The list is oldest first, ordered by when each entry file was first added to the branch (ties by id); the wallet shows the last entry first.
The file is generated: edit the entry files, never sponsors.json. Standard library only.

    python scripts/build_index.py [repo-root]
"""
import json
import subprocess
import sys
from pathlib import Path

import sponsor_data as sd


def added_at_from_git(root, relative_path):
    """Unix time of the commit that first added the file; 0 if git cannot tell (new, uncommitted file)."""
    result = subprocess.run(
        ["git", "log", "--diff-filter=A", "--format=%ct", "--", relative_path],
        cwd=root, capture_output=True, text=True, check=False,
    )
    times = [int(line) for line in result.stdout.split() if line.strip().isdigit()]
    return min(times) if times else 0


def build(root, added_at=added_at_from_git):
    root = Path(root)
    entries = sd.load_entries(root)
    donations = sd.count_donations(root)
    ordered = sorted(
        entries.items(),
        key=lambda item: (added_at(root, f"data/entries/{item[0]}.json"), item[0]),
    )
    sponsors = []
    for stem, entry in ordered:
        item = {"id": stem, "name": entry["name"]}
        if entry.get("image"):
            item["image"] = entry["image"]
        if entry.get("link"):
            item["link"] = entry["link"]
        # One claim file per verified donation; the wallet shows the count when it is more than one.
        if donations.get(stem, 0) > 1:
            item["donations"] = donations[stem]
        sponsors.append(item)
    return {"version": 1, "sponsors": sponsors[-sd.MAX_ENTRIES:]}


def render(index):
    return json.dumps(index, indent=2, ensure_ascii=False) + "\n"


def main(argv):
    root = Path(argv[1]) if len(argv) > 1 else Path(".")
    target = root / "data" / "sponsors.json"
    text = render(build(root))
    if target.exists() and target.read_text(encoding="utf-8") == text:
        print("sponsors.json is up to date")
        return 0
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8")
    print("sponsors.json regenerated")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
