#!/usr/bin/env python3
"""Add/replace a version entry in a Jellyfin plugin repository manifest (manifest.json)."""
import json
import os
import sys
from datetime import datetime, timezone

import yaml  # pip install pyyaml

manifest_path = os.environ["MANIFEST_PATH"]
meta = yaml.safe_load(open(os.environ.get("BUILD_YAML", "build.yaml"), encoding="utf-8"))

entry = {
    "version": os.environ["VERSION"],
    "changelog": os.environ.get("CHANGELOG") or "No changelog provided.",
    "targetAbi": str(meta["targetAbi"]),
    "sourceUrl": os.environ["SOURCE_URL"],
    "checksum": os.environ["CHECKSUM"],
    "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
}

manifest = []
if os.path.exists(manifest_path):
    with open(manifest_path, encoding="utf-8") as f:
        manifest = json.load(f)

plugin = next((p for p in manifest if p.get("guid", "").lower() == meta["guid"].lower()), None)
if plugin is None:
    plugin = {"guid": meta["guid"], "versions": []}
    manifest.append(plugin)

# Keep descriptive fields in sync with build.yaml
plugin.update(
    {
        "name": meta["name"],
        "description": " ".join(str(meta.get("description", "")).split()),
        "overview": meta.get("overview", ""),
        "owner": meta.get("owner", ""),
        "category": meta.get("category", "General"),
    }
)
if os.environ.get("IMAGE_URL"):
    plugin["imageUrl"] = os.environ["IMAGE_URL"]

# Replace an existing entry for the same version, newest first
versions = [v for v in plugin.get("versions", []) if v.get("version") != entry["version"]]
versions.insert(0, entry)
versions.sort(key=lambda v: tuple(int(x) for x in v["version"].split(".")), reverse=True)
plugin["versions"] = versions

with open(manifest_path, "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")

print(f"manifest.json now lists {len(versions)} version(s); latest {versions[0]['version']}", file=sys.stderr)
