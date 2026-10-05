"""Offline AI environment smoke test used by Qtis Vision Setup."""

from pathlib import Path
from tempfile import TemporaryDirectory
import sys
import warnings

import numpy
import onnx
import onnxscript
from PIL import Image
import torch


class SmokeModel(torch.nn.Module):
    def forward(self, value):
        return value + 1.0


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    warnings.filterwarnings(
        "ignore",
        message="You are using the legacy TorchScript-based ONNX export.*",
        category=DeprecationWarning,
    )

    with TemporaryDirectory(prefix="qtis-ai-smoke-") as temporary:
        output_path = Path(temporary) / "smoke.onnx"
        model = SmokeModel().eval()
        sample = torch.zeros((1, 1, 32, 32), dtype=torch.float32)
        torch.onnx.export(
            model,
            (sample,),
            str(output_path),
            input_names=["image"],
            output_names=["score"],
            opset_version=12,
            do_constant_folding=True,
            dynamo=False,
        )
        checked_model = onnx.load(str(output_path))
        onnx.checker.check_model(checked_model)

    print(
        "AI_ENVIRONMENT_OK"
        f"|python_torch={torch.__version__}"
        f"|onnx={onnx.__version__}"
        f"|onnxscript={onnxscript.__version__}"
        f"|numpy={numpy.__version__}"
        f"|pillow={Image.__version__}"
    )


if __name__ == "__main__":
    main()
