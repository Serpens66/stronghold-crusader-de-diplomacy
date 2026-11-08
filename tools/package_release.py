"""Package the existing verified preview; never rebuild or start the game."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile

import local_game as game

VERSION = "2.3.11-preview.1"
PREFIX = "Stronghold-Crusader-Diplomatie-" + VERSION
DLL_SHA256 = "4f0bacef7f634c5986b005c3f83296445b35a52f86f90572c56c993d70d5b4a2"
MODULE_ID = "8709ce84-96f3-4a47-8172-3afcd523fa4d"
STAMP = (2026, 11, 8, 0, 0, 0)
ROOT_FILES = (".gitattributes", ".gitignore", "README.md", "VALIDIERUNG.md",
              "RELEASE_NOTES.md", "build.py", "Diplomatie starten.cmd",
              "source-index.json", "source-index.json.sha256",
              "StrongholdEuropeDiplomatie.csproj")
TEXT_SUFFIXES = {".cs", ".py", ".csproj", ".json", ".sha256", ".cmd",
                 ".md", ".cfg", ".xaml", ".gitattributes", ".gitignore"}
PRIVATE = re.compile(r"(?:[A-Za-z]:[\\/]|(?<![A-Za-z0-9_])\\\\[A-Za-z0-9_-]+[\\/]|"
                     r"[/]Users[/]|[/]home[/]|DESKTOP-[A-Za-z0-9]+|"
                     r"gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]+|"
                     r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)")


def public_text(name: str, value: bytes) -> None:
    text = value.decode("utf-8-sig")
    # URI scheme endings are not drive-letter paths.
    cleaned = re.sub(r"https?://", "URL/", text)
    game.check(not PRIVATE.search(cleaned), f"Private or absolute path in {name}")


def collect() -> dict[str, bytes]:
    paths = [game.ROOT / name for name in ROOT_FILES]
    for directory in ("src", "assets", "references", "tools", "evidence"):
        paths.extend(p for p in (game.ROOT / directory).rglob("*") if p.is_file()
                     and "__pycache__" not in p.parts)
    entries = {}
    for path in sorted(paths):
        game.regular_path(path)
        game.check(path.suffix.lower() in TEXT_SUFFIXES or path.name in ROOT_FILES,
                   f"Unexpected public source file: {path.name}")
        name = path.relative_to(game.ROOT).as_posix()
        value = path.read_bytes()
        public_text(name, value)
        entries[name] = value
    return entries


def write_zip(path: Path, entries: dict[str, bytes]) -> None:
    game.check(not path.exists(), f"Output already exists: {path.name}")
    with zipfile.ZipFile(path, "x", compression=zipfile.ZIP_DEFLATED,
                         compresslevel=9) as archive:
        for name, content in sorted(entries.items()):
            info = zipfile.ZipInfo(name, STAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, content, compresslevel=9)
    with zipfile.ZipFile(path) as archive:
        game.check(archive.testzip() is None, "Archive integrity check failed.")
        game.check(set(archive.namelist()) == set(entries), "Archive inventory differs.")
        for name, content in entries.items():
            game.check(archive.read(name) == content, f"Archived content differs: {name}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", required=True, type=Path)
    args = parser.parse_args()
    game.check(not subprocess.check_output(
        ["git", "status", "--porcelain"], cwd=game.ROOT, text=True).strip(),
        "Commit the reviewed packaging files before making a release.")
    commit = subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=game.ROOT, text=True).strip()
    game.verify_install()
    receipt = game.read_json(game.ROOT / "build/qa/build-receipt.json")
    dll = (game.ROOT / "build/qa/bin" / game.ASSEMBLY).read_bytes()
    game.check(hashlib.sha256(dll).hexdigest() == DLL_SHA256 == receipt["artifact_sha256"],
               "Only the currently verified DLL may be packaged.")
    game.check(receipt["exit_code"] == 0 and receipt["source_unchanged_after_build"]
               and receipt["source_index_sha256"] == game.sha(game.ROOT / "source-index.json"),
               "The verified source/build contract differs.")
    game.check(game.module_id(game.ROOT / "build/qa/bin" / game.ASSEMBLY) == MODULE_ID,
               "The module identity differs.")
    # Scan both string encodings without distributing debug symbols or logs.
    for encoding in ("utf-8", "utf-16-le"):
        decoded = dll.decode(encoding, errors="ignore")
        game.check(not PRIVATE.search(re.sub(r"https?://", "URL/", decoded)),
                   "Private path or credential pattern in DLL.")
    source = collect()
    source_index = game.read_json(game.ROOT / "source-index.json")
    for entry in source_index["source_files"]:
        name = "src/" + entry["path"]
        game.check(hashlib.sha256(source[name]).hexdigest() == entry["sha256"],
                   f"Indexed source differs: {name}")
    manifest = {
        "schema": "diplomacy-public-preview-v1", "version": VERSION,
        "source_commit": commit, "dll_sha256": DLL_SHA256, "module_id": MODULE_ID,
        "source_index_sha256": receipt["source_index_sha256"],
        "source_count": receipt["source_count"], "build_profile": "qa",
        "singleplayer_only": True, "autonomous_ai_aid_enabled": True,
        "autonomous_ai_military_enabled": False, "native_mp_release": False,
        "native_normal_release": False, "game_or_dependency_binaries_included": False,
        "personal_profiles_or_saves_included": False,
    }
    manifest_bytes = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode()
    source["RELEASE_MANIFEST.json"] = manifest_bytes
    windows = {name: value for name, value in source.items()
               if name.startswith(("assets/", "evidence/")) or name in {
                   "README.md", "VALIDIERUNG.md", "RELEASE_NOTES.md", "RELEASE_MANIFEST.json",
                   "Diplomatie starten.cmd", "source-index.json", "source-index.json.sha256",
                   "references/preview-runtime.json", "references/qa.json",
                   "tools/local_game.py", "tools/preview.py"}}
    windows["build/qa/bin/" + game.ASSEMBLY] = dll
    windows["build/qa/build-receipt.json"] = (
        json.dumps(receipt, ensure_ascii=False, indent=2) + "\n").encode()
    for entries in (source, windows):
        checksum_lines = [hashlib.sha256(value).hexdigest() + "  " + name
                          for name, value in sorted(entries.items())]
        entries["CONTENTS.sha256"] = ("\n".join(checksum_lines) + "\n").encode()
    destination = args.output_dir.resolve()
    game.regular_path(destination)
    game.check(not destination.is_relative_to(game.GAME.resolve())
               and not destination.is_relative_to(game.PROFILE.resolve()),
               "Release output must stay outside game and profile.")
    destination.mkdir(parents=True, exist_ok=True)
    results = []
    for suffix, entries in (("Windows", windows), ("Source", source)):
        archive = destination / (PREFIX + "-" + suffix + ".zip")
        write_zip(archive, entries)
        results.append({"name": archive.name, "bytes": archive.stat().st_size,
                        "sha256": game.sha(archive), "files": len(entries)})
    sums = "".join(x["sha256"] + "  " + x["name"] + "\n" for x in results)
    (destination / "SHA256SUMS.txt").write_text(sums, encoding="ascii")
    (destination / "RELEASE_MANIFEST.json").write_bytes(manifest_bytes)
    (destination / "RELEASE_NOTES.md").write_bytes(source["RELEASE_NOTES.md"])
    print(json.dumps({"manifest": manifest, "archives": results}, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error))
