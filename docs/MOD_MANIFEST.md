# Adding a mod to the SurrounDead Mod Launcher

The launcher never changes. It reads two kinds of files:

1. **`catalog.json`** in this repo, which lists every mod. To add a mod, add one entry.
2. **`version.json`** hosted by the mod author, which says what to download and how to install it.

## Catalog entry

```json
{ "id": "mymod", "name": "My Mod", "author": "me",
  "manifest": "https://raw.githubusercontent.com/me/MyMod/main/version.json",
  "homepage": "https://github.com/me/MyMod",
  "icon": "https://.../icon.png", "banner": "https://.../banner.png" }
```

- `id` must be unique and stay the same forever.
- `icon` and `banner` are optional fallbacks. The ones in `version.json` take priority.

## version.json

```json
{
  "schema": 1,
  "name": "My Mod",
  "version": "1.2.0",
  "released": "2026-11-14",
  "game": { "steam_app_id": 1645820, "build": "26471822", "branch": "", "requires_exact_build": true },
  "download": { "url": "https://github.com/me/MyMod/releases/download/v1.2.0/MyMod-1.2.0.zip",
                "size": 12625011, "sha256": "<64 hex chars>" },
  "install": {
    "enabled_by": "SurrounDead/Binaries/Win64/ue4ss/Mods/MyMod/enabled.txt",
    "keep_if_present": ["SurrounDead/Binaries/Win64/dwmapi.dll", "SurrounDead/Binaries/Win64/ue4ss/UE4SS.dll"],
    "ue4ss_dll_sha256": "<sha256 of the UE4SS.dll in your zip>",
    "off_by_default": ["SurrounDead/Binaries/Win64/ue4ss/Mods/OptionalThing/"],
    "ini_set": [ { "file": "{game}/SurrounDead/Config/UserEngine.ini", "section": "Foo", "key": "Bar", "value": "1" } ]
  },
  "icon": "https://.../icon.png",
  "banner": "https://.../banner.png",
  "screenshots": ["https://.../shot1.png", "https://.../shot2.png"],
  "changelog": ["Fixed doors opening twice", "New: shared map markers"],
  "notes": "https://github.com/me/MyMod/releases/tag/v1.2.0"
}
```

| Field | Meaning |
|---|---|
| `version` | Raise it for every release. The launcher compares numerically (`1.10.0` > `1.9.0`). |
| `game.branch` | Steam beta branch (`""` = default branch, `"experimental"`, ...). |
| `game.build` + `requires_exact_build` | With `true`, the mod is only offered for exactly this Steam build. |
| `download.url` | **https only.** A zip whose paths start at the game folder (the one with `SurrounDead.exe`), e.g. `SurrounDead/Binaries/Win64/...` |
| `download.sha256` | **Required.** The launcher refuses the download if it does not match. `size` is optional. |
| `enabled_by` | File that turns the mod on. Only one mod is active at a time; the launcher creates and deletes this file. |
| `keep_if_present` | Files or folders (ending with `/`) that are not overwritten if they already exist, except when `UE4SS.dll` does not match `ue4ss_dll_sha256`. |
| `off_by_default` | Folders (their `enabled.txt` becomes `enabled.txt.off`) or files (renamed to `.off`) that are switched off on the first install. |
| `ini_set` | Optional ini keys. `file` must start with `{game}/` or `{config}/` (= `%LOCALAPPDATA%\SurrounDead\Saved\Config`). Removed again on uninstall. |
| `icon` | Square PNG, about 128×128 (64×64 or smaller is drawn as pixel art). |
| `banner` | Wide PNG or JPG, about 1280×400. Shown behind the mod name; keep the important part in the middle, the bottom ~15% is darkened for the title. |
| `screenshots` | Up to 6 preview images (PNG or JPG, 16:9 such as 1280×720 works best). Shown as thumbnails; a click opens them large. |
| `changelog` | Short lines shown under the button ("CHANGES"). A list of strings, or one string with one line per change. Keep it to the latest release, about 3 to 6 lines. |
| `notes` | Link to the full release notes ("Full changelog"). |

Rules the launcher enforces:
- Images are PNG or JPG, at most 2 MB.
- Zip entries must stay inside the game folder.
- Setup programs and other executables in the manifest are never run.
- Unknown keys under `install` make the launcher show *Update launcher* instead of guessing.
- Everything else unknown is ignored.

## Hosting the images

The simplest way is to keep the images in your GitHub repo, e.g. a folder `launcher/`, and link the raw files:

```
https://raw.githubusercontent.com/<you>/<repo>/<branch>/launcher/banner.png
```

Those links stay the same across releases. Without images the launcher draws a pixel landscape and a pixel icon.

## Checksum (PowerShell)

```
(Get-FileHash .\MyMod-1.2.0.zip -Algorithm SHA256).Hash.ToLower()
```
