#!/usr/bin/env python3
"""Collect confirmed area RAM/scene fixtures through an opt-in local game shell."""
import argparse
import json
import socket
import time
from pathlib import Path


def command(port, text):
    with socket.create_connection(("127.0.0.1", port), timeout=8) as client:
        client.sendall((text + "\n").encode())
        data = b""
        while b"\n" not in data:
            block = client.recv(65536)
            if not block:
                raise RuntimeError("shell disconnected")
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=27903)
    parser.add_argument("--areas", default=",".join(map(str, range(28))))
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--headings", type=int, default=1)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    report = []
    wait_state(args.port, seconds=50)
    for area in map(int, args.areas.split(",")):
        try:
            command(args.port, "view off")
            command(args.port, f"warp {area}")
            state = wait_state(args.port, area)
            time.sleep(0.5)
            state = wait_state(args.port, area)
            command(args.port, "dump " + str((args.output / f"area{area:02d}.ram").resolve()))
            view = command(args.port, "view")["camera"]
            for i in range(args.headings):
                camera = view[:4] + [(view[4] + i * 4096 // args.headings) & 4095, view[5]]
                command(args.port, "view " + " ".join(map(str, camera)))
                time.sleep(0.5)
            row = {"requested": area, "confirmed": True, "state": state, "camera": view,
                   "headings": args.headings, "playerPhysicsHeld": True, "context": "area-corpus"}
            print(f"confirmed {area:02d}/{state['overlay']}, hp={state['hp']}, headings={args.headings}", flush=True)
        except (OSError, RuntimeError) as error:
            row = {"requested": area, "confirmed": False, "error": str(error)}
            print(f"unresolved {area:02d}: {error}", flush=True)
            report.append(row)
            (args.output / "coverage.json").write_text(json.dumps(report, indent=2) + "\n")
            # A timed-out command can remain queued. Stop rather than filling the queue.
            raise
        report.append(row)
        (args.output / "coverage.json").write_text(json.dumps(report, indent=2) + "\n")
    command(args.port, "view off")


if __name__ == "__main__":
    main()
