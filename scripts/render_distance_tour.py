#!/usr/bin/env python3
"""Warp through areas and read RenderDistance's counters from four level headings and
two pitched ones at each arrival, with the retained renderer's draws and misses.

Needs one copied-state game with KF3_SHELL=1 KF3_SCENE_DRIVER=1 and
KF3_RENDERDIST_PROBE=1 (the walk's halves are checked against the game's predicted
reach only while probing). See docs/WIDESCREEN.md, "Render distance".
Usage: render_distance_tour.py <output-dir> [areas=0,1,...] [tiles=30] [fade=3]
(model_mask_tour reads the same argv when imported: name the areas.)
Writes <output-dir>/render-distance.json, one row per view."""
import json, sys, time
from pathlib import Path

from model_mask_tour import cmd, wait_area

OUT = Path(sys.argv[1])
AREAS = [int(a) for a in sys.argv[2].split(",")] if len(sys.argv) > 2 else list(range(28))
TILES = sys.argv[3] if len(sys.argv) > 3 else "30"
FADE = sys.argv[4] if len(sys.argv) > 4 else "3"
# Level at four headings, then looking down and up (pitch is 0x1000 a turn).
VIEWS = [(0, 0), (0, 1024), (0, 2048), (0, 3072), (0x300, 512), (0xD00, 1536)]
SETTLE, SAMPLE = 0.6, 1.0
DELTAS = ("mainDraws", "mainMissed", "rdFrames", "rdAdded", "rdChecked", "rdWalked", "rdOutsideReach", "rdOverlap",
          "rdModels", "rdRefused", "rdCreaturesFaded")
LEVELS = ("rdRadius", "rdT5", "rdEdge", "rdGameEdge", "rdMaxDepth")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    rows = []
    wait_area(None, 120)
    print(cmd(f"renderdist {TILES} {FADE}"), flush=True)
    for area in AREAS:
        try:
            cmd("view off")
            cmd(f"warp {area}")
            wait_area(area)
            time.sleep(1.0)
            wait_area(area)
            cam = cmd("view")["camera"]
            for pitch, yaw in VIEWS:
                cmd(f"view {cam[0]} {cam[1]} {cam[2]} {pitch} {yaw} {cam[5]}")
                time.sleep(SETTLE)
                before = cmd("gpu")
                time.sleep(SAMPLE)
                after = cmd("gpu")
                row = {"area": area, "pitch": pitch, "yaw": yaw, "camera": cam[:3],
                       **{k: after[k] - before[k] for k in DELTAS}, **{k: after[k] for k in LEVELS}}
                rows.append(row)
            ar = [r for r in rows if r.get("area") == area and "yaw" in r]
            tot = lambda k: sum(r[k] for r in ar)
            frames = max(1, tot("rdFrames"))
            print(f"area {area:02d}: radius {ar[0]['rdRadius']}, game edge {ar[0]['rdGameEdge'] / 2048:.2f} tiles, "
                  f"draws {tot('mainDraws')} missed {tot('mainMissed')}, added/frame {tot('rdAdded') / frames:.0f}, "
                  f"walked/frame {tot('rdWalked') / max(1, tot('rdChecked')):.0f}, outside reach {tot('rdOutsideReach')}, "
                  f"overlap {tot('rdOverlap')}, max depth {max(r['rdMaxDepth'] for r in ar)}, "
                  f"models past reach/frame {tot('rdModels') / frames:.1f}, refused {tot('rdRefused')}, creatures faded {tot('rdCreaturesFaded')}", flush=True)
        except (OSError, RuntimeError) as e:
            print(f"area {area:02d} failed: {e}", flush=True)
            rows.append({"area": area, "error": str(e)})
            if isinstance(e, OSError):
                break
        (OUT / "render-distance.json").write_text(json.dumps(rows, indent=1))
    try:
        cmd("view off")
    except Exception:
        pass
    (OUT / "render-distance.json").write_text(json.dumps(rows, indent=1))


if __name__ == "__main__":
    main()
