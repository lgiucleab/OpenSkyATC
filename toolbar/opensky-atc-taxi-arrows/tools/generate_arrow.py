"""Generates the OpenSkyATC taxi arrow model (original work, GPL-3.0).

A flat chevron, 3 m long and 2 m wide, lying 5 cm above the ground, pointing along +Z (the SimObject's forward
axis), blue and slightly emissive so it stays visible at night. Writes TaxiArrow.gltf and TaxiArrow.bin into
PackageSources/SimObjects/Misc/OpenSkyATC_TaxiArrow/model. Run: python generate_arrow.py
"""
import json
import struct
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "PackageSources" / "SimObjects" / "Misc" / "OpenSkyATC_TaxiArrow" / "model"
HEIGHT = 0.05  # metres above the ground, to avoid flickering with the taxiway surface

# Chevron outline (x right, z forward), metres: tip, right arm, inner notch, left arm.
TIP, RIGHT_OUT, RIGHT_BACK, NOTCH, LEFT_BACK, LEFT_OUT = (0, 1.6), (1, 0.0), (1, -1.4), (0, 0.2), (-1, -1.4), (-1, 0.0)
POINTS = [TIP, RIGHT_OUT, RIGHT_BACK, NOTCH, LEFT_BACK, LEFT_OUT]
# Two convex arms, two triangles each, wound counter-clockwise seen from above (+Y).
TRIANGLES = [(0, 2, 1), (0, 3, 2), (0, 5, 4), (0, 4, 3)]


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    positions = [(x, HEIGHT, z) for x, z in POINTS]
    normals = [(0.0, 1.0, 0.0)] * len(positions)
    indices = [i for tri in TRIANGLES for i in tri]

    pos_bytes = b"".join(struct.pack("<3f", *p) for p in positions)
    nrm_bytes = b"".join(struct.pack("<3f", *n) for n in normals)
    idx_bytes = b"".join(struct.pack("<H", i) for i in indices)
    idx_bytes += b"\0" * (-len(idx_bytes) % 4)
    blob = pos_bytes + nrm_bytes + idx_bytes
    (OUT / "TaxiArrow.bin").write_bytes(blob)

    xs, zs = [p[0] for p in positions], [p[2] for p in positions]
    gltf = {
        "asset": {"version": "2.0", "generator": "OpenSkyATC generate_arrow.py", "copyright": "OpenSkyATC, GPL-3.0"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": "TaxiArrow", "mesh": 0}],
        "meshes": [{"name": "TaxiArrow", "primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1}, "indices": 2, "material": 0}]}],
        "materials": [{
            "name": "TaxiArrowBlue",
            "doubleSided": True,
            "pbrMetallicRoughness": {"baseColorFactor": [0.05, 0.35, 1.0, 1.0], "metallicFactor": 0.0, "roughnessFactor": 0.6},
            "emissiveFactor": [0.0, 0.22, 0.85],
        }],
        "buffers": [{"uri": "TaxiArrow.bin", "byteLength": len(blob)}],
        "bufferViews": [
            {"buffer": 0, "byteOffset": 0, "byteLength": len(pos_bytes), "target": 34962},
            {"buffer": 0, "byteOffset": len(pos_bytes), "byteLength": len(nrm_bytes), "target": 34962},
            {"buffer": 0, "byteOffset": len(pos_bytes) + len(nrm_bytes), "byteLength": len(indices) * 2, "target": 34963},
        ],
        "accessors": [
            {"bufferView": 0, "componentType": 5126, "count": len(positions), "type": "VEC3",
             "min": [min(xs), HEIGHT, min(zs)], "max": [max(xs), HEIGHT, max(zs)]},
            {"bufferView": 1, "componentType": 5126, "count": len(normals), "type": "VEC3"},
            {"bufferView": 2, "componentType": 5123, "count": len(indices), "type": "SCALAR"},
        ],
    }
    (OUT / "TaxiArrow.gltf").write_text(json.dumps(gltf, indent=2), encoding="utf-8")
    print(f"Wrote {OUT / 'TaxiArrow.gltf'} ({len(positions)} vertices, {len(TRIANGLES)} triangles)")


if __name__ == "__main__":
    main()
