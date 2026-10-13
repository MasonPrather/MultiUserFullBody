#!/usr/bin/env python3
"""Check the cloned scene and imported Meta assets without starting Unity.

Run from any directory after Unity has restored the project's package cache.
This checks serialized dependencies, not Unity import or headset behavior.
"""
from collections import defaultdict
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Assets/_MetaAvatarsFall2026"
SCENE = ROOT / "Assets/_Scenes/MUFBAvatars_Fall2026.unity"
SOURCE = ROOT / "Assets/_Scenes/PhonePhotoUpload.unity"
DOCUMENT = re.compile(r"^--- !u!(\d+) &(-?\d+)([^\n]*)\n(.*?)(?=^--- !u!|\Z)", re.M | re.S)
GUID = re.compile(r"^guid: (\w+)", re.M)


def documents(path):
    matches = list(DOCUMENT.finditer(path.read_text()))
    result = {m[2]: (m[1], m[3], m[4]) for m in matches}
    assert len(result) == len(matches), f"Duplicate object IDs: {path}"
    return result


def dangling_local_references(path):
    ids = set(documents(path)) | {"0"}
    return set(re.findall(r"\{fileID: (-?\d+)\}", path.read_text())) - ids


def main():
    original, cloned = documents(SOURCE), documents(SCENE)
    missing = dangling_local_references(SCENE) - dangling_local_references(SOURCE)
    assert not missing, f"New dangling scene references: {missing}"
    text = SCENE.read_text()
    for legacy in (
        "readyplayer", "4853d5faba80e4b3ebca0150cd9550e3",  # Old network avatar
        "32c1bb7ba83be4107b67aec1e6d07300",  # RPM local loader
        "91b64ab16a896416f81957864fc0e9d2",  # RPM blendshape probe
    ):
        assert legacy not in text.lower(), f"Legacy avatar dependency: {legacy}"
    protocols = [int(v) for v in re.findall(r"ProtocolVersion: (\d+)", text)]
    assert 2027 in protocols and all(0 <= v <= 65535 for v in protocols), "Missing Meta/XRI protocol 2027"
    for prefab in (ASSETS / "Prefabs").glob("*.prefab"):
        assert not dangling_local_references(prefab), f"Dangling prefab references: {prefab}"

    index = defaultdict(list)
    for base in (ROOT / "Assets", ROOT / "Library/PackageCache"):
        for meta in base.rglob("*.meta"):
            if any(part.endswith("~") for part in meta.parts):
                continue  # UPM's unimported samples are not Unity assets.
            try:
                match = GUID.search(meta.read_text())
                if match:
                    index[match[1]].append(Path(str(meta)[:-5]))
            except (OSError, UnicodeError):
                continue

    files = [p for p in ASSETS.rglob("*") if p.is_file() and p.suffix != ".meta"]
    for path in files:
        assert Path(str(path) + ".meta").exists(), f"Missing stable GUID: {path}"
        try:
            text = path.read_text()
        except UnicodeError:
            continue
        for file_id, guid in re.findall(r"fileID: (-?\d+), guid: (\w+)", text):
            if guid.startswith("00000000"):
                continue  # Unity built-ins
            assert guid in index, f"Unresolved GUID {guid} in {path}"
            target = index[guid][0]
            if target.suffix in (".prefab", ".asset", ".controller"):
                ids = documents(target)
                assert not ids or file_id in ids or file_id in ("0", "100100000"), (
                    f"Missing object {file_id} in {target}, referenced by {path}"
                )
    for guid, paths in index.items():
        if any(p.is_relative_to(ASSETS) for p in paths):
            assert len(paths) == 1, f"Duplicate imported GUID {guid}: {paths}"

    for name in ("7_quest_light.glb.zst", "7_rift_light.glb.zst"):
        model = ROOT / "Assets/StreamingAssets/MUFBAvatars_Fall2026" / name
        # Meta's supplied GLBs are Zstandard compressed, including those named just .glb in its ZIPs.
        assert model.read_bytes()[:4] == b"\x28\xb5\x2f\xfd", f"Missing/invalid bundled avatar: {model}"
        assert Path(str(model) + ".meta").exists()
    rig = ASSETS / "SDKSupport/Scenes/LegsNetworkLoopback/Resources/RTR00003.prefab"
    assert rig.exists(), "Missing Resources-loaded default legs rig"
    assert "enableCrouchTimeout: 0" in rig.read_text()
    changed = sum(original[i] != cloned[i] for i in original.keys() & cloned.keys())
    print(f"PASS: {len(files)} support assets, two bundled avatars, and cloned scene references.")
    print(f"Source comparison: {changed} changed documents, {len(original.keys() - cloned.keys())} removed.")


if __name__ == "__main__":
    main()
