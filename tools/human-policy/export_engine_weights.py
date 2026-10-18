#!/usr/bin/env python3
"""Export HumanMovePolicy tensors to Sapling's compact embedded weight format."""

from __future__ import annotations

import argparse
import struct
from pathlib import Path

import numpy as np
import torch


MAGIC = b"SAPHPW01"
TENSORS = (
    ("board_encoder.0.weight", (32, 18, 3, 3)),
    ("board_encoder.0.bias", (32,)),
    ("board_encoder.2.weight", (32, 32, 3, 3)),
    ("board_encoder.2.bias", (32,)),
    ("board_encoder.5.weight", (128, 2048)),
    ("board_encoder.5.bias", (128,)),
    ("move_scorer.0.weight", (64, 263)),
    ("move_scorer.0.bias", (64,)),
    ("move_scorer.2.weight", (1, 64)),
    ("move_scorer.2.bias", (1,)),
)


def export(checkpoint_path: Path, output_path: Path) -> None:
    checkpoint = torch.load(checkpoint_path, map_location="cpu", weights_only=False)
    if checkpoint.get("schema_version") != 1:
        raise ValueError("checkpoint schema_version must be 1")
    state = checkpoint["model_state_dict"]

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("wb") as stream:
        stream.write(MAGIC)
        stream.write(struct.pack("<II", 1, len(TENSORS)))
        total = 0
        for name, shape in TENSORS:
            if name not in state:
                raise ValueError(f"checkpoint is missing tensor {name}")
            array = state[name].detach().cpu().numpy()
            if tuple(array.shape) != shape:
                raise ValueError(f"{name}: expected {shape}, got {tuple(array.shape)}")
            array = np.asarray(array, dtype="<f4", order="C")
            if not np.isfinite(array).all():
                raise ValueError(f"{name} contains a non-finite value")
            encoded_name = name.encode("ascii")
            if len(encoded_name) > 255 or len(shape) > 255:
                raise ValueError("tensor metadata exceeds format limits")
            stream.write(struct.pack("<B", len(encoded_name)))
            stream.write(encoded_name)
            stream.write(struct.pack("<B", len(shape)))
            stream.write(struct.pack("<" + "I" * len(shape), *shape))
            stream.write(struct.pack("<I", array.size))
            stream.write(array.tobytes(order="C"))
            total += array.size
    print(f"wrote {total:,} float32 weights to {output_path} ({output_path.stat().st_size:,} bytes)")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checkpoint", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    export(args.checkpoint, args.output)


if __name__ == "__main__":
    main()
