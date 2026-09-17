"""Validate the optional preview sidecar without changing package metadata."""
import copy
import json
from pathlib import Path
import unittest
from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]
VALIDATOR = Draft202012Validator(json.loads((ROOT / "schemas/media.schema.json").read_text()))


def validate(data):
    VALIDATOR.validate(data)
    if len(json.dumps(data).encode()) > 256 * 1024:
        raise ValueError("Media index exceeds 256 KiB")
    ids = [gallery["id"] for gallery in data["galleries"]]
    if len(ids) != len(set(ids)):
        raise ValueError("Duplicate gallery id")


class MediaTests(unittest.TestCase):
    def test_public_and_examples(self):
        for name in ["media.json", "examples/six-image-gallery.json", "examples/animated-gallery.json"]:
            validate(json.loads((ROOT / name).read_text()))

    def test_invalid_media(self):
        base = json.loads((ROOT / "examples/animated-gallery.json").read_text())
        for url in ["https://evil.test/a.gif", "assets/../a.gif", "https://cdn.sidequestvr.com/file/1/a.gif?x=1", "assets/example/a.svg"]:
            data = copy.deepcopy(base)
            data["galleries"][0]["items"][0]["url"] = url
            with self.assertRaises(Exception):
                validate(data)
        del base["galleries"][0]["items"][0]["poster"]
        with self.assertRaises(Exception):
            validate(base)

    def test_gallery_bounds(self):
        data = json.loads((ROOT / "examples/six-image-gallery.json").read_text())
        data["galleries"].append(copy.deepcopy(data["galleries"][0]))
        with self.assertRaises(ValueError):
            validate(data)
        data["galleries"].pop()
        data["galleries"][0]["items"] *= 2
        with self.assertRaises(Exception):
            validate(data)


if __name__ == "__main__":
    unittest.main()
