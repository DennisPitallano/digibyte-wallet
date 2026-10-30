"""Fixture checks for the validate-sponsors and build-sponsors-index scripts. Run with: python -m unittest discover -s tests"""
import json
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))

import build_index  # noqa: E402
import sponsor_data as sd  # noqa: E402
import validate  # noqa: E402

TX = "a" * 64


def png(width, height, pad_to=0):
    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + b"\x10\x20\x30" * width for _ in range(height))
    data = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))
    if len(data) < pad_to:  # pad with an ancillary chunk so the picture stays a valid PNG but gets big
        data = data[:-12] + chunk(b"tEXt", b"k\x00" + b"x" * (pad_to - len(data))) + data[-12:]
    return data


def jpeg(width, height):
    return b"\xff\xd8\xff\xc0" + struct.pack(">HBHHB", 17, 8, height, width, 3) + b"\x00" * 12 + b"\xff\xd9"


def webp_vp8x(width, height):
    body = b"WEBPVP8X" + struct.pack("<I", 10) + b"\x00\x00\x00\x00" + (width - 1).to_bytes(3, "little") + (height - 1).to_bytes(3, "little")
    return b"RIFF" + struct.pack("<I", len(body)) + body


class Repo:
    """A throwaway branch layout on disk."""

    def __init__(self):
        self.dir = tempfile.TemporaryDirectory()
        self.root = Path(self.dir.name)
        for sub in ("entries", "images", "donations"):
            (self.root / "data" / sub).mkdir(parents=True)

    def add(self, name="Alice", link="https://alice.example", image=None, txid=TX, claim=True, entry_id=None, **extra):
        image_bytes = image[1] if image else b""
        eid = entry_id or sd.entry_id(name, link, image_bytes)
        entry = {"id": eid, "name": name}
        if image:
            entry["image"] = image[0]
            (self.root / "data" / "images" / image[0]).write_bytes(image_bytes)
        if link:
            entry["link"] = link
        entry.update(extra)
        (self.root / "data" / "entries" / f"{eid}.json").write_text(json.dumps(entry), encoding="utf-8")
        if claim:
            (self.root / "data" / "donations" / f"{txid}.txt").write_text(eid + "\n", encoding="utf-8")
        return eid

    def problems(self):
        return validate.validate(self.root)

    def cleanup(self):
        self.dir.cleanup()


class ValidateTests(unittest.TestCase):
    def setUp(self):
        self.repo = Repo()
        self.addCleanup(self.repo.cleanup)

    def assertProblem(self, text):
        found = self.repo.problems()
        self.assertTrue(any(text in p for p in found), f"expected a problem containing {text!r}, got {found}")

    def test_valid_sample_passes(self):
        self.repo.add(image=("alice.png", png(64, 64)))
        self.assertEqual(self.repo.problems(), [])

    def test_name_only_entry_passes(self):
        self.repo.add(name="Solo", link=None)
        self.assertEqual(self.repo.problems(), [])

    def test_empty_branch_passes(self):
        self.assertEqual(self.repo.problems(), [])

    def test_oversized_image_fails(self):
        self.repo.add(image=("big.png", png(64, 64, pad_to=sd.IMAGE_MAX_BYTES + 5000)))
        self.assertProblem("larger than 200 KB")

    def test_image_over_512_fails(self):
        self.repo.add(image=("wide.png", png(600, 600)))
        self.assertProblem("larger than 512x512")

    def test_image_exactly_512_passes(self):
        self.repo.add(image=("ok.png", png(512, 512)))
        self.assertEqual(self.repo.problems(), [])

    def test_duplicate_entries_fail(self):
        self.repo.add(name="Alice", link="https://a.example", txid="b" * 64, entry_id="111111111111")
        self.repo.add(name="alice", link="https://a.example", txid="c" * 64, entry_id="222222222222")
        self.assertProblem("duplicate of")

    def test_same_name_different_link_is_not_a_duplicate(self):
        self.repo.add(name="Alice", link="https://a.example", txid="b" * 64)
        self.repo.add(name="Alice", link="https://b.example", txid="c" * 64)
        self.assertEqual(self.repo.problems(), [])

    def test_http_link_fails(self):
        self.repo.add(link="http://insecure.example")
        self.assertProblem("https URL")

    def test_link_with_credentials_fails(self):
        self.repo.add(link="https://user:pw@example.com")
        self.assertProblem("https URL")

    def test_link_over_200_fails(self):
        self.repo.add(link="https://example.com/" + "a" * 200)
        self.assertProblem("https URL")

    def test_svg_name_fails(self):
        self.repo.add(image=("logo.svg", b"<svg xmlns='http://www.w3.org/2000/svg'/>"))
        self.assertProblem("plain png/jpg/jpeg/webp file name")

    def test_svg_content_with_png_name_fails(self):
        self.repo.add(image=("logo.png", b"<svg xmlns='http://www.w3.org/2000/svg'/>"))
        self.assertProblem("not a PNG, JPEG or WebP")

    def test_png_content_with_jpg_name_fails(self):
        self.repo.add(image=("pic.jpg", png(64, 64)))
        self.assertProblem("contents are png but the name says jpg")

    def test_missing_image_file_fails(self):
        eid = self.repo.add()
        path = self.repo.root / "data" / "entries" / f"{eid}.json"
        entry = json.loads(path.read_text())
        entry["image"] = "ghost.png"
        path.write_text(json.dumps(entry))
        self.assertProblem("referenced by an entry but missing")

    def test_unreferenced_image_fails(self):
        self.repo.add()
        (self.repo.root / "data" / "images" / "stray.png").write_bytes(png(8, 8))
        self.assertProblem("not referenced by any entry")

    def test_entry_without_claim_fails(self):
        self.repo.add(claim=False)
        self.assertProblem("has no donation claim file")

    def test_claim_of_a_removed_entry_is_allowed(self):
        # A removal deletes the entry and its picture but keeps the claim, so the donation can never be reused.
        self.repo.add()
        (self.repo.root / "data" / "donations" / ("d" * 64 + ".txt")).write_text("ffffffffffff\n")
        self.assertEqual(self.repo.problems(), [])

    def test_repo_with_only_a_leftover_claim_is_valid(self):
        (self.repo.root / "data" / "donations" / ("d" * 64 + ".txt")).write_text("ffffffffffff\n")
        self.assertEqual(self.repo.problems(), [])

    def test_claim_with_a_malformed_entry_id_fails(self):
        self.repo.add()
        (self.repo.root / "data" / "donations" / ("d" * 64 + ".txt")).write_text("not-an-id\n")
        self.assertProblem("12-character lowercase hex entry id")

    def test_two_claims_for_one_entry_fail(self):
        eid = self.repo.add()
        (self.repo.root / "data" / "donations" / ("e" * 64 + ".txt")).write_text(eid + "\n")
        self.assertProblem("2 donation claim files")

    def test_bad_claim_file_name_fails(self):
        self.repo.add()
        (self.repo.root / "data" / "donations" / "not-a-txid.txt").write_text("x")
        self.assertProblem("64-character lowercase hex")

    def test_id_must_equal_file_name(self):
        eid = self.repo.add()
        path = self.repo.root / "data" / "entries" / f"{eid}.json"
        entry = json.loads(path.read_text())
        entry["id"] = "000000000000"
        path.write_text(json.dumps(entry))
        self.assertProblem("id must equal the file name")

    def test_unknown_fields_fail(self):
        self.repo.add(amount=500, tier="gold")
        self.assertProblem("unknown fields")

    def test_control_characters_in_name_fail(self):
        self.repo.add(name="Bad\u0007Name")
        self.assertProblem("name must be 1-60")

    def test_long_name_fails(self):
        self.repo.add(name="x" * 61)
        self.assertProblem("name must be 1-60")


class ImageSizeTests(unittest.TestCase):
    def test_reads_png_jpeg_webp_dimensions(self):
        self.assertEqual(sd.image_size(png(40, 30)), (40, 30))
        self.assertEqual(sd.image_size(jpeg(120, 80)), (120, 80))
        self.assertEqual(sd.image_size(webp_vp8x(300, 200)), (300, 200))

    def test_garbage_has_no_size(self):
        self.assertIsNone(sd.image_size(b"not an image"))


class BuildIndexTests(unittest.TestCase):
    def setUp(self):
        self.repo = Repo()
        self.addCleanup(self.repo.cleanup)

    def test_orders_oldest_first_by_added_time_then_id(self):
        a = self.repo.add(name="A", link=None, txid="1" * 64)
        b = self.repo.add(name="B", link=None, txid="2" * 64)
        c = self.repo.add(name="C", link=None, txid="3" * 64)
        times = {a: 300, b: 100, c: 100}
        index = build_index.build(self.repo.root, added_at=lambda root, rel: times[Path(rel).stem])
        order = [s["id"] for s in index["sponsors"]]
        tied = sorted([b, c])
        self.assertEqual(order, tied + [a])

    def test_output_has_version_and_only_public_fields(self):
        eid = self.repo.add(image=("alice.png", png(8, 8)))
        index = build_index.build(self.repo.root, added_at=lambda r, p: 1)
        self.assertEqual(index["version"], 1)
        entry = index["sponsors"][0]
        self.assertEqual(set(entry), {"id", "name", "image", "link"})
        text = build_index.render(index)
        self.assertNotIn(TX, text)  # the donation transaction is never copied into the public list

    def test_rendering_is_stable(self):
        self.repo.add()
        first = build_index.render(build_index.build(self.repo.root, added_at=lambda r, p: 1))
        second = build_index.render(build_index.build(self.repo.root, added_at=lambda r, p: 1))
        self.assertEqual(first, second)
        self.assertTrue(first.endswith("\n"))

    def test_empty_branch_gives_empty_list(self):
        index = build_index.build(self.repo.root, added_at=lambda r, p: 1)
        self.assertEqual(index, {"version": 1, "sponsors": []})

    def test_name_only_entry_has_no_image_or_link_keys(self):
        self.repo.add(name="Solo", link=None)
        entry = build_index.build(self.repo.root, added_at=lambda r, p: 1)["sponsors"][0]
        self.assertEqual(set(entry), {"id", "name"})


class ScopeTests(unittest.TestCase):
    def test_only_entry_picture_and_claim_paths_are_allowed(self):
        import check_scope
        ok = ["data/entries/a.json", "data/images/a.webp", "data/donations/" + TX + ".txt"]
        self.assertEqual(check_scope.disallowed(ok), [])
        bad = ["data/sponsors.json", "scripts/validate.py", ".github/workflows/x.yml", "README.md"]
        self.assertEqual(check_scope.disallowed(ok + bad), bad)


class ParityTests(unittest.TestCase):
    def test_entry_id_matches_the_wallet_and_api(self):
        # Same vector as SponsorRulesTests in the main repository (C#): name, link, picture bytes -> 12 hex characters.
        self.assertEqual(sd.entry_id("Alice", "https://a.example", b"\x01\x02\x03"), "861f32e0e299")
        self.assertEqual(sd.entry_id("alice", "https://a.example", b"\x01\x02\x03"), sd.entry_id("Alice", "https://a.example", b"\x01\x02\x03"))


if __name__ == "__main__":
    unittest.main()
