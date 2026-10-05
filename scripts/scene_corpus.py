#!/usr/bin/env python3
"""Collect confirmed area RAM/scene fixtures through an opt-in local game shell."""
import argparse
import json
import socket
import time
from pathlib import Path


def command(port, text, timeout=8):
    with socket.create_connection(("127.0.0.1", port), timeout=timeout) as client:
        client.sendall((text + "\n").encode())
        data = b""
        while b"\n" not in data:
            block = client.recv(65536)
            if not block:
                raise OSError("shell disconnected")
            data += block
        reply = json.loads(data.split(b"\n", 1)[0])
        if not reply.get("ok"):
            raise RuntimeError(f"{text}: {reply}")
        return reply


def wait_state(port, area=None, seconds=20):
    until = time.monotonic() + seconds
    last = {}
    while time.monotonic() < until:
        try:
            last = command(port, "state")["state"]
            if (last.get("inGame") and last.get("loop") and
                    (area is None or last.get("area") == area and last.get("overlay") == f"fdat{area * 3 + 2:02d}")):
                return last
        except (OSError, RuntimeError):
            pass
        time.sleep(0.25)
    raise RuntimeError(f"area {area} did not arrive: {last}")


def cleanup(port, original_yaw=None, guest_yaw=False, timeout=2.0):
    """Release the yaw and render overrides after the corpus, best effort.

    Restores the guest view yaw the corpus found, clears its yaw hold and
    turns the shell view override off; player physics remains held. A shell that did not answer is left
    alone: an OSError (timeout or disconnect) ends cleanup, so no further
    commands queue behind an unanswered one. A RuntimeError is the server
    answering and refusing -- it is responsive, so cleanup carries on.
    """
    commands = []
    if guest_yaw:
        if original_yaw is not None:
            commands.append(f"scene-yaw {original_yaw & 4095}")
        commands.append("scene-yaw off")
    commands.append("view off")
    for text in commands:
        try:
            command(port, text, timeout=timeout)
        except OSError:
            return
        except RuntimeError:
            continue


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=27903)
    parser.add_argument("--areas", default=",".join(map(str, range(28))))
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--headings", type=int, default=1)
    parser.add_argument("--guest-yaw", action="store_true",
                        help="also hold the guest view yaw (scene-yaw) with each heading, so the "
                             "front submit sees the heading; off keeps the old render-only behavior")
    args = parser.parse_args()
    if not 1 <= args.headings <= 4096:
        parser.error("--headings must be 1..4096")
    args.output.mkdir(parents=True, exist_ok=True)
    report = []
    original = wait_state(args.port, seconds=50)
    original_yaw = original.get("yaw")
    for area in map(int, args.areas.split(",")):
        try:
            command(args.port, "view off")
            command(args.port, f"warp {area}")
            state = wait_state(args.port, area)
            time.sleep(0.5)
            state = wait_state(args.port, area)
            command(args.port, "dump " + str((args.output / f"area{area:02d}.ram").resolve()))
            view = command(args.port, "view")["camera"]
            yaws = []
            for i in range(args.headings):
                yaw = (view[4] + i * 4096 // args.headings) & 4095
                yaws.append(yaw)
                if args.guest_yaw:
                    command(args.port, f"scene-yaw {yaw}")
                camera = view[:4] + [yaw, view[5]]
                command(args.port, "view " + " ".join(map(str, camera)))
                time.sleep(0.5)
            row = {"requested": area, "confirmed": True, "state": state, "camera": view,
                   "headings": args.headings, "yaws": yaws, "guestYaw": args.guest_yaw,
                   "playerPhysicsHeld": True, "context": "area-corpus"}
            print(f"confirmed {area:02d}/{state['overlay']}, hp={state['hp']}, headings={args.headings}", flush=True)
        except (OSError, RuntimeError) as error:
            row = {"requested": area, "confirmed": False, "error": str(error)}
            print(f"unresolved {area:02d}: {error}", flush=True)
            report.append(row)
            (args.output / "coverage.json").write_text(json.dumps(report, indent=2) + "\n")
            # An unanswered command may still be queued: add no more after a
            # timeout/disconnect. A refused command is safe to clean up after.
            if isinstance(error, RuntimeError):
                cleanup(args.port, original_yaw, args.guest_yaw)
            raise
        report.append(row)
        (args.output / "coverage.json").write_text(json.dumps(report, indent=2) + "\n")
    cleanup(args.port, original_yaw, args.guest_yaw)


if __name__ == "__main__":
    main()
