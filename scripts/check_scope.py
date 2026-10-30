"""Fails a submission pull request that touches anything outside its own entry, picture and donation claim.

Used by validate-sponsors for branches named submission/*. Maintainers' own branches are not restricted.

    python scripts/check_scope.py <base-ref> <head-ref-name>
"""
import subprocess
import sys

ALLOWED_PREFIXES = ("data/entries/", "data/donations/", "data/images/")


def disallowed(changed_paths):
    return [p for p in changed_paths if not p.startswith(ALLOWED_PREFIXES)]


def main(argv):
    base, head_name = argv[1], argv[2]
    if not head_name.startswith("submission/"):
        print("not a submission branch: scope check skipped")
        return 0
    result = subprocess.run(["git", "diff", "--name-only", f"{base}...HEAD"], capture_output=True, text=True, check=True)
    bad = disallowed([line.strip() for line in result.stdout.splitlines() if line.strip()])
    for path in bad:
        print(f"{path}: a submission may only change data/entries, data/donations and data/images")
    print("OK" if not bad else f"{len(bad)} file(s) outside the allowed folders")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
