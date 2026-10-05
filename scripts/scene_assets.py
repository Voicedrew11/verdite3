#!/usr/bin/env python3
"""Inventory source records in confirmed RAM fixtures; discovery is not execution."""
import argparse
import json
import struct
from collections import Counter
from pathlib import Path


def inspect(path):
    ram = path.read_bytes()
    if len(ram) != 0x200000:
        raise ValueError(f"{path}: expected 2 MB")

    def valid(address, size=4):
        address &= 0x1fffffff
        return address < len(ram) and 0 <= size <= len(ram) - address

    def u32(address):
        if not valid(address):
            raise ValueError(f"outside RAM: {address:x}")
        return struct.unpack_from("<I", ram, address & 0x1fffff)[0]

    def mesh(table, index):
        header = table + 12 + index * 28
        words = [u32(header + i * 4) for i in range(7)]
        vertices, nvertices, normals, nnormals, faces, nfaces, extra = words
        if nvertices > 8192 or nfaces > 4096 or not valid(table + 12 + vertices, nvertices * 8):
            raise ValueError("implausible mesh header")
        at = table + 12 + faces
        commands = Counter()
        sizes = Counter()
        for _ in range(nfaces):
            word = u32(at)
            size = (word >> 6) & 0x3fc
            if not valid(at + 4, size):
                raise ValueError("face body outside RAM")
            commands[f"{word >> 24:02X}"] += 1
            sizes[f"{size:02X}"] += 1
            at += 4 + size
        return dict(index=index, header=f"{header:08X}", vertices=nvertices,
                    normals=nnormals, faces=nfaces, commands=dict(commands), bodyBytes=dict(sizes),
                    sourceBytes=at - (table + 12 + faces), extra=extra)

    used = Counter()
    halves = 0
    heights, rotations, lights = Counter(), Counter(), Counter()
    for at in range(0x1d4464, 0x1d4464 + 64000, 5):
        kind, height, rotation, flags, light = ram[at:at + 5]
        if kind >= 240:
            continue
        used[kind] += 1
        heights[height] += 1
        rotations[rotation & 3] += 1
        lights[light & 63] += 1
        halves += 1
    table = u32(0x801a929c)
    map_meshes = []
    for kind in sorted(used):
        try:
            item = mesh(table, kind)
            item["halves"] = used[kind]
            map_meshes.append(item)
        except ValueError as error:
            map_meshes.append(dict(index=kind, error=str(error)))
    banks = []
    for index in range(256):
        address = u32(0x801a92b0 + index * 4)
        if address == 0 or not valid(address, 20):
            continue
        table = address + u32(address + 8)
        if not valid(table, 40):
            continue
        try:
            base = mesh(table, 0)
            if base["vertices"] == 0 or base["faces"] == 0:
                continue
            banks.append(dict(bank=index, address=f"{address:08X}",
                              clipMetadata=u32(address + 4), firstMesh=base))
        except ValueError:
            pass
    return dict(fixture=path.name, discoveryOnly=True, area=ram[0x18fae4], halves=halves,
                heights=dict(heights), rotations=dict(rotations), lights=dict(lights),
                mapMeshes=map_meshes, candidateBanks=banks)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("fixtures", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    reports = [inspect(path) for path in sorted(args.fixtures.glob("area*.ram"))]
    args.output.write_text(json.dumps(reports, indent=2) + "\n")
    for row in reports:
        print(f"{row['fixture']}: area {row['area']}, {row['halves']} halves, "
              f"{len(row['mapMeshes'])} used meshes, {len(row['candidateBanks'])} candidate banks; discovery only")


if __name__ == "__main__":
    main()
