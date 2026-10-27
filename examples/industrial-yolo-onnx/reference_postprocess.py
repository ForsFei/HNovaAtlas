"""Numerical reference from the previous batch's fixed raw fixture (no installs/network).

Core decode/score/+1 class-agnostic NMS follows the documented upstream policy.
Equal scores explicitly prefer smaller row id, rather than NumPy's default unstable argsort.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument("raw_reference", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
if args.raw_reference.stat().st_size != 3549 * 85 * 4:
    raise ValueError("Expected previous batch's301665 little-endian raw floats")
raw_bytes = args.raw_reference.read_bytes()
raw = np.frombuffer(raw_bytes, dtype="<f4").reshape(3549, 85)
if not np.isfinite(raw).all() or ((raw[:, 4:] < 0) | (raw[:, 4:] > 1)).any():
    raise ValueError("Invalid raw fixture")
levels = []
strides = []
for stride in (8, 16, 32):
    size = 416 // stride
    x, y = np.meshgrid(np.arange(size), np.arange(size))
    levels.append(np.stack((x, y), axis=-1).reshape(-1, 2))
    strides.append(np.full((size * size, 1), stride))
grid = np.concatenate(levels)
stride_values = np.concatenate(strides)
decoded = raw[:, :4].copy()
decoded[:, :2] = (raw[:, :2] + grid) * stride_values
decoded[:, 2:] = np.exp(raw[:, 2:4]) * stride_values
boxes = np.empty_like(decoded)
boxes[:, :2] = decoded[:, :2] - decoded[:, 2:] / 2
boxes[:, 2:] = decoded[:, :2] + decoded[:, 2:] / 2
products = raw[:, 4:5] * raw[:, 5:]
classes = products.argmax(axis=1)
scores = products[np.arange(3549), classes]
threshold = np.float32(.01)
candidates = np.flatnonzero(scores > threshold)
order = candidates[np.lexsort((candidates, -scores[candidates]))]
# Float64 NMS geometry is an explicit local numerical policy, matching the C# implementation.
geometry = boxes.astype(np.float64)
kept = []
while len(order):
    current = int(order[0])
    kept.append(current)
    rest = order[1:]
    first = geometry[current]
    others = geometry[rest]
    wh = np.maximum(0, np.minimum(first[2:], others[:, 2:]) - np.maximum(first[:2], others[:, :2]) + 1)
    intersection = wh[:, 0] * wh[:, 1]
    area_a = np.prod(first[2:] - first[:2] + 1)
    area_b = np.prod(others[:, 2:] - others[:, :2] + 1, axis=1)
    overlap = intersection / (area_a + area_b - intersection)
    order = rest[overlap <= .45]
source_boxes = []
clipped = []
for row in kept:
    network = geometry[row]
    source = np.array([max(0, network[0]), max(0, network[1]), min(416, network[2]), min(208, network[3])])
    source_boxes.append(source.tolist() if source[2] > source[0] and source[3] > source[1] else None)
    clipped.append(bool(source_boxes[-1] is None or not np.array_equal(source, network)))
rows = np.column_stack((boxes, classes, scores)).tolist()
document = {"modelSha256": "C789161ED43C8269FCD4E67C67EEEB4E80C622DA2EB296A20BC6007BD18A0B7D",
            "inputSha256": "5F598DA750EAE012350FC7B4B49B154C609104EE2B65A9AA3AC5D51C7C469863",
            "sourceHeight": 208, "scoreThreshold": float(threshold), "nmsThreshold": .45,
            "rawSha256": hashlib.sha256(raw_bytes).hexdigest(), "numpyVersion": np.__version__,
            "rows": rows, "candidateRows": candidates.tolist(), "keptRows": kept,
            "sourceBoxes": source_boxes, "clipped": clipped}
with args.output.open("x", encoding="utf-8") as file:
    json.dump(document, file, ensure_ascii=False, allow_nan=False, separators=(",", ":"))
print(json.dumps({"rows": len(rows), "candidates": len(candidates), "keptRows": kept,
                  "visible": sum(box is not None for box in source_boxes), "numpy": np.__version__}))
