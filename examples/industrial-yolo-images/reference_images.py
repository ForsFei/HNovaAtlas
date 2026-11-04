"""Local CPU cross-language reference; no installs, downloads or output overwrite.

YOLOX upstream model-specific preprocessing; double source-space geometry and
stable row-id tie breaking are explicit local policies shared with C#.
"""
import argparse
import hashlib
import json
from pathlib import Path
import cv2
import numpy as np
import onnxruntime as ort

parser = argparse.ArgumentParser()
parser.add_argument("model", type=Path)
parser.add_argument("image", type=Path)
parser.add_argument("prefix", type=Path)
args = parser.parse_args()
sha = lambda data: hashlib.sha256(data).hexdigest().upper()
model_sha = "C789161ED43C8269FCD4E67C67EEEB4E80C622DA2EB296A20BC6007BD18A0B7D"
if args.model.stat().st_size != 3659407 or sha(args.model.read_bytes()) != model_sha:
    raise ValueError("Fixed YOLOX-Nano model required")
if cv2.__version__ != "4.13.0" or ort.__version__ != "1.27.0":
    raise ValueError("Reference requires OpenCV4.13.0 and ORT1.27.0")
paths = [Path(str(args.prefix) + suffix) for suffix in (".input.f32", ".output.f32", ".json")]
if any(path.exists() for path in paths):
    raise FileExistsError("Reference output exists")
if not 3 <= args.image.stat().st_size <= 16 * 1024 * 1024:
    raise ValueError("Encoded file size invalid")
encoded = args.image.read_bytes()
if not (encoded.startswith(b"\x89PNG\r\n\x1a\n") or encoded.startswith(b"\xff\xd8\xff")):
    raise ValueError("PNG/JPEG required")
image = cv2.imdecode(np.frombuffer(encoded, np.uint8), cv2.IMREAD_UNCHANGED)
if image is None or image.dtype != np.uint8:
    raise ValueError("8-bit decoded image required")
if image.ndim == 2:
    image = cv2.cvtColor(image, cv2.COLOR_GRAY2BGR)
if image.ndim != 3 or image.shape[2] != 3:
    raise ValueError("BGR or grayscale only; alpha rejected")
height, width = image.shape[:2]
if not (1 <= width <= 8192 and 1 <= height <= 8192 and width * height <= 16777216):
    raise ValueError("Decoded dimensions invalid")
ratio = min(416 / width, 416 / height)
rw, rh = int(width * ratio), int(height * ratio)
if min(rw, rh) < 1:
    raise ValueError("Truncated resize has zero side")
canvas = np.full((416, 416, 3), 114, dtype=np.uint8)
canvas[:rh, :rw] = cv2.resize(image, (rw, rh), interpolation=cv2.INTER_LINEAR)
tensor = np.ascontiguousarray(canvas.transpose(2, 0, 1)[None], dtype=np.float32)
options = ort.SessionOptions()
options.intra_op_num_threads = options.inter_op_num_threads = 1
options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_DISABLE_ALL
session = ort.InferenceSession(str(args.model), options, providers=["CPUExecutionProvider"])
raw = session.run(["output"], {"images": tensor})[0]
if raw.shape != (1, 3549, 85) or not np.isfinite(raw).all():
    raise ValueError("Unexpected output")
values = raw[0]
grids, strides = [], []
for stride in (8, 16, 32):
    size = 416 // stride
    x, y = np.meshgrid(np.arange(size), np.arange(size))
    grids.append(np.stack((x, y), axis=-1).reshape(-1, 2))
    strides.append(np.full((size * size, 1), stride))
decoded = values[:, :4].copy()
decoded[:, :2] = (values[:, :2] + np.concatenate(grids)) * np.concatenate(strides)
decoded[:, 2:] = np.exp(values[:, 2:4]) * np.concatenate(strides)
boxes = np.empty_like(decoded)
boxes[:, :2] = decoded[:, :2] - decoded[:, 2:] / 2
boxes[:, 2:] = decoded[:, :2] + decoded[:, 2:] / 2
source = boxes.astype(np.float64) / ratio
products = values[:, 4:5] * values[:, 5:]
classes = products.argmax(axis=1)
scores = products[np.arange(3549), classes]
threshold = np.float32(.01)
candidates = np.flatnonzero(scores > threshold)
order = candidates[np.lexsort((candidates, -scores[candidates]))]
kept = []
while len(order):
    current = int(order[0])
    kept.append(current)
    rest = order[1:]
    a, b = source[current], source[rest]
    wh = np.maximum(0, np.minimum(a[2:], b[:, 2:]) - np.maximum(a[:2], b[:, :2]) + 1)
    intersection = wh[:, 0] * wh[:, 1]
    union = np.prod(a[2:] - a[:2] + 1) + np.prod(b[:, 2:] - b[:, :2] + 1, axis=1) - intersection
    order = rest[intersection / union <= .45]
visible, clipped = [], []
for row in kept:
    box = boxes[row].astype(np.float64)
    crop = np.array([max(0, box[0]), max(0, box[1]), min(rw, box[2]), min(rh, box[3])])
    mapped = np.clip(crop / ratio, [0, 0, 0, 0], [width, height, width, height])
    result = mapped.tolist() if crop[2] > crop[0] and crop[3] > crop[1] else None
    visible.append(result)
    clipped.append(bool(result is None or not np.array_equal(mapped, source[row])))
input_bytes = tensor.astype("<f4").tobytes()
output_bytes = raw.astype("<f4").tobytes()
document = dict(modelSha256=model_sha, encodedSha256=sha(encoded), inputSha256=sha(input_bytes),
    width=width, height=height, ratio=ratio, resizedWidth=rw, resizedHeight=rh,
    openCvVersion=cv2.__version__, numpyVersion=np.__version__, ortVersion=ort.__version__,
    scoreThreshold=float(threshold), nmsThreshold=.45,
    rows=np.column_stack((source, classes, scores)).tolist(), candidateRows=candidates.tolist(),
    keptRows=kept, sourceBoxes=visible, clipped=clipped)
for path, data in zip(paths, [input_bytes, output_bytes, json.dumps(document, allow_nan=False, separators=(",", ":")).encode()]):
    with path.open("xb") as output:
        output.write(data)
print(json.dumps(dict(inputSha256=sha(input_bytes), rows=3549, candidates=len(candidates), keptRows=kept,
    visible=sum(box is not None for box in visible))))
