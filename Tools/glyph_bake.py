# -*- coding: utf-8 -*-
"""
字形贴图烘焙器（Editor 阶段离线工具，运行时不参与）。

为什么是 PNG 而不是 Unity Font API：
  1. 批处理模式（-batchmode -nographics）下动态字体图集不可靠；
  2. CONSTRAINTS C2.6 要求图集预烘焙为资产；
  3. CONSTRAINTS C4.1 禁止运行时用 TextMeshPro 渲染战场实体。

用法：
  python glyph_bake.py --out <目录> --font <ttf/ttc> --glyph 兵 --cell 128 --size 110 --name tex_bing
  python glyph_bake.py --out <目录> --font <ttf/ttc> --glyph 英雄 --cell 128 --size 106 --name tex_hero

多字串按 --cell 宽度横向排布，因此「英雄」会生成 256x128 的 2:1 贴图。
"""
import argparse
import os

from PIL import Image, ImageDraw, ImageFont


def bake(text, cell, size, font_path, out_path):
    """把一个字符串烘焙成透明底白字贴图，每字占 cell x cell，整字在格内居中。"""
    n = len(text)
    img = Image.new("RGBA", (cell * n, cell), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    font = ImageFont.truetype(font_path, size)

    for i, ch in enumerate(text):
        # 用 bbox 而不是 font.getsize，避免字体自带行间距把字形顶歪
        left, top, right, bottom = draw.textbbox((0, 0), ch, font=font)
        w = right - left
        h = bottom - top
        x = i * cell + (cell - w) / 2 - left
        y = (cell - h) / 2 - top
        draw.text((x, y), ch, font=font, fill=(255, 255, 255, 255))

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    img.save(out_path)
    return img.size


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True, help="输出目录")
    parser.add_argument("--font", default=r"C:\Windows\Fonts\msyhbd.ttc", help="字形源字体")
    parser.add_argument("--glyph", required=True, help="要烘焙的字符串")
    parser.add_argument("--cell", type=int, default=128, help="单字格边长（像素）")
    parser.add_argument("--size", type=int, default=110, help="字号（像素）")
    parser.add_argument("--name", required=True, help="输出文件名（不含扩展名）")
    args = parser.parse_args()

    out_path = os.path.join(args.out, args.name + ".png")
    size = bake(args.glyph, args.cell, args.size, args.font, out_path)
    print("OK %s -> %s  size=%dx%d" % (args.glyph, out_path, size[0], size[1]))


if __name__ == "__main__":
    main()
