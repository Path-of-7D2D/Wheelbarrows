"""Generate weathered/rusted texture maps for the wheelbarrow.

No third-party deps (numpy + stdlib zlib only) so it runs under the system
Python. Outputs tileable PNGs into the Unity project's Textures folder; the
asset-bundle build assigns them to Standard materials by original material name.
"""

import os
import struct
import zlib

import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT_DIR = os.path.join(ROOT, "UnityProject", "Assets", "Wheelbarrow", "Textures")

SIZE = 512


def write_png(path, arr):
    """Write an HxWx3 uint8 array as an 8-bit RGB PNG (no external deps)."""
    h, w = arr.shape[:2]
    arr = np.ascontiguousarray(arr.astype(np.uint8))
    raw = bytearray()
    stride = w * 3
    flat = arr.reshape(h, stride)
    for y in range(h):
        raw.append(0)  # filter type 0 (None)
        raw.extend(flat[y].tobytes())

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data +
                struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff))

    sig = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)  # 8-bit, color type 2 (RGB)
    idat = zlib.compress(bytes(raw), 9)
    with open(path, "wb") as f:
        f.write(sig + chunk(b"IHDR", ihdr) + chunk(b"IDAT", idat) + chunk(b"IEND", b""))


def tileable_noise(size, period, seed):
    """Periodic bilinear value noise -> seamlessly tiles at `size`."""
    rng = np.random.default_rng(seed)
    grid = rng.random((period, period)).astype(np.float64)

    coords = np.arange(size) * (period / size)
    i0 = np.floor(coords).astype(int) % period
    i1 = (i0 + 1) % period
    frac = coords - np.floor(coords)
    # smoothstep on the interpolation fraction
    frac = frac * frac * (3.0 - 2.0 * frac)

    gx0 = grid[np.ix_(i0, i0)]
    # interpolate along x then y using outer combinations
    def lerp(a, b, t):
        return a + (b - a) * t

    g00 = grid[np.ix_(i0, i0)]
    g10 = grid[np.ix_(i1, i0)]
    g01 = grid[np.ix_(i0, i1)]
    g11 = grid[np.ix_(i1, i1)]
    fx = frac[:, None]
    fy = frac[None, :]
    top = lerp(g00, g10, fx)
    bot = lerp(g01, g11, fx)
    return lerp(top, bot, fy)


def fbm(size, seed, octaves=5, base_period=4):
    out = np.zeros((size, size), np.float64)
    amp = 1.0
    total = 0.0
    period = base_period
    for o in range(octaves):
        out += amp * tileable_noise(size, period, seed + o * 17)
        total += amp
        amp *= 0.5
        period *= 2
    out /= total
    return out


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def mix(c0, c1, t):
    """Mix c0 and c1 by float field t (HxW). Each of c0/c1 may be a len-3
    color or a full HxWx3 image."""
    c0 = np.asarray(c0, np.float64)
    c1 = np.asarray(c1, np.float64)
    if c0.ndim == 1:
        c0 = c0[None, None, :]
    if c1.ndim == 1:
        c1 = c1[None, None, :]
    t = t[..., None]
    return c0 * (1 - t) + c1 * t


def rusted_metal(seed=1):
    base = fbm(SIZE, seed)
    patches = fbm(SIZE, seed + 100, octaves=4, base_period=3)
    fine = fbm(SIZE, seed + 200, octaves=6, base_period=8)

    steel = (74, 74, 80)
    rust_mid = (124, 66, 33)
    rust_dark = (58, 28, 16)
    rust_light = (150, 92, 50)

    rust_mask = smoothstep(0.45, 0.72, patches)
    col = mix(steel, rust_mid, rust_mask)
    col = mix(col, rust_dark, smoothstep(0.55, 0.85, base) * 0.7)
    col = mix(col, rust_light, smoothstep(0.6, 0.9, fine) * rust_mask * 0.6)
    # overall grime darkening
    col *= (0.78 + 0.22 * base)[..., None]
    return np.clip(col, 0, 255)


def rusted_red(seed=2):
    base = fbm(SIZE, seed)
    patches = fbm(SIZE, seed + 100, octaves=4, base_period=3)
    fine = fbm(SIZE, seed + 200, octaves=6, base_period=10)

    paint = (138, 38, 30)
    paint_faded = (108, 46, 40)
    rust_mid = (122, 64, 34)
    rust_dark = (60, 30, 18)

    col = mix(paint, paint_faded, smoothstep(0.35, 0.7, base))
    rust_mask = smoothstep(0.5, 0.78, patches)
    col = mix(col, rust_mid, rust_mask)
    col = mix(col, rust_dark, smoothstep(0.6, 0.9, fine) * 0.5)
    col *= (0.8 + 0.2 * base)[..., None]
    return np.clip(col, 0, 255)


def weathered_wood(seed=3):
    # stretch noise along one axis for grain
    grain = fbm(SIZE, seed, octaves=5, base_period=2)
    streak = tileable_noise(SIZE, 64, seed + 50)
    grain = 0.5 * grain + 0.5 * streak

    wood = (104, 78, 50)
    wood_dark = (66, 46, 28)
    wood_gray = (120, 110, 96)

    col = mix(wood, wood_dark, smoothstep(0.4, 0.75, grain))
    col = mix(col, wood_gray, smoothstep(0.6, 0.95, fbm(SIZE, seed + 9, octaves=3)) * 0.35)
    return np.clip(col, 0, 255)


def rubber(seed=4):
    base = fbm(SIZE, seed, octaves=5, base_period=6)
    black = (20, 20, 22)
    dust = (46, 44, 42)
    col = mix(black, dust, smoothstep(0.55, 0.9, base) * 0.5)
    return np.clip(col, 0, 255)


def normal_map(seed=1, strength=2.2):
    h = fbm(SIZE, seed, octaves=6, base_period=6)
    h += 0.25 * fbm(SIZE, seed + 33, octaves=4, base_period=24)
    dx = np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)
    dy = np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)
    nx = -dx * strength
    ny = -dy * strength
    nz = np.ones_like(h)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    nx *= inv
    ny *= inv
    nz *= inv
    out = np.stack([(nx * 0.5 + 0.5), (ny * 0.5 + 0.5), (nz * 0.5 + 0.5)], axis=-1)
    return np.clip(out * 255, 0, 255)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    jobs = {
        "wb_metal_albedo.png": rusted_metal(),
        "wb_red_albedo.png": rusted_red(),
        "wb_wood_albedo.png": weathered_wood(),
        "wb_rubber_albedo.png": rubber(),
        "wb_metal_normal.png": normal_map(),
    }
    for name, arr in jobs.items():
        path = os.path.join(OUT_DIR, name)
        write_png(path, arr)
        print(f"wrote {path}  {arr.shape[1]}x{arr.shape[0]}")


if __name__ == "__main__":
    main()
