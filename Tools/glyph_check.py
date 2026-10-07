# -*- coding: utf-8 -*-
"""字形贴图自检：确认 alpha 通道真的有字形（而不是一张空图）。"""
import sys

from PIL import Image


def check(path):
    img = Image.open(path).convert("RGBA")
    alpha = img.getchannel("A")
    box = alpha.getbbox()
    total = img.size[0] * img.size[1]
    opaque = sum(1 for p in alpha.getdata() if p > 32)
    print("%s  size=%s  bbox=%s  opaque=%d/%d (%.1f%%)"
          % (path.split("/")[-1], img.size, box, opaque, total, 100.0 * opaque / total))
    return box is not None and opaque > 0


if __name__ == "__main__":
    ok = all(check(p) for p in sys.argv[1:])
    print("PASS" if ok else "FAIL")
    sys.exit(0 if ok else 1)
