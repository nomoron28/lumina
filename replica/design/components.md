# Lumina components

Source of truth for values: `tokens.json` (generated into `src/Lumina/Theme/Tokens.xaml`).
Styles live in `src/Lumina/Theme/Controls.xaml` and `Templates.xaml`.
Shape rule: interactive things are pills, surfaces are 14, inputs are 10, posters are 10.
One accent (mint, `accent`) for progress, selection glyphs, links and focus. The only bright
fill on any screen is the primary pill.

Focus rings are keyboard-only (`FocusMode.Keyboard`): Tab, arrows, Enter and Space switch
them on, any mouse press switches them off. Text inputs always show their focus border.

## Buttons

```
Primary pill        Lumina.Btn.Primary
  use          one per screen: Play, Resume, Add folder, Save
  tokens       bg primary, text on-primary, radius pill, 44px min height, 600 weight
  states       default, hover (0.88 opacity), pressed (0.97 scale), keyboard focus (2px accent ring), disabled (0.4)
  a11y         real Button; label states the action; contrast on-primary/primary 17.4:1
Secondary pill      Lumina.Btn.Secondary
  use          Open file, Scan now, Cancel, Remove, Connect Trakt
  tokens       bg surface-raised, 1px border, text, radius pill, 40px min height
  states       as primary, hover bg surface-hover
Link                Lumina.Btn.Link
  use          "See all"
  tokens       text accent, radius pill, transparent bg; hover bg surface-raised
Icon (round)        Lumina.Btn.Icon / Lumina.Btn.Glass
  use          rescan, shelf scroll arrows (Icon); back over artwork (Glass: black 60% with hairline)
  size         38px, 32px for shelf arrows
  a11y         always has AutomationProperties.Name and a tooltip where the glyph is not obvious
Tile                Lumina.Btn.Tile
  use          Home summary tiles (Movies, TV shows, Unwatched, Folders)
  tokens       bg surface, 1px border, radius 14; hover bg surface-raised, border border-input
```

## Navigation and selection

```
Sidebar item        Lumina.Nav.Item (RadioButton, exactly one checked)
  anatomy      24px glyph, label, 38px tall, radius 10
  states       default (muted), hover (raised bg, text), checked (surface-hover bg, text), keyboard focus ring
  used on      every page
Segmented item      Lumina.Seg.Item (RadioButton)
  use          library filter (All, Unwatched, In progress), sort (A to Z, Newest), season switcher
  states       default muted, checked bg surface-hover + text
Search field        Lumina.Search
  anatomy      magnifier at left, placeholder "Search", live filter; clears itself when you navigate
```

## Cards and shelves

```
Poster card         Lumina.PosterCard (156 x 234, radius 10)
  anatomy      ImageBrush poster, title (semibold, one line), sub line (caption, muted)
  overlays     progress line (3px accent) 9px above the bottom; watched check chip top-right
  no artwork   title set in the slot, surface fill, so the grid never shows blank boxes
  states       hover lifts 3 percent (120ms), keyboard focus ring around the whole card
Hero card           Lumina.HeroCard (320 x 180, radius 14)
  use          Continue watching
  anatomy      backdrop (poster as fallback), bottom shade, play glyph on hover, progress line, title, sub line
Shelf               Shelf control
  anatomy      title, optional "See all", two scroll arrows, horizontal list
  behaviour    wheel scrolls the page, Shift+wheel scrolls the row, arrows page by one screen minus a card
               an empty shelf collapses its heading too
Episode row         Lumina.EpisodeRow
  anatomy      number, title, "Watched" chip, two-line summary, progress line, secondary Play/Resume pill
Chip                ChipTemplate
  use          facts over the backdrop: type, year, TMDB rating, season count
```

## Containers

```
Group               Lumina.Group (bg surface, 1px border, radius 14)
  use          settings sections, folder list; rows separated by Lumina.Hairline inset 16px
Page header         Lumina.Text.Page (28, semibold) + muted count or caption
Banner              Group + label + muted text + secondary pill (missing TMDB key)
Empty state         accent-dim circle with a glyph, headline, one sentence, primary + secondary pill, drop hint
Dialog              ConfirmDialog, TraktConnectWindow
  anatomy      borderless card, radius 14, surface-raised, soft shadow; Cancel secondary, confirm primary
  keys         Enter confirms, Esc cancels, card drags the window
```

## Inputs

```
Text input          Lumina.Input (surface-raised, 1px border, radius 10)
  states       hover border-input, focus border accent, placeholder (Tag) in text-faint
Password field      WPF-UI PasswordBox with reveal toggle, accent set app-wide at startup
Multiline           same style, mono face, used for extra mpv options
```

## Details page

```
Backdrop 540px tall, fades to page colour at the bottom; a left-to-right shade keeps text readable.
Stack bottom-left (max 660): chips, title (display 44), summary (max 4 lines), Play/Resume pill
beside a progress bar with "elapsed of total". Shows: season switcher (hidden for one season) then
episode rows. Movies: the file path under a "File" caption.
```

## Player controller (scripts/lumina-osc.lua)

```
Idle           nothing drawn; cursor idles away after 2s in fullscreen
Awake          mouse move, pause, drag, end of file, or file open: fades in over about 170ms
Top            title (semibold 30) and a line under it (episode or year), over a smooth scrim
Seek row       elapsed, bar (accent fill, knob on hover, chapter ticks), remaining
Control row    centre: back 10, play/pause (white circle), forward 10
               right: CC pill (shows track language), Audio pill, speed pill (cycles 1, 1.25, 1.5, 2, 0.75), fullscreen
Input          clicks and drags arrive through MouseForwarder (mpv's own window is disabled)
```

## Type

| Role | Font | Size | Weight |
|------|------|------|--------|
| Display (details title) | Segoe UI Variable Display | 44 | 600 |
| Page title | Segoe UI Variable Display | 28 | 600 |
| Section title | Segoe UI Variable Display | 20 | 600 |
| Label, card title | Segoe UI Variable Text | 14 | 600 |
| Body | Segoe UI Variable Text | 14 | 400 |
| Caption | Segoe UI Variable Text | 12 | 400 |

## Accessibility

Contrast is checked by `contrast.py` (20 pairs, all pass AA, body text AAA). Library cards expose
their title to screen readers. Every icon-only button is named. Animations (page fade, card lift)
are short and skipped when Windows has client-area animations turned off.
