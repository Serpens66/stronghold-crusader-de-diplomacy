"""Prepare the prebuilt preview from a compatible game supplied by its owner.

The source game is read only. No SDK, game binaries, provider mods, saves or
personal configuration are distributed with the preview.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import shutil
import uuid

import local_game as game


def prepare(source: Path) -> None:
    source = source.resolve(strict=True)
    game.regular_path(source)
    game.regular_path(game.RUNTIME)
    game.check(source.is_dir(), "Select a game directory.")
    game.check(not game.ROOT.resolve().is_relative_to(source),
               "Extract the preview outside the source game directory.")
    game.check(not source.is_relative_to(game.RUNTIME.resolve()),
               "The input must be outside this preview's runtime directory.")
    game.check(not game.GAME.exists(), "A preview game is already prepared.")
    game.check(not game.RUNTIME.exists() or not any(game.RUNTIME.glob("game.incoming-*")),
               "Inspect the interrupted preparation before trying again.")
    lock = game.read_json(game.ROOT / "references/preview-runtime.json")
    for relative, expected in lock["game_files"].items():
        target = source / relative
        game.regular_path(target)
        game.check(target.is_file() and game.sha(target) == expected,
                   f"Unsupported game or extender file: {relative}")
    for provider, expected in lock["providers"].items():
        root = source / "BepInEx/plugins" / provider
        game.regular_path(root)
        game.check(root.is_dir() and game.inventory(root) == expected,
                   f"This preview needs the exact pinned provider: {provider}")

    # Hashes are checked before copying or installing anything. Existing game
    # settings, unrelated plugins, logs and saves are not copied.
    game.RUNTIME.mkdir(parents=True, exist_ok=True)
    incoming = game.RUNTIME / ("game.incoming-" + uuid.uuid4().hex[:8])
    incoming.mkdir()
    for name in ("Assets", "MonoBleedingEdge", game.GAME_DATA,
                 "BepInEx/core", "BepInEx/patchers"):
        original = source / name
        if original.is_dir():
            print(f"Copying {name}", flush=True)
            game.copy_tree(original, incoming / name)
    for name in (game.EXE_NAME, "UnityPlayer.dll", "UnityCrashHandler64.exe",
                 "msvcp140.dll", "winhttp.dll", ".doorstop_version",
                 "doorstop_config.ini", "steam_appid.txt"):
        original = source / name
        game.regular_path(original)
        if original.is_file():
            shutil.copyfile(original, incoming / name)
    for provider in lock["providers"]:
        relative = Path("BepInEx/plugins") / provider
        game.copy_tree(source / relative, incoming / relative)
    config = incoming / "BepInEx/config"
    config.mkdir()
    shutil.copyfile(game.ROOT / "assets/config/000shcdese.cfg",
                    config / "000shcdese.cfg")
    for relative, expected in lock["game_files"].items():
        game.check(game.sha(incoming / relative) == expected,
                   f"Copied game fingerprint differs: {relative}")
    for provider, expected in lock["providers"].items():
        game.check(game.inventory(incoming / "BepInEx/plugins" / provider) == expected,
                   f"Copied provider differs: {provider}")
    incoming.replace(game.GAME)
    game.write_json(game.RUNTIME / "prepared.json", lock)
    game.install()
    game.verify_install()
    print("Preview prepared. Use Diplomatie starten.cmd for a local skirmish.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    setup = commands.add_parser("prepare")
    setup.add_argument("--game-source", type=Path, required=True,
                       help="Your compatible game with the pinned extender and provider mods")
    args = parser.parse_args()
    if args.command == "prepare":
        game.no_running_game()
        prepare(args.game_source)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError) as error:
        raise SystemExit(str(error))
