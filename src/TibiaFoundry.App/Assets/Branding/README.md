# Tibia Foundry branding assets

The official logo was supplied on 2026-10-12 (as the logo of the program under its previous name) and is used unchanged for Tibia Foundry. No separate Tibia Foundry logo has been supplied; to change the logo, replace the master file and run the generator below.

| File | What it is | Used by |
|---|---|---|
| `tibia-foundry-logo.png` | **Official logo, the master.** 32×32 RGBA pixel art, exactly as supplied (SHA-256 `ec4968ab…c8629`, checked by `BrandingTests`). Never edit or re-save it. | Navigation header (32 px), About page (128 px, nearest-neighbor), self-test |
| `tibia-foundry.ico` | Windows icon with 16, 24, 32, 48, 64, 128 and 256 px images | `ApplicationIcon` (TibiaFoundry.exe, taskbar, shortcuts), window icon |
| `tibia-foundry-logo-256.png` | 8× nearest-neighbor enlargement | README and documentation, installer or store listings |

The icon and the 256 px logo are generated from the master by `tools/branding/make_branding.py` (Pillow):

```
python -I tools/branding/make_branding.py
```

Every size uses nearest-neighbor sampling only, so each pixel is an exact copy of a logo pixel: no blending, no new colors, and the 1-bit transparency is kept. Sizes below 32 px can only keep some of the logo's pixels; the rows and columns kept were chosen for legibility and are fixed in the script.

All XAML that shows the logo sets `RenderOptions.BitmapScalingMode="NearestNeighbor"`.
