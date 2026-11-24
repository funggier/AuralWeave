"""Turn a user-provided AN ribbon PNG into the alpha-clean Windows icon art.

Requires Pillow, runs offline. Preserves the existing white/silver shading;
only removes isolated background particles and centers the connected ribbon.
"""
from __future__ import annotations

import argparse
from collections import deque
from pathlib import Path
from PIL import Image, ImageChops, ImageFilter


def largest_connected_ribbon(image: Image.Image) -> Image.Image:
    image = image.convert("RGBA")
    w, h = image.size
    pixels = list(image.get_flattened_data())
    valid = bytearray(w * h)
    for i, (r, g, b, a) in enumerate(pixels):
        if a >= 100 and max(r, g, b) >= 75:
            valid[i] = 1
    seen = bytearray(w * h)
    largest = []
    for seed in range(w * h):
        if not valid[seed] or seen[seed]:
            continue
        seen[seed] = 1
        queue = deque([seed])
        component = []
        while queue:
            index = queue.popleft()
            component.append(index)
            x, y = index % w, index // w
            for next_index in (
                index - 1 if x else -1,
                index + 1 if x + 1 < w else -1,
                index - w if y else -1,
                index + w if y + 1 < h else -1,
            ):
                if next_index >= 0 and valid[next_index] and not seen[next_index]:
                    seen[next_index] = 1
                    queue.append(next_index)
        if len(component) > len(largest):
            largest = component

    if len(largest) < 15000:
        raise ValueError("Could not isolate the connected AN ribbon")
    mask_bytes = bytearray(w * h)
    for pixel in largest:
        mask_bytes[pixel] = 255

    mask = Image.frombytes("L", (w, h), bytes(mask_bytes))
    mask = mask.filter(ImageFilter.MedianFilter(3)).filter(
        ImageFilter.GaussianBlur(0.55)
    )
    alpha = ImageChops.darker(image.getchannel("A"), mask)
    image.putalpha(alpha)
    return image


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--size", type=int, default=512)
    args = parser.parse_args()
    with Image.open(args.source) as source:
        image = largest_connected_ribbon(source)
    bounds = image.getbbox()
    if bounds is None:
        raise ValueError("Processed logo is empty")
    margin = 8
    bounds = (
        max(0, bounds[0] - margin),
        max(0, bounds[1] - margin),
        min(image.width, bounds[2] + margin),
        min(image.height, bounds[3] + margin),
    )
    image = image.crop(bounds)
    size = args.size
    fit = round(size * 0.94)
    scale = min(fit / image.width, fit / image.height)
    scaled = image.resize(
        (max(1, round(image.width * scale)),
         max(1, round(image.height * scale))),
        Image.Resampling.LANCZOS,
    )
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.alpha_composite(
        scaled, ((size - scaled.width) // 2, (size - scaled.height) // 2)
    )
    assert canvas.getpixel((0, 0))[3] == 0
    args.destination.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(args.destination, optimize=True)
    print(
        f"AN icon image: {args.destination} | {canvas.size} | "
        f"opaque area: {canvas.getbbox()} | transparent corners"
    )


if __name__ == "__main__":
    main()
