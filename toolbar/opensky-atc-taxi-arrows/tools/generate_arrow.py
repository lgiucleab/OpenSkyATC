"""Generates the OpenSkyATC taxi arrow model (original work, GPL-3.0).

A flat chevron, 3 m long and 2 m wide, lying 5 cm above the ground, pointing along +Z (the SimObject's forward
axis; +X is left, +Y up), blue and slightly emissive so it stays visible at night. Writes TaxiArrow.gltf and
TaxiArrow.bin into PackageSources/SimObjects/Misc/OpenSkyATC_TaxiArrow/model. Run: python generate_arrow.py

The sim only draws models in its "optimized" glTF layout (what the SDK package tool produces; a plain glTF
spawns but is invisible): the asset carries ASOBO_asset_optimized, and vertices are interleaved, 36 bytes each:
position 3 x float32, tangent 4 x int8, normal 4 x int8, two UV sets 2 x float16 each, colour 4 x float16.
"""
import json
import struct
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "PackageSources" / "SimObjects" / "Misc" / "OpenSkyATC_TaxiArrow" / "model"
HEIGHT = 0.05  # metres above the ground, to avoid flickering with the taxiway surface
STRIDE = 36

# Chevron outline (x, z) in metres: tip, one arm, inner notch, other arm.
POINTS = [(0, 1.6), (1, 0.0), (1, -1.4), (0, 0.2), (-1, -1.4), (-1, 0.0)]
# Two convex arms, two triangles each; winding is fixed below so every face points up (+Y).
TRIANGLES = [(0, 1, 2), (0, 2, 3), (0, 3, 4), (0, 4, 5)]


def facing_up(tri: tuple[int, int, int]) -> tuple[int, int, int]:
    (ax, az), (bx, bz), (cx, cz) = (POINTS[i] for i in tri)
    ux, uz, vx, vz = bx - ax, bz - az, cx - ax, cz - az
    normal_y = uz * vx - ux * vz  # y component of (b - a) x (c - a)
    return tri if normal_y > 0 else (tri[0], tri[2], tri[1])


def vertex(x: float, z: float) -> bytes:
    return (
        struct.pack("<3f", x, HEIGHT, z)
        + struct.pack("<4b", 127, 0, 0, 127)       # tangent +X
        + struct.pack("<4b", 0, 127, 0, 127)       # normal +Y
        + struct.pack("<2e", (x + 1) / 2, (z + 1.4) / 3)  # UV 0 (unused, no textures)
        + struct.pack("<2e", 0.0, 0.0)             # UV 1
        + struct.pack("<4e", 1.0, 1.0, 1.0, 1.0)   # vertex colour white
    )


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    triangles = [facing_up(t) for t in TRIANGLES]
    indices = [i for tri in triangles for i in tri]

    vertices = b"".join(vertex(x, z) for x, z in POINTS)
    index_bytes = b"".join(struct.pack("<H", i) for i in indices)
    index_bytes += b"\0" * (-len(index_bytes) % 4)
    blob = vertices + index_bytes
    (OUT / "TaxiArrow.bin").write_bytes(blob)

    xs, zs = [p[0] for p in POINTS], [p[1] for p in POINTS]
    box_min, box_max = [min(xs), HEIGHT, min(zs)], [max(xs), HEIGHT, max(zs)]
    count = len(POINTS)
    gltf = {
        "asset": {
            "version": "2.0",
            "generator": "OpenSkyATC generate_arrow.py",
            "copyright": "OpenSkyATC, GPL-3.0",
            "extensions": {
                "ASOBO_asset_optimized": {
                    "BoundingBoxMax": box_max,
                    "BoundingBoxMin": box_min,
                    "MajorVersion": 4,
                    "MinorVersion": 6,
                    "UseCheckerboardMaterialForMissingTextures": True,
                    "UseOnlyFilenameForImageURI": True,
                },
                "ASOBO_normal_map_convention": {"tangent_space_convention": "DirectX"},
            },
        },
        "extensionsUsed": ["ASOBO_normal_map_convention", "ASOBO_asset_optimized"],
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": "TaxiArrow", "mesh": 0}],
        "meshes": [{
            "name": "TaxiArrow",
            "primitives": [{
                "attributes": {"POSITION": 0, "TANGENT": 1, "NORMAL": 2, "TEXCOORD_0": 3, "TEXCOORD_1": 4, "COLOR_0": 5},
                "indices": 6,
                "material": 0,
                "mode": 4,
                "extras": {"ASOBO_primitive": {
                    "PrimitiveCount": len(triangles),
                    "VertexCount": count,
                    "VertexType": "VTX",
                    "VertexVersion": 2,
                    "UVLength": 1.0,
                    "UV2Length": 1.0,
                }},
            }],
        }],
        "materials": [{
            "name": "TaxiArrowBlue",
            "doubleSided": True,
            "pbrMetallicRoughness": {"baseColorFactor": [0.05, 0.35, 1.0, 1.0], "metallicFactor": 0.0, "roughnessFactor": 0.6},
            "emissiveFactor": [0.0, 0.22, 0.85],
        }],
        "buffers": [{"uri": "TaxiArrow.bin", "byteLength": len(blob)}],
        "bufferViews": [
            {"buffer": 0, "byteLength": len(vertices), "byteStride": STRIDE, "target": 34962, "name": "BufferViewVertexND"},
            {"buffer": 0, "byteOffset": len(vertices), "byteLength": len(indices) * 2, "target": 34963, "name": "BufferViewIndexU16"},
        ],
        "accessors": [
            {"bufferView": 0, "byteOffset": 0, "componentType": 5126, "count": count, "type": "VEC3", "min": box_min, "max": box_max},
            {"bufferView": 0, "byteOffset": 12, "componentType": 5120, "count": count, "type": "VEC4"},
            {"bufferView": 0, "byteOffset": 16, "componentType": 5120, "count": count, "type": "VEC4"},
            {"bufferView": 0, "byteOffset": 20, "componentType": 5122, "count": count, "type": "VEC2"},
            {"bufferView": 0, "byteOffset": 24, "componentType": 5122, "count": count, "type": "VEC2"},
            {"bufferView": 0, "byteOffset": 28, "componentType": 5123, "count": count, "type": "VEC4"},
            {"bufferView": 1, "componentType": 5123, "count": len(indices), "type": "SCALAR"},
        ],
    }
    (OUT / "TaxiArrow.gltf").write_text(json.dumps(gltf, indent=2), encoding="utf-8")
    print(f"Wrote {OUT / 'TaxiArrow.gltf'} ({count} vertices, {len(triangles)} triangles, optimized layout)")


if __name__ == "__main__":
    main()
