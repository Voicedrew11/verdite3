#!/usr/bin/env python3
"""Inventory generated drawing callers; write reports only to ignored scratch/."""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TARGETS = {
    "800422B8": "scene frame (including modal and area-script redraws)",
    "800357E8": "camera",
    "80035630": "table and primitive reset",
    "80035700": "presentation and front-table splice",
    "8003BFD0": "map walk",
    "8003BE34": "cell helper (no direct static callers)",
    "8003BB04": "tile half",
    "80040AE4": "creature/object/effect/billboard enumeration",
    "8003E34C": "main model submit",
    "8003F304": "front-table model submit",
    "800400AC": "sky submit",
    "8003DF50": "view-space arm",
    "80039D50": "bulk map assembler",
    "8003AB04": "near map assembler",
    "80035CA4": "lit assembler shared by world, HUD and preview",
    "800366A8": "near model assembler",
    "80037BEC": "forced-blend assembler",
    "80038844": "front-table assembler",
    "80039428": "sky assembler",
    "8003C35C": "packet-owned HUD models",
    "8004290C": "packet-owned standalone preview",
    "80041E68": "packet-owned HUD icons",
    "80041D9C": "packet-owned overlay",
    "8003D280": "packet-owned screen effect 0",
    "8003D38C": "packet-owned screen effect 1",
    "8003D41C": "packet-owned screen effect 2",
    "8003D568": "packet-owned screen effect 3",
    "8003D64C": "packet-owned screen effect 4",
    "8003D79C": "packet-owned screen effect 5",
    "80074D88": "triangle division entry",
    "80075188": "quad division entry",
    "800756A8": "triangle division body",
    "80075B48": "quad division body",
}


def inventory(root):
    targets = {"func_" + a: {"address": a, "owner": owner, "callers": []}
               for a, owner in TARGETS.items()}
    sdk, indirect, areas = [], [], []
    for path in sorted((root / "generated").glob("*.cs")):
        text = path.read_text()
        marks = list(re.finditer(r"public static void (\w+)\(CpuContext c, IMemory m\)", text))
        if path.stem.startswith("fdat"):
            areas.append({"overlay": path.stem, "functions": len(marks)})
        for i, mark in enumerate(marks):
            body = text[mark.end():marks[i + 1].start() if i + 1 < len(marks) else len(text)]
            caller = {"overlay": path.stem, "function": mark[1]}
            for call in re.finditer(r"KingsField3_\w+\.(\w+)\(c, m\)", body):
                if call[1] in targets:
                    ras = re.findall(r"c\.RA = (0x[0-9A-Fa-f]+)u;", body[:call.start()])
                    targets[call[1]]["callers"].append({**caller, "site": hex(int(ras[-1], 16) - 8) if ras else None})
            for call in re.finditer(r"RecompOne\.Runtime\.Sdk\.Lib(\w+)\.(\w+)\(c, m\)", body):
                sdk.append({**caller, "library": call[1], "entry": call[2]})
            for call in re.finditer(r"Dispatcher\.Call\(c, m, ([^)]+)\)", body):
                indirect.append({**caller, "target": call[1]})
    return {"targets": targets, "sdk": sdk, "dispatch_calls": indirect, "areas": areas,
            "limits": "Static calls and dispatch expressions only. Guest function pointers, direct packet writes, runtime content and reachability require the scene census."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "scratch/drawing-inventory.json")
    args = parser.parse_args()
    result = inventory(ROOT)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n")
    print(f"{len(result['targets'])} drawing targets, {len(result['areas'])} area modules; {args.output}")
    for name, row in result["targets"].items():
        print(f"{name}: {row['owner']}; {len(row['callers'])} call sites")


if __name__ == "__main__":
    main()
