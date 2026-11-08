# Lumina brand system: Aperture

**Same Lumina. Different mood.** The identity is fixed. The interface accent and the appearance mode are the user's.

## What is fixed

| Element | Rule |
|---|---|
| Mark | Six-blade aperture. Each blade is a wedge whose apex sits on a corner of the small hexagonal opening, so the blades overlap like a pinwheel iris. Built by `scripts/brand/make_brand.py`; never redrawn by hand. |
| Colour | Warm amber. Full mark: blades from `#FFBE5C` (lit) to `#EC861C` (shaded) depending on which way they face. Flat mark: `#F6A23A`. The mark never takes the user's accent. |
| Wordmark | "Lumina" in Garet Heavy, outlined in the logo SVGs so no font has to be installed. |
| Typeface in the app | Garet Book for text, Garet Heavy for headings and the wordmark. Nothing else. The font files are compiled into the program (the Spacetype free-font EULA allows embedding in software; see `src/Lumina/Assets/Fonts/NOTICE.txt`). |
| Shape | Controls radius 10, cards 14, dialogs 14. Circles only for the accent swatches and the mark. |
| Player | Dark charcoal over the picture in every appearance mode. |

## What the user chooses

- **Appearance:** Dark, Light, Match Windows (the library and settings follow it; the player does not).
- **Accent:** one of nine. The accent is used for progress, selection, focus and the player's active controls (play pill, seek thumb and progress, volume, selected rows, active tab).

| Accent | Swatch |
|---|---|
| Mint (default) | `#5CE1C6` |
| Sky | `#6CB8FF` |
| Amber | `#F5B84B` |
| Orange | `#F28C1E` |
| Coral | `#FF8A7A` |
| Pink | `#FF7BAC` |
| Lime | `#B8E65A` |
| Lilac | `#A99BFF` |
| Neutral | `#D9DEE8` |

These swatches are the values the app already shipped and tested, so saved settings keep working. Mint stays the default for everyone.

### How an accent becomes interface colours

`src/Lumina/Settings/Accent.cs` derives, per swatch and appearance mode:

| Role | Used for | Rule |
|---|---|---|
| `Accent` | text, icons, selected labels | On dark: the swatch. On light: darkened until it reads 4.6:1 on the page and cards. |
| `AccentFill` | progress, selection rings, corner marks | The livelier colour; needs only 3:1 against the surface. |
| `AccentDim` | tinted plates behind accent glyphs | A mix of the accent and the page. |
| `OnAccent` | text on an accent fill | Near-black or white, whichever has more contrast with the accent. |

Colour is never the only signal: selected accent has a ring and a check mark, selected tabs an underline, selected rows a check, playing state changes the Play glyph.

## Neutral ladder

Dark: canvas `#0C0D10`, sidebar `#101216`, surface `#15171C`, raised `#1D2027`, hover `#242832`, border `#272B35`, text `#ECEEF2`, muted `#9BA1AE`, faint `#7C8392`.
Light: canvas `#F6F7F9`, sidebar `#EEF0F3`, surface `#FFFFFF`, raised `#F1F3F6`, hover `#E3E6EB`, border `#D6DAE1`, text `#14161A`, muted `#4A5160`, faint `#626B7C`.
The single source is `replica/design/tokens.json`; `build_wpf.py` generates `Tokens.xaml`.

## Clear space and sizes

- Clear space around the mark: one quarter of its diameter.
- Minimum: the mark alone at 16 px (flat, wide gaps, no glow). The horizontal lockup from 120 px wide. Below that use the mark alone.
- Windows app icon: 256, 128, 64, 48, 32, 24, 16 px in one `.ico`, each rendered at its own size (the glow art from 48 px up, the flat art below).
- Tray icon: flat single colour, white on a dark taskbar, near-black on a light one, chosen at start-up.

## Use

| Place | Asset |
|---|---|
| Window and taskbar | `assets/brand/app-icon/lumina.ico` |
| Executable and installer | the same `.ico` |
| Tray | `assets/brand/tray-icon/lumina-tray-dark.ico` / `-light.ico` |
| Sidebar | the full mark as vector (`Theme/BrandMark.xaml`, generated) beside the Garet Heavy wordmark; the mark alone when the sidebar is collapsed |
| Marketing | the lockups in `assets/brand/logo/` |

## Brand lines

Primary: **Your media, in the light.** Others: "Point it at a folder. Get a cinema." "Plays everything. Remembers where you stopped."

## Iconography

Controls use the existing Fluent-style outline icons from the UI library at 16, 20 and 24 px. Only the aperture is bespoke. The player's own glyphs are drawn in the controller script as vector shapes (play, prev, next, skip, volume, subtitles, audio, speed, snapshot, settings, list, fullscreen) and follow the same line weight.
