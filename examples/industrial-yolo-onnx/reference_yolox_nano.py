"""Optional numerical reference: existing numpy + onnxruntime only; installs nothing.

Creates a NEW raw little-endian float32 reference file, never overwrites one.
The r=1 fixture needs no interpolation; this is not the full upstream image demo.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
import onnxruntime as ort

parser = argparse.ArgumentParser()
parser.add_argument("model", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
model_bytes = args.model.read_bytes()
expected_hash = "c789161ed43c8269fcd4e67c67eeeb4e80c622da2eb296a20bc6007bd18a0b7d"
if len(model_bytes) != 3659407 or hashlib.sha256(model_bytes).hexdigest() != expected_hash:
    raise ValueError("Pinned model length/hash mismatch")
y, x = np.indices((208, 416), dtype=np.int32)
source = np.stack((x % 256, y % 256, (x + 2 * y) % 256), axis=-1).astype(np.uint8)
canvas = np.full((416, 416, 3), 114, dtype=np.uint8)
canvas[:208, :416, :] = source
tensor = np.ascontiguousarray(canvas.transpose(2, 0, 1)[None], dtype=np.float32)
options = ort.SessionOptions()
options.intra_op_num_threads = 1
options.inter_op_num_threads = 1
options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_DISABLE_ALL
session = ort.InferenceSession(model_bytes, sess_options=options, providers=["CPUExecutionProvider"])
if [(i.name, i.shape, i.type) for i in session.get_inputs()] != [("images", [1, 3, 416, 416], "tensor(float)")]:
    raise ValueError("Input metadata mismatch")
result = session.run(["output"], {"images": tensor})[0]
if result.shape != (1, 3549, 85) or result.dtype != np.float32 or not np.isfinite(result).all():
    raise ValueError("Invalid output")
raw = result.astype("<f4").tobytes()
with args.output.open("xb") as file:
    file.write(raw)
indices = [0, 1, 2, 3, 4, 5, 84, 85, 89, 170, 2719, 5000, 10000, 30000,
           100000, 200000, 250000, 300000, 301660, 301664]
print(json.dumps({"fixture": "owned-bgr-r1-416x208-v1", "modelSha256": expected_hash,
                  "ort": ort.__version__, "numpy": np.__version__, "provider": session.get_providers(),
                  "inputSha256": hashlib.sha256(tensor.astype("<f4").tobytes()).hexdigest(),
                  "shape": list(result.shape), "count": int(result.size),
                  "outputSha256": hashlib.sha256(raw).hexdigest(),
                  "samples": [{"index": i, "value": float(result.flat[i])} for i in indices]}, indent=2))
