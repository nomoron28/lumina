# Brand asset manifest

Everything under `assets/brand/` is generated. Change `scripts/brand/make_brand.py` and run `python scripts/brand/make_brand.py`; do not edit the outputs.

## Logo (`assets/brand/logo/`)

| File | Use |
|---|---|
| `aperture-mark.svg` | Full-colour mark, the master |
| `aperture-mark-flat.svg` | One flat amber, for small sizes and one-colour print |
| `aperture-mark-mono-white.svg`, `aperture-mark-mono-black.svg` | Single colour on dark / light |
| `aperture-mark-outline.svg` | Outline, large sizes only |
| `lumina-horizontal-{dark,light,mono-white,mono-black}.svg` | Mark left, wordmark right. `dark` is for dark backgrounds, `light` for light ones |
| `lumina-stacked-{dark,light,mono-white,mono-black}.svg` | Mark above wordmark: welcome and splash |
| `wordmark-{dark,light}.svg` | Wordmark only (Garet Heavy as outlines) |
| `aperture-mark-{1024,256,128,64,48,32,16}.png`, `aperture-mark-flat-{1024,256,64,32,16}.png` | Rasters, rendered from the same geometry |

## App icon (`assets/brand/app-icon/`)

| Variant | File stem | Role |
|---|---|---|
| A glow | `app-icon-A-glow` | **Recommended.** Used for 48 px and up |
| B flat | `app-icon-B-flat` | Used below 48 px in the shipped `.ico` |
| C glass | `app-icon-C-glass` | Presentation only (always has a flat fallback) |
| D outline | `app-icon-D-outline` | Alternative for light contexts |

Each has an SVG and PNGs at 1024, 256, 128, 64, 48, 32, 16. `lumina.ico` is the shipped file: 256, 128, 64, 48, 32, 24, 16, one native render per size.

## Tray icon (`assets/brand/tray-icon/`)

Variants A filled, B compact, C outline, **D mono (shipped)**. Each as `tray-<variant>-{light,dark}.svg` and PNGs at 32, 24, 20, 16. `light` means "for a light taskbar" (near-black glyph). Shipped files: `lumina-tray-dark.ico` and `lumina-tray-light.ico`. The outline variant (C) does not survive 16 px and is not recommended.

## Splash and previews

`splash/splash-mark.svg`, `splash/splash-1600x1000.png`, and `splash/splash-680x420.png` (the start-up splash: static, shown by the app for a normal launch and not for the quiet start-with-Windows launch); `previews/mark-sizes.png` shows the mark and icons at every size, including the tray glyphs on light and dark plates.

## In the application

| Generated file | Purpose |
|---|---|
| `src/Lumina/Theme/BrandMark.xaml` | The mark as WPF vector drawings (`Lumina.Brand.Mark`, `Lumina.Brand.MarkFlat`) |
| `src/Lumina/Assets/Brand/*` (linked) | The three `.ico` files and the splash image compiled in as resources |
| `src/Lumina/Ui/WelcomeWindow.xaml` | The first-run welcome: logo, one line, three steps (choose a folder, optional keys, appearance and accent) |
| `src/Lumina/Lumina.csproj` | `ApplicationIcon` points at `lumina.ico` |
| `installer/Lumina.iss` | `SetupIconFile` points at `lumina.ico` |

Not changed: the Windows jump-list and now-playing entries use the executable's icon, so they follow `lumina.ico`.
