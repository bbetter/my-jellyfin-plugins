#!/usr/bin/env python3
"""Add or replace one version entry in the registry manifest.json.

Usage:
    update_manifest.py <manifest.json> <meta.json> <sourceUrl> <checksum>

meta.json is the file packaged inside the plugin zip. Its keys (guid, name,
version, targetAbi, overview, description, owner, category) become the plugin
entry and version entry in the manifest.
"""
import json
import sys
from datetime import datetime, timezone

manifest_path, meta_path, source_url, checksum = sys.argv[1:5]

with open(meta_path, encoding="utf-8") as f:
    meta = json.load(f)

with open(manifest_path, encoding="utf-8") as f:
    manifest = json.load(f)

plugin = next((p for p in manifest if p.get("guid") == meta["guid"]), None)
if plugin is None:
    plugin = {
        "guid": meta["guid"],
        "name": meta["name"],
        "description": meta["description"],
        "overview": meta["overview"],
        "owner": meta["owner"],
        "category": meta["category"],
        "versions": [],
    }
    manifest.append(plugin)

version_entry = {
    "version": meta["version"],
    "changelog": meta.get("changelog", f"Release {meta['version']}"),
    "targetAbi": meta["targetAbi"],
    "sourceUrl": source_url,
    "checksum": checksum,
    "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
}

# Replace an existing entry for the same version, then put the newest first.
plugin["versions"] = [v for v in plugin["versions"] if v["version"] != meta["version"]]
plugin["versions"].insert(0, version_entry)

with open(manifest_path, "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2, ensure_ascii=False)
    f.write("\n")

print(f"manifest: {meta['name']} {meta['version']} -> {manifest_path}")
