"""Validate supplemental metadata without changing legacy listings."""
import copy
import json
from pathlib import Path
import unittest
from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]
VALIDATOR = Draft202012Validator(json.loads((ROOT / "schemas/downloads.schema.json").read_text()))


def validate(data, check_listings=False):
    VALIDATOR.validate(data)
    if len(json.dumps(data).encode()) > 128 * 1024:
        raise ValueError("Download index exceeds 128 KiB")
    ids = [item["id"] for item in data["downloads"]]
    if len(ids) != len(set(ids)):
        raise ValueError("Duplicate download id")
    if check_listings:
        entries = [json.loads((ROOT / path).read_text(encoding="utf-8")) for path in json.loads((ROOT / "index.json").read_text())["entries"]]
        for item in data["downloads"]:
            matches = [e for e in entries if e["id"] == item["id"] and e["version"] == item["version"]]
            if len(matches) != 1 or matches[0].get("download") is not None or matches[0].get("reviewStatus") != "listed":
                raise ValueError("Supplement requires one listed identity without a legacy download")


class DownloadTests(unittest.TestCase):
    def test_public_sidecar(self):
        validate(json.loads((ROOT / "downloads.json").read_text()), True)

    def test_bounds_and_identity(self):
        data = {"schemaVersion": 1, "downloads": [{"id": "example.creation", "version": "1.0.0", "download": {"url": "https://cdn.sidequestvr.com/file/1/a.unitypackage", "byteLength": 93245650, "sha256": "a" * 64}}]}
        validate(data)
        for key, value in [("byteLength", 268435457), ("byteLength", 0), ("sha256", "bad"), ("url", "https://evil.test/a.unitypackage"), ("url", "https://cdn.sidequestvr.com/file/1/a.unitypackage?x=1")]:
            bad = copy.deepcopy(data)
            bad["downloads"][0]["download"][key] = value
            with self.assertRaises(Exception):
                validate(bad)
        data["downloads"].append(copy.deepcopy(data["downloads"][0]))
        with self.assertRaises(ValueError):
            validate(data)


if __name__ == "__main__":
    unittest.main()
