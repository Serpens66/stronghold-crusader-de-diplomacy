#!/usr/bin/env python3
"""Verify native trade and AI aid using separate headless original-game processes."""
import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import uuid

import local_game

ROOT = local_game.RUNTIME / "checks"
EXE = local_game.EXE_NAME
PLUGIN = (local_game.MOD / local_game.ASSEMBLY).relative_to(local_game.GAME)
HEX64 = re.compile(r"[0-9a-fA-F]{64}\Z")
RUN_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9_-]{0,39}\Z")
ACTIONS = {"trade": ("LOAD_TRADE", "LOAD_VERIFY"),
           "aid": ("LOAD_AI_AID_WARM", "LOAD_AI_VERIFY")}


def insist(value, detail):
    if not value:
        raise RuntimeError("ACCEPTANCE_REFUSED: " + detail)


def sha256(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def exact_sha(value):
    insist(bool(HEX64.fullmatch(value)), "invalid SHA-256 string")
    return value.lower()


def is_reparse(path):
    path = Path(path)
    for item in (path, *path.parents):
        if item.exists():
            if item.is_symlink() or (hasattr(item, "is_junction") and item.is_junction()):
                return True
    return False


def check_inputs(args):
    insist(sys.platform == "win32", "live runner requires Windows")
    insist(args.run_id is None or RUN_ID.fullmatch(args.run_id), "invalid run-id")
    game = local_game.GAME.resolve(strict=True)
    dll = local_game.ROOT / "build/qa/bin" / local_game.ASSEMBLY
    manifest = local_game.verify_install()
    template = Path(args.profile_template).resolve(strict=True)
    trade = Path(args.trade_fixture).resolve(strict=True)
    aid = Path(args.aid_fixture).resolve(strict=True)
    paths = [game, dll, template, trade, aid, ROOT]
    insist(not any(is_reparse(p) for p in paths), "reparse point/junction blocked")
    insist(game.is_dir() and (game / EXE).is_file(), "game exe absent")
    insist(template.is_dir() and (template / "settings.cfg").is_file(),
           "read-only isolated profile template/settings.cfg absent")
    insist(trade.is_file() and aid.is_file() and trade != aid and
           trade.suffix.lower() == ".sav" and aid.suffix.lower() == ".sav" and
           trade.name != aid.name, "distinct authentic .sav fixtures required")
    pins = {"trade_fixture": exact_sha(args.trade_sha256),
            "aid_fixture": exact_sha(args.aid_sha256),
            "dll": exact_sha(args.dll_sha256)}
    for name, path in (("trade_fixture", trade), ("aid_fixture", aid), ("dll", dll)):
        insist(sha256(path) == pins[name], name + " SHA-256 mismatch")
    active_dll = game / PLUGIN
    insist(active_dll.is_file() and not is_reparse(active_dll) and
           sha256(active_dll) == pins["dll"],
           "coordinator must already stage identical DLL in game copy")
    mvid = str(uuid.UUID(args.mvid))
    insist(local_game.module_id(dll) == mvid and manifest["module_id"] == mvid,
           "PE/CLI module MVID mismatch")
    insist(ROOT != game and not ROOT.is_relative_to(game), "output must be separate")
    return {"game": game, "dll": dll, "template": template,
            "trade": trade, "aid": aid, "mvid": mvid, "pins": pins}


def read_events(path, pid=None):
    if not Path(path).is_file():
        return []
    result = []
    for line in Path(path).read_text("utf-8-sig", errors="replace").splitlines():
        if pid is not None and ("|pid=" + str(pid) + "|") not in line:
            continue
        entries = line.split("|")
        event = {}
        for part in entries:
            if "=" in part:
                key, value = part.split("=", 1)
                event[key] = value
        if "status" in event:
            result.append(event)
    return result


def one(events, status, **attributes):
    matches = [x for x in events if x.get("status") == status and
               all(x.get(k) == str(v) for k, v in attributes.items())]
    insist(len(matches) == 1,
           "expected exactly one " + status + " " + repr(attributes) +
           "; observed=" + str(len(matches)))
    return matches[0]


def csv_ints(value, count):
    nums = [int(s) for s in value.split(",")]
    insist(len(nums) == count and all(n >= 0 for n in nums),
           "stock cell count/value invalid")
    return nums

def no_running_crusader():
    # Read-only process inventory; refuse to proceed if it cannot be obtained.
    query = ("Get-CimInstance Win32_Process -Filter "
             "\"Name LIKE '%Crusader%'\" | Select-Object -ExpandProperty ProcessId")
    done = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive",
                           "-Command", query], capture_output=True, text=True,
                          timeout=18, check=False)
    insist(done.returncode == 0, "cannot attest exclusive game ownership: " +
           done.stderr[-250:])
    insist(not done.stdout.strip(), "another Crusader process is active")


def make_profile(template, dest, fixture):
    insist(not dest.exists(), "profile already exists: " + str(dest))
    dest.mkdir()
    insist(not is_reparse(template / "settings.cfg"),
           "profile settings may not be a link")
    shutil.copy2(template / "settings.cfg", dest / "settings.cfg")
    allowed_folders = ("Local", "Roaming", "CustomLords", "CustomMedia",
                       "CustomTrails", "ExtendedLords", "Maps",
                       "TrailMaker", "UserWorkshopMaps")
    for part in allowed_folders:
        source = template / part
        target = dest / part
        insist(not is_reparse(source), "profile template reparse path blocked")
        if source.is_dir():
            for base, subdirs, files in os.walk(source):
                for name in subdirs + files:
                    insist(not is_reparse(Path(base) / name),
                           "profile contains a reparse point")
            shutil.copytree(source, target)
        elif part in ("Local", "Roaming"):
            target.mkdir()
    (dest / "Saves").mkdir()
    copied = dest / "Saves" / fixture.name
    shutil.copy2(fixture, copied)
    insist(sha256(copied) == sha256(fixture), "isolated fixture copy changed")
    return copied


def new_env(ctx, profile, lane, action, result):
    env = os.environ.copy()
    for key in list(env):
        if key.startswith("DIPLOMACY_"):
            del env[key]
    base = {
        "DIPLOMACY_TEST_GAME_ROOT": str(ctx["game"]),
        "DIPLOMACY_TEST_PROFILE_ROOT": str(profile),
        "DIPLOMACY_HEADLESS_TEST": "1",
        "DIPLOMACY_CANONICAL_TRADE": "1",
        "DIPLOMACY_EXPECTED_MODULE_ID": ctx["mvid"],
        "DIPLOMACY_HEADLESS_STOP_UNIX_MS":
            str(int(time.time() * 1000) +
                (235000 if action == "LOAD_AI_AID_WARM"
                 else 225000 if action == "LOAD_AI_VERIFY"
                 else 195000 if action == "LOAD_TRADE" else 225000)),
        "APPDATA": str(profile / "Roaming"),
        "LOCALAPPDATA": str(profile / "Local"),
        "USERPROFILE": str(profile),
        "TEMP": str(result),
        "TMP": str(result),
        "DIPLOMACY_NATIVE_TRADE_FOUR_CELL_PROBE": "1" if lane == "trade" else "0",
        "DIPLOMACY_AID_PROBE": "1" if lane == "aid" else "0",
        "DIPLOMACY_AI_MILITARY_TEST": "1" if lane == "aid" else "0",
        "DIPLOMACY_ENABLE_AUTONOMOUS_AID":
            "1" if action == "LOAD_AI_AID_WARM" else "0",
    }
    if lane == "trade":
        base["DIPLOMACY_PRIVATE_QA_TRADE_AI_SLOT"] = "2"
        base["DIPLOMACY_PRIVATE_QA_TRADE_REQUESTED_QUANTITY"] = "1"
    env.update(base)
    return env


def snapshot_bepinex(ctx, result, label, previous):
    target = ctx["game"] / "BepInEx" / "LogOutput.log"
    insist(target.is_file() and sha256(target) != previous,
           "game BepInEx log unchanged; cannot attribute this process")
    path = result / (label + "_bepinex.log")
    insist(not path.exists(), "BepInEx snapshot path already exists")
    shutil.copy2(target, path)
    return path


def launch(ctx, lane, phase, save, home, render, label):
    profile, qa = home / "profile", home / "qa"
    no_running_crusader()
    insist(sha256(ctx["dll"]) == ctx["pins"]["dll"] and
           sha256(ctx["game"] / PLUGIN) == ctx["pins"]["dll"],
           "same binary not present for this process")
    unity = home / (label + "_unity.log")
    stdout = home / (label + "_stdout.log")
    game_log = ctx["game"] / "BepInEx" / "LogOutput.log"
    before = sha256(game_log) if game_log.is_file() else None
    action = ACTIONS[lane][0 if phase == "warm" else 1]
    cmd = [str(ctx["game"] / EXE), "-batchmode"]
    if render == "nographics":
        cmd.append("-nographics")
    cmd.extend(["-logFile", str(unity)])
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0  # SW_HIDE; no Windows UI actions or screenshots.
    start = time.time()
    with stdout.open("xb") as file:
        proc = subprocess.Popen(cmd, cwd=ctx["game"],
                                env=new_env(ctx, profile, lane, action, home),
                                stdin=subprocess.DEVNULL, stdout=file,
                                stderr=subprocess.STDOUT, startupinfo=startup,
                                creationflags=subprocess.CREATE_NO_WINDOW)
        print(f"{lane} {phase}: original game PID {proc.pid}", flush=True)
        timed_out = False
        try:
            returncode = proc.wait(timeout=275 if lane == "aid" else 245)
        except subprocess.TimeoutExpired:
            timed_out = True
            # Only the process handle created above may be terminated.
            proc.terminate()
            try:
                proc.wait(timeout=12)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait(timeout=10)
            returncode = proc.returncode
    assert proc.poll() is not None
    events = read_events(qa / "HeadlessDriver.log", proc.pid)
    changed = game_log.is_file() and sha256(game_log) != before
    game_snapshot = (snapshot_bepinex(ctx, home, label, before)
                     if changed else None)
    insist(sha256(ctx["dll"]) == ctx["pins"]["dll"] and
           sha256(ctx["game"] / PLUGIN) == ctx["pins"]["dll"],
           "binary changed while running")
    return {
        "lane": lane, "phase": phase, "action": action,
        "pid": proc.pid, "render": render,
        "elapsed_seconds": round(time.time() - start, 3),
        "returncode": returncode, "timed_out": timed_out,
        "unity_log": str(unity), "stdout_log": str(stdout),
        "bepinex_log": str(game_snapshot) if game_snapshot else None,
        "events": events,
    }


def process_once(ctx, lane, phase, save, home, mode, allow_fallback):
    profile, qa = home / "profile", home / "qa"
    insist((profile / "Saves" / save).is_file(), "command input save missing")
    command = qa / "HeadlessCommand.txt"
    insist(not command.exists(), "previous QA command not consumed")
    action = ACTIONS[lane][0 if phase == "warm" else 1]
    command.write_text(action + "|" + save, encoding="utf-8")
    render = "hidden-gpu" if mode == "hidden-gpu" else "nographics"
    first = launch(ctx, lane, phase, save, home, render, phase + "_" + render)
    attempts = [first]
    if render == "nographics" and allow_fallback:
        joined = (Path(first["unity_log"]).read_text("utf-8-sig", errors="replace")
                  if Path(first["unity_log"]).is_file() else "")
        joined += Path(first["stdout_log"]).read_text("utf-8-sig", errors="replace")
        graphics_problem = any(x in joined.lower() for x in (
            "graphics device is null", "failed to initialize graphics",
            "no graphics device", "graphics device initialization failed",
            "noesis graphics initialization failed"))
        was_consumed = any(x.get("status") in (
            "explicit_command_consumed", "original_load_begin",
            "original_load_returned") for x in first["events"])
        # No rerun after any user/save/gameplay command has begun.
        if graphics_problem and not was_consumed and command.is_file() and (
                command.read_text("utf-8") == action + "|" + save):
            attempts.append(launch(ctx, lane, phase, save, home,
                                   "hidden-gpu", phase + "_hidden-gpu"))
    current = attempts[-1]
    insist(not current["timed_out"] and current["returncode"] == 0,
           "game process failed; no further gameplay retry")
    insist(not command.exists(), "game did not consume explicit command")
    return current, attempts


def common_proof(proc, save_sha):
    events = proc["events"]
    one(events, "batch_inert_devices_ready", input_events_injected="0")
    one(events, "explicit_command_consumed", action=proc["action"])
    one(events, "original_load_begin", sha256=save_sha)
    one(events, "original_load_returned")
    solo = one(events, "original_solo_admitted",
               reason="one_engine_local_human_no_remote_peer", human_slots="1")
    insist({"2", "3"}.issubset(solo.get("ai_slots", "").split(",")),
           "not authenticated one-human/two-original-AI solo SaveData")
    forbidden = ("driver_fault", "blocked", "operator_deadline_abort",
                 "operator_shutdown_grace_exhausted")
    insist(not any(x.get("status") in forbidden for x in events),
           "QA driver emitted terminal refusal")
    insist(proc["bepinex_log"] is not None, "no new game plugin log")
    log = Path(proc["bepinex_log"]).read_text("utf-8-sig", errors="replace")
    insist("DIPLOMACY_PRIVATE_PROVIDER_PROFILE_READY" in log and
           "_PRIVATE_PROFILE_ROUTING_VERIFIED" in log, "isolated profile routes missing")
    insist("Loading [Stronghold Crusader Diplomatie 2.3.11]" in log and
           "status=apishared_shared_owner_active" in log,
           "same QA DLL + final APIShared owner not attested")
    return log


def trade_warm_proof(proc, input_sha, home):
    common_proof(proc, input_sha)
    one(proc["events"], "production_native_qa_finished", passed="True", qa_stage="100")
    goods = read_events(home / "qa" / "NativeTradeQa.log")
    one(goods, "explicit_run_consumed", process=proc["pid"])
    one(goods, "private_terms_selected",
        forced_AI_decision="false", forced_native_write="false")
    consent = one(goods, "actual_ai_offer_answered_via_production_consent")
    commit = one(goods, "actual_four_cell_native_commit")
    insist(consent.get("id") == commit.get("id"), "AI consent/commit ID drift")
    before = csv_ints(commit["before"], 4)
    after = csv_ints(commit["after"], 4)
    players = csv_ints(commit["players"], 4)
    goods_ids = csv_ints(commit["goods"], 4)
    insist(players[0] == players[3] and players[1] == players[2] and
           players[0] != players[1] and
           goods_ids[0] == goods_ids[1] and goods_ids[2] == goods_ids[3] and
           goods_ids[0] != goods_ids[2], "not four distinct native barter cells")
    insist(before[0] + before[1] == after[0] + after[1] and
           before[2] + before[3] == after[2] + after[3] and
           before[0] > after[0] and after[1] > before[1] and
           before[2] > after[2] and after[3] > before[3],
           "native trade stocks not conserved/transferred")
    saved = home / "profile" / "Saves" / "AfterNativeTrade.sav"
    expectation = home / "qa" / "NativeTradeColdExpectation.bin"
    insist(saved.is_file() and expectation.is_file(), "warm original SaveData/seal absent")
    seal = one(goods, "cold_expectation_written", trade_id=commit["id"],
               save_sha256=sha256(saved), native_write_calls="4")
    insist(seal is not None, "native 4-write seal invalid")
    return {"id": commit["id"], "before": before, "after": after,
            "players": players, "goods": goods_ids, "saved_sha256": sha256(saved),
            "expectation_sha256": sha256(expectation),
            "goods_log_sha256": sha256(home / "qa" / "NativeTradeQa.log")}


def trade_cold_proof(proc, warm, home):
    common_proof(proc, warm["saved_sha256"])
    one(proc["events"], "cold_verification_finished", passed="True")
    row = one(proc["events"], "cold_native_reload_verified",
              source_pid=warm["pid"], trade_id=warm["id"],
              save_sha256=warm["saved_sha256"],
              previous_native_write_calls="4", reload_native_write_calls="0")
    for key, expected in (("before", warm["before"]),
                          ("after_commit", warm["after"]),
                          ("at_save", warm["after"]),
                          ("reloaded", warm["after"])):
        insist(csv_ints(row[key], 4) == expected, "cold barter stock " + key + " drift")
    insist(sha256(home / "profile" / "Saves" / "AfterNativeTrade.sav") ==
           warm["saved_sha256"], "original trade SaveData mutated by cold process")
    insist(sha256(home / "qa" / "NativeTradeColdExpectation.bin") ==
           warm["expectation_sha256"], "native trade expectation mutated")
    insist(sha256(home / "qa" / "NativeTradeQa.log") ==
           warm["goods_log_sha256"], "cold created another native trade record")
    return {"native_replay_calls": 0, "four_stock_cells": warm["after"],
            "original_SaveData_sha256": warm["saved_sha256"],
            "expectation_sha256": warm["expectation_sha256"]}


def aid_warm_proof(proc, input_sha, home, mvid):
    game_log = common_proof(proc, input_sha)
    one(proc["events"], "original_ai_warm_input_observed",
        alliance_2_to_3="True", alliance_3_to_2="True",
        original_native_team_2="2", original_native_team_3="2")
    native = one(proc["events"], "actual_native_ai_aid_observed",
                 native_write_calls="2")
    before = csv_ints(native["before"], 2)
    after = csv_ints(native["after"], 2)
    amount = int(native["amount"])
    insist(amount > 0 and before[0] - after[0] == amount and
           after[1] - before[1] == amount, "AI gift not two native stock cells")
    one(proc["events"], "original_ai_gift_ready_for_original_save",
        id=native["id"], observed_original_two_native_writes="true",
        ledger_delivered_and_favor_validated="true")
    one(proc["events"], "original_ai_warm_save_and_cold_expectation_sealed",
        id=native["id"], module_mvid=mvid, exact_save="AfterNativeAiAid.sav")
    one(proc["events"], "original_ai_aid_warm_finished", passed="True")
    insist(game_log.count("status=native_delivered_and_journal_committed") == 1 and
           "status=original_save_history_receipt_sealed" in game_log,
           "exactly one delivered AI gift and authenticated ledger required")
    saved = home / "profile" / "Saves" / "AfterNativeAiAid.sav"
    expectation = home / "qa" / "NativeAidColdExpectation.bin"
    receipts = list((home / "profile" / "NativeAidProof").glob("*.receipt"))
    insist(saved.is_file() and expectation.is_file() and len(receipts) == 1,
           "warm native SaveData/expectation/private receipt absent or ambiguous")
    receipt = receipts[0]
    insist(len(str(receipt)) < 260, "receipt path exceeds Windows framework limit")
    return {"id": native["id"], "donor": native["donor"],
            "recipient": native["recipient"], "good": native["good"],
            "quantity": amount, "before": before, "after": after,
            "saved_sha256": sha256(saved), "expectation_sha256": sha256(expectation),
            "receipt_path": str(receipt), "receipt_sha256": sha256(receipt)}


def aid_cold_proof(proc, warm, home):
    game_log = common_proof(proc, warm["saved_sha256"])
    readback = {}
    saved_cells = {}
    for phase in ("initial", "production_pump_no_replay"):
        event = one(proc["events"], "cold_ai_aid_verified", phase=phase,
                    source_pid=warm["pid"], id=warm["id"],
                    save_sha256=warm["saved_sha256"],
                    cold_native_write_calls="0",
                    original_gift_native_replay_attempts="0",
                    distinct_saved_pending_gifts_delivered="0",
                    historical_receipt_authenticated="True",
                    native_gift_observed_this_process="False",
                    ledger_matched="True")
        saved_cells[phase] = csv_ints(event["saved"], 2)
        readback[phase] = csv_ints(event["actual"], 2)
        if phase == "initial":
            insist(event.get("saved_cells_match") == "True",
                   "original AI stock did not match the sealed warm save")
    # AI production can change the cells between the two-cell gift commit
    # and the original save. Cold logs report the sealed warm expectation.
    insist(saved_cells["production_pump_no_replay"] == saved_cells["initial"] and
           readback["initial"] == saved_cells["initial"],
           "native AI gift reloaded stock drift")
    progress = one(proc["events"], "cold_original_simulation_progress")
    insist(int(progress["actual_tick"]) > int(progress["start_tick"]),
           "original simulation did not advance before the replay audit")
    one(proc["events"], "cold_ai_military_finished", passed="True")
    insist("status=native_delivered_and_journal_committed" not in game_log,
           "cold AI gift was delivered twice")
    paths = {"saved_sha256": home / "profile" / "Saves" / "AfterNativeAiAid.sav",
             "expectation_sha256": home / "qa" / "NativeAidColdExpectation.bin",
             "receipt_sha256": Path(warm["receipt_path"])}
    for key, path in paths.items():
        insist(path.is_file() and sha256(path) == warm[key],
               "original AI history changed after cold: " + key)
    return {"native_replay_calls": 0, "two_stock_cells": saved_cells["initial"],
            "stocks_after_original_simulation": readback["production_pump_no_replay"],
            "original_SaveData_sha256": warm["saved_sha256"],
            "expectation_sha256": warm["expectation_sha256"],
            "authenticated_receipt_sha256": warm["receipt_sha256"]}


def public_attempt(proc):
    keys = ("lane", "phase", "action", "pid", "render",
            "elapsed_seconds", "returncode", "timed_out")
    details = {key: proc[key] for key in keys}
    details["sha256"] = {
        "unity_log": sha256(proc["unity_log"])
        if Path(proc["unity_log"]).is_file() else None,
        "stdout_log": sha256(proc["stdout_log"]),
        "bepinex_log": sha256(proc["bepinex_log"])
        if proc["bepinex_log"] else None,
    }
    return details


def run_lane(ctx, args, lane, runroot):
    home = runroot / lane
    insist(not home.exists(), "lane output already exists")
    home.mkdir()
    (home / "qa").mkdir()
    source = ctx[lane]
    baseline = make_profile(ctx["template"], home / "profile", source)
    start_pin = ctx["pins"][lane + "_fixture"]
    insist(sha256(baseline) == start_pin, "original fixture not pinned")
    warm, attempts_w = process_once(ctx, lane, "warm", baseline.name, home,
                                    args.render_mode, args.allow_hidden_gpu_fallback)
    if lane == "trade":
        accepted = trade_warm_proof(warm, start_pin, home)
        cold_name = "AfterNativeTrade.sav"
    else:
        accepted = aid_warm_proof(warm, start_pin, home, ctx["mvid"])
        cold_name = "AfterNativeAiAid.sav"
    accepted["pid"] = warm["pid"]
    cold, attempts_c = process_once(ctx, lane, "cold", cold_name, home,
                                    args.render_mode, args.allow_hidden_gpu_fallback)
    insist(warm["pid"] != cold["pid"], "cold process must have a different PID")
    if lane == "trade":
        verified = trade_cold_proof(cold, accepted, home)
    else:
        verified = aid_cold_proof(cold, accepted, home)
    insist(sha256(source) == start_pin and sha256(baseline) == start_pin,
           "authentic original fixture mutated")
    return {"passed": True, "original_fixture_sha256": start_pin,
            "same_dll_sha256": ctx["pins"]["dll"], "MVID": ctx["mvid"],
            "warm": accepted, "cold": verified,
            "processes": [public_attempt(x) for x in attempts_w + attempts_c]}


def parse_arguments(argv=None):
    parser = argparse.ArgumentParser(
        description="Read-only preflight or coordinator-authorized original-game "
                    "two-lane native trade + AI aid warm/cold acceptance.")
    parser.add_argument("operation", choices=("preflight", "run"))
    parser.add_argument("--lane", choices=("both", "aid", "trade"), default="both",
                        help="selected lane; default both (trade then aid)")
    parser.add_argument("--profile-template", required=True,
                        help="read-only clean original isolated profile settings")
    parser.add_argument("--dll-sha256", required=True)
    parser.add_argument("--mvid", required=True)
    parser.add_argument("--trade-fixture", required=True)
    parser.add_argument("--trade-sha256", required=True)
    parser.add_argument("--aid-fixture", required=True)
    parser.add_argument("--aid-sha256", required=True)
    parser.add_argument("--run-id", help="new output/runs/<id>, never overwritten")
    parser.add_argument("--render-mode", choices=("auto", "hidden-gpu"),
                        default="hidden-gpu")
    parser.add_argument("--allow-hidden-gpu-fallback", action="store_true",
                        help="only if nographics fails before QA command, with "
                             "documented graphics-init failure signature")
    parser.add_argument("--authorize-live-run", default="",
                        help="must equal COORDINATOR_EXCLUSIVE_GAME_RUN")
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_arguments(argv)
    ctx = check_inputs(args)
    pins = ctx["pins"]
    lanes = ("trade", "aid") if args.lane == "both" else (args.lane,)
    public = {
        "schema": "DIPLOMACY_COMBINED_NATIVE_ACCEPTANCE_V1",
        "source": "final coordinator-supplied DLL, game, MVID and fixtures",
        "output_scope": str(ROOT),
        "selected_lanes": list(lanes),
        "dll_sha256": pins["dll"],
        "verified_MVID": ctx["mvid"],
        "trade_fixture_sha256": pins["trade_fixture"],
        "aid_fixture_sha256": pins["aid_fixture"],
        "original_file_paths": {
            "dll": str(ctx["dll"]), "trade_fixture": str(ctx["trade"]),
            "aid_fixture": str(ctx["aid"])},
        "mode": args.render_mode,
        "no_screenshots_or_injected_inputs": True,
        "steam_multiplayer_or_release_authorized": False,
    }
    if args.operation == "preflight":
        public["status"] = "READ_ONLY_PREFLIGHT_PASS_NO_GAME_START"
        print(json.dumps(public, ensure_ascii=False, indent=2))
        return 0

    insist(args.authorize_live_run == "COORDINATOR_EXCLUSIVE_GAME_RUN",
           "no live run without explicit coordinator authorization")
    insist(args.run_id and RUN_ID.fullmatch(args.run_id),
           "a unique safe --run-id is mandatory")
    output = ROOT / "runs" / args.run_id
    insist(not is_reparse(output), "output path includes a reparse point")
    output.mkdir(parents=True, exist_ok=False)
    result = dict(public, run_id=args.run_id, status="FAIL_CLOSED",
                  trade=None, aid=None, error=None)
    try:
        no_running_crusader()
        for lane in lanes:
            result[lane] = run_lane(ctx, args, lane, output)
        insist(all(result[lane] is not None and result[lane]["passed"]
                   for lane in lanes), "selected lane is not proven")
        result["status"] = ("TWO_LANES_ACCEPTED_PRIVATE_QA_ONLY"
                            if args.lane == "both" else
                            args.lane.upper() + "_LANE_ACCEPTED_PRIVATE_QA_ONLY")
        return_code = 0
    except Exception as error:
        result["error"] = type(error).__name__ + ": " + str(error)
        return_code = 2
    result_path = output / "run_result.json"
    payload = (json.dumps(result, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    with result_path.open("xb") as file:
        file.write(payload)
    (output / "run_result.json.sha256").write_text(
        hashlib.sha256(payload).hexdigest() + "  run_result.json\n",
        encoding="ascii")
    print(json.dumps({"status": result["status"], "output": str(result_path),
                      "error": result["error"]}, ensure_ascii=False))
    return return_code


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print("ACCEPTANCE_REFUSED: " + type(error).__name__ + ": " + str(error),
              file=sys.stderr)
        sys.exit(2)
