# my-jellyfin-plugins

Personal Jellyfin plugin registry. Add this repository in Jellyfin and install plugins from the catalog; updates show up automatically.

## Add the registry to Jellyfin

1. Dashboard → **Plugins** → **Repositories** → **Add**
2. Name: `my-jellyfin-plugins`
3. URL: `https://raw.githubusercontent.com/bbetter/my-jellyfin-plugins/main/manifest.json`
4. Save, then open the **Catalog** and install the plugin.
5. Restart Jellyfin after installing or updating.

## Plugins

| Plugin | Source | Latest release |
|---|---|---|
| [JellyShare](plugins/JellyShare) | Temporary public share links for media items | see [Releases](https://github.com/bbetter/my-jellyfin-plugins/releases) |

## Releasing a plugin

Releases are built by GitHub Actions from a tag:

```bash
git tag jellyshare-v1.0.1
git push origin jellyshare-v1.0.1
```

The workflow in `.github/workflows/release-jellyshare.yml`:

1. Publishes the plugin with .NET 8, using the tag version.
2. Packages `JellyShare.dll` and `meta.json` into `JellyShare-<version>.zip`.
3. Creates a GitHub Release with the zip attached.
4. Adds the version to `manifest.json` (MD5 checksum, source URL, timestamp) and pushes it to `main`.

`tools/update_manifest.py` does step 4. Each plugin keeps its own tag prefix, so one repository can host several plugins.

## Layout

```
manifest.json                       # plugin registry read by Jellyfin
plugins/<Name>/                     # plugin source
.github/workflows/release-<name>.yml
tools/update_manifest.py
```
