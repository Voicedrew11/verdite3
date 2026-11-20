#!/usr/bin/env python3
"""Warp through areas and look at the live creatures/objects nearest each arrival
from several sides, reading the retained model-mask counters per view.

Needs one copied-state game with KF3_SHELL=1 KF3_SCENE_DRIVER=1 KF3_GPU_WORLD=1
KF3_GPU_MASK_PROBE=1 (see docs/GPU_RENDERER.md, "Models under later packets").
Usage: model_mask_tour.py <output-dir> [areas=0,1,...] [things-per-area=6]
Writes <output-dir>/tour.json (one row per view) and a RAM dump per area."""
import json, math, socket, struct, sys, time
from pathlib import Path

PORT = 27903
OUT = Path(sys.argv[1])
AREAS = [int(a) for a in sys.argv[2].split(",")] if len(sys.argv) > 2 else list(range(28))
PER_AREA = int(sys.argv[3]) if len(sys.argv) > 3 else 6
DIST = (900, 2000)
HEADINGS = 4
SETTLE = 0.6


def cmd(text, timeout=8):
    with socket.create_connection(("127.0.0.1", PORT), timeout=timeout) as c:
        c.sendall((text + "\n").encode())
        data = b""
        while b"\n" not in data:
            b = c.recv(65536)
            if not b:
                raise OSError("disconnected")
            data += b
    r = json.loads(data.split(b"\n", 1)[0])
    if not r.get("ok"):
        raise RuntimeError(f"{text}: {r}")
    return r


def wait_area(area, seconds=25):
    until = time.monotonic() + seconds
    last = {}
    while time.monotonic() < until:
        try:
            last = cmd("state")["state"]
            if last.get("inGame") and last.get("loop") and (area is None or (
                    last.get("area") == area and last.get("overlay") == f"fdat{area * 3 + 2:02d}")):
                return last
        except (OSError, RuntimeError):
            pass
        time.sleep(0.25)
    raise RuntimeError(f"area {area} did not arrive: {last}")


def things(ram):
    out = []
    for i in range(200):  # creatures: 0x80185DA8, 0x88, live u8[+9]==1, position +0x2C
        b = 0x185DA8 + i * 0x88
        if ram[b + 9] == 1:
            out.append(("creature", i, struct.unpack_from("<iii", ram, b + 0x2C)))
    for i in range(396):  # objects: 0x80191A5C, 0x44, live u16[+6]!=0xFF, position +0x14
        b = 0x191A5C + i * 0x44
        if struct.unpack_from("<H", ram, b + 6)[0] != 0xFF and ram[b + 4] not in (0x1F, 0xF0):
            out.append(("object", i, struct.unpack_from("<iii", ram, b + 0x14)))
    return out


def forward(yaw):
    """World direction of view +z for a yaw, read from the view matrix."""
    cmd(f"view 0 0 0 0 {yaw} 0")
    time.sleep(0.6)
    h = cmd("peek 801AEB4C 18")["hex"]
    r = struct.unpack("<9h", bytes.fromhex(h))
    return r[6] / 4096, r[8] / 4096


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    rows = []
    wait_area(None, 120)
    # The yaw that faces each world direction: calibrate two quarter turns.
    f0, f1 = forward(0), forward(1024)
    print("forward(0)", f0, "forward(1024)", f1, flush=True)
    cmd("view off")
    for area in AREAS:
        try:
            cmd("view off")
            cmd(f"warp {area}")
            st = wait_area(area)
            time.sleep(1.0)
            st = wait_area(area)
            ram_path = (OUT / f"area{area:02d}.ram").resolve()
            cmd(f"dump {ram_path}")
            ram = ram_path.read_bytes()
            cam = cmd("view")["camera"]
            eye = cam[1] - st["pos"][1]
            ts = things(ram)
            # Nearest the arrival point first.
            px, pz = st["pos"][0], st["pos"][2]
            ts.sort(key=lambda t: (t[0] != "creature", (t[2][0] - px) ** 2 + (t[2][2] - pz) ** 2))
            for kind, idx, (x, y, z) in ts[:PER_AREA]:
                for d in DIST:
                    for h in range(HEADINGS):
                        yaw = (h * 4096 // HEADINGS) & 4095
                        a = yaw / 4096 * 2 * math.pi
                        # forward(yaw) rotates with yaw from forward(0) towards forward(1024).
                        fx = f0[0] * math.cos(a) + f1[0] * math.sin(a)
                        fz = f0[1] * math.cos(a) + f1[1] * math.sin(a)
                        cx, cz = int(x - fx * d), int(z - fz * d)
                        cmd(f"view {cx} {y + eye} {cz} {cam[3]} {yaw} {cam[5]}")
                        time.sleep(SETTLE)
                        before = cmd("gpu")
                        time.sleep(SETTLE)
                        after = cmd("gpu")
                        row = {"area": area, "kind": kind, "index": idx, "pos": [x, y, z], "dist": d, "yaw": yaw,
                               "camera": [cx, y + eye, cz],
                               **{k: after[k] - before[k] for k in
                                  ("mainDraws", "instances", "maskFrames", "maskBatches", "maskSamples", "maskBehind", "maskAhead",
                                   "modelSamples", "modelUnderMap")},
                               "underSlack": [a - b for a, b in zip(after["modelUnderSlack"], before["modelUnderSlack"])]}
                        rows.append(row)
            ar = [r for r in rows if r.get("area") == area and "dist" in r]
            tot = lambda k: sum(r[k] for r in ar)
            under = [sum(r["underSlack"][i] for r in ar) for i in range(5)]
            worst = max(ar, key=lambda r: r["maskBehind"], default=None)
            print(f"area {area:02d}: {len(ts)} things, {len(ar)} views, model samples {tot('modelSamples')}, "
                  f"under map by slack 8/32/128/512/960 {under}, packets behind {tot('maskBehind')}, "
                  f"blended ahead {tot('maskAhead')}; most behind {worst and (worst['kind'], worst['index'], worst['dist'], worst['yaw'], worst['maskBehind'])}",
                  flush=True)
        except (OSError, RuntimeError) as e:
            print(f"area {area:02d} failed: {e}", flush=True)
            rows.append({"area": area, "error": str(e)})
            if isinstance(e, OSError):
                break
        (OUT / "tour.json").write_text(json.dumps(rows, indent=1))
    try:
        cmd("view off")
    except Exception:
        pass
    (OUT / "tour.json").write_text(json.dumps(rows, indent=1))


if __name__ == "__main__":
    main()
