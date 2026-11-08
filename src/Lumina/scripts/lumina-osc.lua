-- Lumina's on-screen controller for mpv. Replaces the stock OSC (run with osc=no).
-- Everything is drawn from vector shapes and the system UI font, in Lumina's palette.
-- Input arrives from the host window as mouse / keydown / keyup commands (mpv's own window is disabled).

local mp = require 'mp'

-- ASS colours are BGR. These are the tokens in replica/design/tokens.json.
local C = {
    text    = "F2EEEC",  -- #ECEEF2
    muted   = "AEA19B",  -- #9BA1AE
    accent  = "C6E15C",  -- #5CE1C6
    primary = "F5F2F1",  -- #F1F2F5
    onprim  = "100D0C",  -- #0C0D10
    black   = "000000",
}
-- The app writes its font files to a folder libmpv reads (osd-fonts-dir). libass matches the names inside the files, "Garet Book" and
-- "Garet Heavy" (each is its own family there), so bold text asks for the Heavy file instead of a bold flag.
local FONT = "Garet Book"
local FONT_BOLD = "Garet Heavy"

-- Set from Settings through script-opts=lumina-osc-<name>=<value>.
local opt = { hide_ms = 2600, autohide = "always", skip = 10, click = "none", dclick = "fullscreen", wheel = "volume", controls = "chap+cc+audio+speed+queue+gear+snap", style = "classic", thumbs = "yes", theme = "dark", accent = "5CE1C6" }
opt.font = "garet"
require("mp.options").read_options(opt, "lumina-osc")
if opt.font == "system" then FONT, FONT_BOLD = "Segoe UI", "Segoe UI" end     -- no Garet files (the public source has none)
local HIDE_AFTER = opt.hide_ms / 1000

-- Which optional buttons sit in the right cluster, left to right. Unknown names and repeats are dropped,
-- the gear is always there (it holds Settings access), and an empty list means the default order.
local DEFAULT_CONTROLS = { "chap", "cc", "audio", "speed", "queue", "gear", "snap" }
local function parse_controls(text)
    local known = {}
    for _, n in ipairs(DEFAULT_CONTROLS) do known[n] = true end
    local out, seen = {}, {}
    for n in tostring(text or ""):gmatch("[^+,]+") do
        if known[n] and not seen[n] then seen[n] = true; out[#out + 1] = n end
    end
    if #out == 0 then return DEFAULT_CONTROLS end
    if not seen.gear then out[#out + 1] = "gear" end
    return out
end
local CONTROLS = parse_controls(opt.controls)
local in_fullscreen = false

local ov = mp.create_osd_overlay("ass-events")
local dirty = true
local st = {
    fade = 0, target = 0,
    last_move = -1e9, moved = false,
    dragging = false, down_on = nil, hover = nil,
    title = nil, subtitle = nil,
    regions = {}, last_seek = 0,
    queue = { cur = -1, shuffle = false, repeat_mode = "off", items = {} }, qopen = false, gopen = false, gscroll = 0, gpage = "main", fx = { eq = "Off", comp = "Off", space = "Off", width = 0, norm = false, night = false }, aspect_i = 1, crop_i = 1, qscroll = 0, qrects = {},
    vdrag = false, menu = nil, swallow = false, has_prev = false, has_next = false, bookmarks = {},
}
-- classic: a full-width bar. floating: a rounded bar that floats at the bottom centre, with a Close button at the top left.
-- vlc: a flat bar docked under the picture (the community redesign), with a tabbed panel on the right.
st.style = (opt.style == "floating" or opt.style == "vlc" or opt.style == "modern") and opt.style or "classic"
local function is_dock() return st.style == "vlc" or st.style == "modern" end

-- ---------- helpers ----------

local function clamp(v, lo, hi) return math.max(lo, math.min(hi, v)) end

local function fmt(t)
    if not t or t < 0 then return "0:00" end
    t = math.floor(t)
    local h, m, s = math.floor(t / 3600), math.floor(t / 60) % 60, t % 60
    if h > 0 then return string.format("%d:%02d:%02d", h, m, s) end
    return string.format("%d:%02d", m, s)
end

local function fmt_long(t)
    if not t or t < 0 then t = 0 end
    t = math.floor(t)
    return string.format("%02d:%02d:%02d", math.floor(t / 3600), math.floor(t / 60) % 60, t % 60)
end

-- Alpha 0 is opaque, 255 transparent. f scales the whole controller in and out.
local function alpha(base)
    local a = 255 - math.floor((255 - base) * st.fade + 0.5)
    return string.format("&H%02X&", clamp(a, 0, 255))
end

local function rrect(x, y, w, h, r)
    r = math.min(r, w / 2, h / 2)
    local k = r * 0.5523
    return string.format(
        "m %.1f %.1f l %.1f %.1f b %.1f %.1f %.1f %.1f %.1f %.1f l %.1f %.1f b %.1f %.1f %.1f %.1f %.1f %.1f " ..
        "l %.1f %.1f b %.1f %.1f %.1f %.1f %.1f %.1f l %.1f %.1f b %.1f %.1f %.1f %.1f %.1f %.1f",
        x + r, y, x + w - r, y,
        x + w - r + k, y, x + w, y + r - k, x + w, y + r,
        x + w, y + h - r,
        x + w, y + h - r + k, x + w - r + k, y + h, x + w - r, y + h,
        x + r, y + h,
        x + r - k, y + h, x, y + h - r + k, x, y + h - r,
        x, y + r,
        x, y + r - k, x + r - k, y, x + r, y)
end

local L -- list of ASS event strings for the current frame
local draw_menu
local dock_layout, dock_draw, dock_open_panel, activate_name, toast_layer

-- Test seam: current state is mirrored into mpv user-data so UI tests can read it over the IPC pipe.
local function publish(name, value)
    pcall(mp.set_property, "user-data/lumina/" .. name, value)
end

-- Clickable regions in window pixels, for tests that click controls by name. Only rewritten when they change.
local last_regions = ""
local REGION_NAMES = { "chap", "play", "back", "fwd", "prev", "next", "mute", "vol", "cc", "audio", "speed", "gear", "queue", "snap", "fs", "seek", "close", "pill", "bar" }
local function publish_regions(R)
    local t = {}
    for _, n in ipairs(REGION_NAMES) do
        local r = R[n]
        if r then t[n] = { x = math.floor(r.x), y = math.floor(r.y), w = math.floor(r.w), h = math.floor(r.h) } end
    end
    if st.menu then
        for i, r in ipairs(st.menu.rects or {}) do t["menu" .. i] = { x = math.floor(r.x), y = math.floor(r.y), w = math.floor(r.w), h = math.floor(r.h) } end
    end
    for _, q in ipairs(st.qrects) do
        t[(q.name:gsub(":", ""))] = { x = math.floor(q.x), y = math.floor(q.y), w = math.floor(q.w), h = math.floor(q.h) }
    end
    for _, q in ipairs(st.grects or {}) do
        t[(q.name:gsub(":", "_"))] = { x = math.floor(q.x), y = math.floor(q.y), w = math.floor(q.w), h = math.floor(q.h) }
    end
    local j = require("mp.utils").format_json(t)
    if j ~= last_regions then last_regions = j; publish("regions", j) end
end

local function shape(path, color, a)
    L[#L + 1] = string.format("{\\an7\\pos(0,0)\\bord0\\shad0\\3a&HFF&\\4a&HFF&\\1c&H%s&\\1a%s\\p1}%s{\\p0}", color, alpha(a), path)
end

local function text(x, y, s, size, color, a, anchor, bold, face)
    local is_bold = bold or face == FONT_BOLD
    local family = is_bold and FONT_BOLD or (face or FONT)
    -- Garet Heavy is its own family (bold is a different font file); the Segoe UI fallback has to ask for bold instead
    local flag = (opt.font == "system" and is_bold) and 1 or 0
    L[#L + 1] = string.format("{\\an%d\\pos(%.1f,%.1f)\\fn%s\\fs%.1f\\b%d\\bord0\\shad0\\3a&HFF&\\4a&HFF&\\1c&H%s&\\1a%s}%s",
        anchor or 7, x, y, family, size, flag, color, alpha(a), s)
end

local SCRIM_N = 44

local function scrim_bottom(w, h, height, tmax)
    local prev = 0
    for i = 1, SCRIM_N do
        local u = (i - 0.5) / SCRIM_N              -- 0 at the scrim's top edge, 1 at the window bottom
        local T = tmax * (u ^ 1.7)
        local op = 1 - (1 - T) / (1 - prev)
        prev = T
        local y0 = h - height + height * (i - 1) / SCRIM_N
        if op > 0.002 then
            shape(string.format("m 0 %.1f l %d %.1f %d %d 0 %d", y0, w, y0, w, h, h), C.black, 255 - math.floor(op * 255 + 0.5))
        end
    end
end

local function scrim_top(w, height, tmax)
    local nxt = 0
    for i = SCRIM_N, 1, -1 do
        local u = (i - 0.5) / SCRIM_N              -- 0 at the window top, 1 at the scrim's lower edge
        local T = tmax * ((1 - u) ^ 1.7)
        local op = 1 - (1 - T) / (1 - nxt)
        nxt = T
        local y1 = height * i / SCRIM_N
        if op > 0.002 then
            shape(string.format("m 0 0 l %d 0 %d %.1f 0 %.1f", w, w, y1, y1), C.black, 255 - math.floor(op * 255 + 0.5))
        end
    end
end

local function in_rect(r, x, y)
    return r and x >= r.x and x <= r.x + r.w and y >= r.y and y <= r.y + r.h
end

-- ---------- state from mpv ----------

local function paused() return mp.get_property_native("pause") == true end
local function eof() return mp.get_property_native("eof-reached") == true end

local function track_label(kind)
    local tl = mp.get_property_native("track-list") or {}
    local sel = mp.get_property_native(kind == "sub" and "sid" or "aid")
    if not sel then return nil end
    for _, t in ipairs(tl) do
        if t.type == kind and t.id == sel then
            return t.lang or t.title or ("#" .. t.id)
        end
    end
    return nil
end

local function track_detail(t)
    local parts = {}
    if t.codec then parts[#parts + 1] = string.upper(t.codec) end
    if t.type == "audio" and t["demux-channel-count"] then
        local n = t["demux-channel-count"]
        parts[#parts + 1] = (n == 1 and "Mono") or (n == 2 and "Stereo") or (n == 6 and "5.1") or (n == 8 and "7.1") or (n .. " ch")
    end
    if t.external then parts[#parts + 1] = "file" end
    return table.concat(parts, "  ")
end

local function track_name(t)
    local n = t.lang and string.upper(t.lang) or ("Track " .. t.id)
    if t.title and t.title ~= "" then n = n .. "  " .. t.title end
    if #n > 30 then n = n:sub(1, 29) .. "..." end
    return n
end

-- ---------- layout and drawing ----------

local function layout(w, h)
    if is_dock() then return dock_layout(w, h) end
    local s = clamp(h / 820, 0.85, 1.8)
    local M = 36 * s
    local R = {}
    local floating = st.style == "floating"
    local compact = w < 760 or st.mini   -- a small window, or the miniplayer (on a high-DPI screen its 480 DIP is wider than 760 pixels): only what fits, the rest is in the player panel
    R.compact = compact
    local cy = floating and h - 58 * s or h - 46 * s
    R.s, R.M = s, M

    -- the left and right edges the controls sit between: the window's margins, or the inside of the floating bar
    local L0, R0 = M, w - M
    if floating then
        local pw = math.min(w - 2 * M, 1040 * s)
        R.pill = { x = (w - pw) / 2, y = h - 150 * s, w = pw, h = 126 * s }
        L0, R0 = R.pill.x + 24 * s, R.pill.x + pw - 24 * s
        R.close = { x = M, y = 22 * s, w = 96 * s, h = 36 * s }
    end
    R.tl, R.tr = L0, R0     -- where the elapsed and remaining times are written

    local tw = 76 * s
    local seek_y = floating and R.pill.y + 19 * s or h - 104 * s
    R.seek = { x = L0 + tw, y = seek_y, w = (R0 - L0) - 2 * tw, h = 30 * s }   -- generous hit area around a thin bar
    R.play = { x = w / 2 - 29 * s, y = cy - 29 * s, w = 58 * s, h = 58 * s }
    local back = { x = w / 2 - 29 * s - 92 * s, y = cy - 22 * s, w = 44 * s, h = 44 * s }
    local fwd  = { x = w / 2 + 29 * s + 48 * s, y = cy - 22 * s, w = 44 * s, h = 44 * s }
    if not compact or w >= 520 * s then R.back, R.fwd = back, fwd end      -- in a very narrow window only Play stays in the middle

    if st.has_prev and w >= 600 then R.prev = { x = back.x - 52 * s, y = cy - 22 * s, w = 44 * s, h = 44 * s } end
    if st.has_next and w >= 600 then R.next = { x = fwd.x + 52 * s, y = cy - 22 * s, w = 44 * s, h = 44 * s } end
    if not compact then
        R.mute = { x = L0, y = cy - 22 * s, w = 44 * s, h = 44 * s }
        R.vol = { x = L0 + 52 * s, y = cy - 14 * s, w = 96 * s, h = 28 * s }
    end

    local x = R0
    local function put(name, pw, ph)
        x = x - pw
        R[name] = { x = x, y = cy - ph / 2, w = pw, h = ph }
        x = x - 8 * s
    end
    R.fs = { x = R0 - 44 * s, y = cy - 22 * s, w = 44 * s, h = 44 * s }
    x = R.fs.x - 8 * s
    local sizes = { snap = { 44, 44 }, gear = { 44, 44 }, queue = { 44, 44 }, speed = { 62, 36 }, audio = { 72, 36 }, cc = { 56, 36 }, chap = { 56, 36 } }
    local function shown(name)
        if compact then return name == "gear" end
        if name == "snap" then return w >= 1150 end
        if name == "queue" then return #st.queue.items > 0 end
        if name == "chap" then return w >= 1100 and #(mp.get_property_native("chapter-list") or {}) > 0 end
        return true
    end
    -- The cluster grows leftwards from the fullscreen button. If it would run into the transport buttons (previous / next sit
    -- right of the skip buttons), the least important buttons are left out until it fits: snapshot, then chapters, then speed.
    local edge = R.next or R.fwd or R.play
    local right_limit = edge.x + edge.w + 14 * s
    local fs_left = x
    local dropped = {}
    local function place()
        for name in pairs(sizes) do R[name] = nil end
        x = fs_left
        for i = #CONTROLS, 1, -1 do
            local name = CONTROLS[i]
            if shown(name) and not dropped[name] then put(name, sizes[name][1] * s, sizes[name][2] * s) end
        end
        return x + 8 * s
    end
    local left = place()
    for _, name in ipairs({ "snap", "chap", "speed" }) do
        if left >= right_limit then break end
        dropped[name] = true
        left = place()
    end
    local panel_w = math.min(380 * s, w - 2 * M)      -- never wider than a small window
    R.qpanel = { x = w - M - panel_w, y = 84 * s, w = panel_w, h = (floating and R.pill.y - 12 * s or cy - 60 * s) - 84 * s }
    return R
end

local function pill_bg(r, hot)
    shape(rrect(r.x, r.y, r.w, r.h, r.h / 2), C.text, hot and 0xA8 or 0xD8)
end

-- ---------- popup menus (subtitles, audio, speed) ----------

local function clip_text_basic(str, n)
    if #str <= n then return str end
    return str:sub(1, n - 1) .. "..."
end

local function menu_items(name)
    local items = {}
    local tl = mp.get_property_native("track-list") or {}
    if name == "cc" then
        local sid = mp.get_property_native("sid")
        items[#items + 1] = { label = "Off", on = not sid, run = function() mp.set_property("sid", "no") end }
        for _, t in ipairs(tl) do
            if t.type == "sub" then
                items[#items + 1] = { label = track_name(t), detail = track_detail(t), on = (sid == t.id), run = function() mp.set_property_number("sid", t.id) end }
            end
        end
        items[#items + 1] = { label = "Search OpenSubtitles", dim = true, run = function() mp.commandv("script-message", "lumina-subs") end }
        items[#items + 1] = { label = "Choose from all matches...", dim = true, run = function() mp.commandv("script-message", "lumina-subs-pick") end }
        -- the look of the subtitles for this video: the app remembers it for next time
        -- (this runs through the player's own "lumina-do" message because the helpers it needs are defined further down the file)
        local function look(label, detail, action, arg)
            items[#items + 1] = { label = label, dim = true, detail = detail, reopen = "cc", run = function() mp.commandv("script-message", "lumina-do", action, tostring(arg)) end }
        end
        look("Larger text", string.format("%d%%", math.floor((mp.get_property_number("sub-scale") or 1) * 100 + 0.5)), "sub-size", 0.1)
        look("Smaller text", "", "sub-size", -0.1)
        look("Move up", "", "sub-pos", -5)
        look("Move down", string.format("%d", mp.get_property_number("sub-pos") or 100), "sub-pos", 5)
        look("Show later (+0.1 s)", string.format("%+.2f s", mp.get_property_number("sub-delay") or 0), "sub-delay", 0.1)
        look("Show earlier (-0.1 s)", "", "sub-delay", -0.1)
    elseif name == "audio" then
        local aid = mp.get_property_native("aid")
        for _, t in ipairs(tl) do
            if t.type == "audio" then
                items[#items + 1] = { label = track_name(t), detail = track_detail(t), on = (aid == t.id), run = function() mp.set_property_number("aid", t.id) end }
            end
        end
        if #items == 0 then items[1] = { label = "No audio tracks", dim = true, run = function() end } end
        items[#items + 1] = { label = "Output device", dim = true, detail = ">", reopen = "devices", run = function() end }
    elseif name == "devices" then
        local cur = mp.get_property("audio-device") or "auto"
        for _, d in ipairs(mp.get_property_native("audio-device-list") or {}) do
            items[#items + 1] = { label = clip_text_basic(d.description or d.name, 36), on = (d.name == cur), run = function()
                mp.set_property("audio-device", d.name)
                mp.commandv("script-message", "lumina-audio-device", d.name)
            end }
        end
    elseif name == "chapters" then
        local cur = mp.get_property_number("chapter") or -1
        for i, c in ipairs(mp.get_property_native("chapter-list") or {}) do
            items[#items + 1] = { label = string.format("%d  %s", i, c.title ~= "" and c.title or "Chapter " .. i), detail = fmt(c.time),
                on = (i - 1 == cur), run = function() mp.set_property_number("chapter", i - 1) end }
        end
    elseif name == "bookmarks" then
        for i, b in ipairs(st.bookmarks) do
            items[#items + 1] = { label = b.label, detail = fmt(b.t), run = function() mp.commandv("seek", string.format("%.3f", b.t), "absolute+exact") end }
        end
        items[#items + 1] = { label = "Add a bookmark here", dim = #items > 0, run = function() mp.commandv("script-message", "lumina-bookmark") end }
        if #st.bookmarks > 0 then items[#items + 1] = { label = "Remove all bookmarks", dim = true, run = function() mp.commandv("script-message", "lumina-bookmark-clear") end } end
    elseif name == "speed" then
        local cur = mp.get_property_number("speed") or 1
        for _, v in ipairs({ 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 }) do
            items[#items + 1] = { label = string.format("%gx", v), on = math.abs(cur - v) < 0.001, run = function() mp.set_property_number("speed", v) end }
        end
    end
    return items
end

local function open_menu(name)
    if is_dock() then
        local map = { cc = "subs", audio = "audio", devices = "audio", speed = "playback", chapters = "chapters", bookmarks = "bookmarks" }
        if map[name] then dock_open_panel(map[name]) end
        return
    end
    if st.menu and st.menu.name == name then st.menu = nil
    else
        local items = menu_items(name)
        st.menu = #items > 0 and { name = name, items = items, rects = {}, scroll = 0 } or nil   -- nothing to show: no empty box to draw
    end
    publish("menu", st.menu and st.menu.name or "")
    if st.menu then
        local labels = {}
        for _, it in ipairs(st.menu.items) do labels[#labels + 1] = it.label end
        publish("menu_items", table.concat(labels, "|"))
    end
    dirty = true
end

draw_menu = function(R, w)
    local m = st.menu
    if not m then return end
    local anchor = R[m.name == "devices" and "audio" or m.name == "chapters" and "chap" or m.name] or R.gear   -- a hidden button's menu hangs from the gear
    if not anchor then st.menu = nil; publish("menu", ""); return end
    local s = R.s
    local mw, ih, pad = (m.name == "chapters" and 330 or 300) * s, 38 * s, 8 * s
    local room = math.floor((anchor.y - 14 * s - 24 * s - pad * 2) / ih)
    local rows = math.max(1, math.min(#m.items, room))
    m.scroll = clamp(m.scroll or 0, 0, math.max(0, #m.items - rows))
    local mh = rows * ih + pad * 2
    local x = clamp(anchor.x + anchor.w - mw, R.M, w - R.M - mw)
    local y = anchor.y - 14 * s - mh
    shape(rrect(x, y, mw, mh, 14 * s), "1C1A18", 0x10)
    m.rects = {}
    for k = 1, rows do
        local i = m.scroll + k
        local it = m.items[i]
        local r = { x = x + pad, y = y + pad + (k - 1) * ih, w = mw - pad * 2, h = ih }
        m.rects[i] = r
        if st.hover == "menu:" .. i then shape(rrect(r.x, r.y, r.w, r.h, 10 * s), C.text, 0xE4) end
        if it.on then shape(rrect(r.x + 12 * s, r.y + ih / 2 - 4 * s, 8 * s, 8 * s, 4 * s), C.accent, 0) end
        local room_chars = math.floor((r.w - 44 * s - (it.detail and 96 * s or 0)) / (8.4 * s))
        text(r.x + 32 * s, r.y + ih / 2, clip_text_basic(it.label, math.max(8, room_chars)), 16 * s, it.dim and C.muted or C.text, 0, 4, it.on)
        if it.detail then text(r.x + r.w - 14 * s, r.y + ih / 2, it.detail, 13 * s, C.muted, 0, 6, false) end
    end
end

-- ---------- player essentials: one action table drives the panel, keys and tests ----------

-- The same choices as VLC's Video menu: Default first, then the common shapes
local ASPECTS = { { "-1", "Default" }, { "16:9", "16:9" }, { "4:3", "4:3" }, { "1:1", "1:1" }, { "16:10", "16:10" }, { "2.21:1", "2.21:1" }, { "2.35:1", "2.35:1" }, { "2.39:1", "2.39:1" }, { "5:4", "5:4" } }
local CROPS = { { nil, "Default" }, { "16:10", "16:10" }, { "16:9", "16:9" }, { "4:3", "4:3" }, { "1.85:1", "1.85:1" }, { "2.21:1", "2.21:1" }, { "2.35:1", "2.35:1" }, { "2.39:1", "2.39:1" }, { "5:3", "5:3" }, { "5:4", "5:4" }, { "1:1", "1:1" } }

local function num(name, default) return mp.get_property_number(name) or default or 0 end

local function crop_string(ratio)
    -- video-dec-params is the picture as decoded; video-params follows rotation and would swap width and height
    local vp = mp.get_property_native("video-dec-params") or mp.get_property_native("video-params") or {}
    local w, h = vp.w, vp.h
    if not w or not h then return "" end
    local a, b = ratio:match("^([%d%.]+):([%d%.]+)$")
    local r = tonumber(a) / tonumber(b)
    local cw, ch = w, h
    if r < w / h then cw = math.floor(h * r + 0.5) else ch = math.floor(w / r + 0.5) end
    return string.format("%dx%d+%d+%d", cw, ch, math.floor((w - cw) / 2), math.floor((h - ch) / 2))
end

local PIC = { "brightness", "contrast", "saturation", "gamma", "hue" }

local actions = {}
local function step_prop(name, d, lo, hi)
    return function(arg)
        local v = tonumber(arg) or d
        local nv = clamp(num(name) + v, lo, hi)
        mp.set_property_number(name, math.floor(nv * 1000 + 0.5) / 1000)
    end
end
actions["sub-delay"] = step_prop("sub-delay", 0.1, -600, 600)
actions["sub-size"] = step_prop("sub-scale", 0.1, 0.3, 3)
actions["sub-pos"] = step_prop("sub-pos", 5, 0, 100)
actions["audio-delay"] = step_prop("audio-delay", 0.1, -600, 600)
actions["speed"] = step_prop("speed", 0.05, 0.25, 4)
actions["zoom"] = step_prop("video-zoom", 0.1, -2, 3)
for _, n in ipairs(PIC) do actions[n] = step_prop(n, 5, -100, 100) end
actions["rotate"] = function(arg)
    local cur = num("video-rotate")
    mp.set_property_number("video-rotate", arg and tonumber(arg) or (cur + 90) % 360)
end
actions["aspect"] = function(arg)
    st.aspect_i = (arg and tonumber(arg)) or (st.aspect_i % #ASPECTS + 1)
    mp.set_property("video-aspect-override", ASPECTS[st.aspect_i][1])
    mp.osd_message("Aspect ratio: " .. ASPECTS[st.aspect_i][2], 1.6)
end
actions["crop"] = function(arg)
    -- mpv ignores a crop while the picture is turned; say so instead of silently doing nothing
    if num("video-rotate") ~= 0 then mp.osd_message("Turn rotation off to crop", 1.6); return end
    st.crop_i = (arg and tonumber(arg)) or (st.crop_i % #CROPS + 1)
    local ratio = CROPS[st.crop_i][1]
    mp.set_property("video-crop", ratio and crop_string(ratio) or "")
    mp.osd_message("Crop: " .. CROPS[st.crop_i][2], 1.6)
end
actions["ab-loop"] = function() mp.commandv("ab-loop") end
actions["jump"] = function() mp.commandv("script-message", "lumina-jump") end
actions["info"] = function() mp.commandv("script-message", "lumina-info") end
actions["thumbs-toggle"] = function() mp.commandv("script-message", "lumina-thumbs-toggle") end     -- the host flips the setting and answers with lumina-thumbs-state
-- HDR page of the player panel: how HDR video is shown. Every value is an mpv option that was checked to be accepted.
local TONEMAPS = { "auto", "bt.2390", "bt.2446a", "spline", "hable", "mobius", "reinhard", "clip" }
local PEAKS = { "auto", "100", "203", "400", "600", "1000" }
local YESNO = { "auto", "yes", "no" }
local function cycle_prop(name, list)
    local cur = tostring(mp.get_property(name) or list[1])
    local idx = 0                                   -- a value set somewhere else is not in the list: the first press goes to the first entry
    for i, v in ipairs(list) do if v == cur then idx = i end end
    local nxt = list[idx % #list + 1]
    mp.set_property(name, nxt)
    return nxt
end
local function hdr_signal()
    local g = mp.get_property("video-params/gamma")
    if g == "pq" then return "HDR10" elseif g == "hlg" then return "HLG" elseif g and g ~= "" then return "SDR" end
    return "Unknown"
end
local function publish_hdr()
    publish("hdr_signal", hdr_signal())
    publish("hdr_state", table.concat({ tostring(mp.get_property("tone-mapping")), tostring(mp.get_property("target-peak")),
        tostring(mp.get_property("hdr-compute-peak")), tostring(mp.get_property("target-colorspace-hint")) }, "|"))
end
actions["gear-scroll"] = function(arg) st.gscroll = st.gscroll + (tonumber(arg) or 1); dirty = true end     -- the wheel does the same; this is for tests and keys
actions["hdr-open"] = function() st.gpage = "hdr"; st.gscroll = 0; publish_hdr() end
actions["hdr-tonemap"] = function() cycle_prop("tone-mapping", TONEMAPS); publish_hdr() end
actions["hdr-peak"] = function() cycle_prop("target-peak", PEAKS); publish_hdr() end
actions["hdr-computepeak"] = function() cycle_prop("hdr-compute-peak", YESNO); publish_hdr() end
actions["hdr-hint"] = function() cycle_prop("target-colorspace-hint", YESNO); publish_hdr() end
actions["hdr-info"] = function()
    local g, p, w, h = mp.get_property("video-params/gamma"), mp.get_property("video-params/primaries"), mp.get_property("video-params/w"), mp.get_property("video-params/h")
    mp.osd_message(string.format("%s   %s   %s   %sx%s", hdr_signal(), (p and p ~= "") and p or "unknown primaries", (g and g ~= "") and g or "unknown transfer", w or "?", h or "?"), 4)
end
actions["hdr-reset"] = function()
    mp.set_property("tone-mapping", "auto"); mp.set_property("target-peak", "auto"); mp.set_property("hdr-compute-peak", "auto"); mp.set_property("target-colorspace-hint", "auto")
    publish_hdr()
end

actions["fx-open"] = function() st.gpage = "fx"; st.gscroll = 0; mp.commandv("script-message", "lumina-fx", "refresh") end
actions["fx-eq"] = function() mp.commandv("script-message", "lumina-fx", "eq") end
actions["fx-comp"] = function() mp.commandv("script-message", "lumina-fx", "comp") end
actions["fx-space"] = function() mp.commandv("script-message", "lumina-fx", "space") end
actions["fx-width"] = function(arg) mp.commandv("script-message", "lumina-fx", "width", arg or "10") end
actions["fx-norm"] = function() mp.commandv("script-message", "lumina-fx", "norm") end
actions["fx-night"] = function() mp.commandv("script-message", "lumina-fx", "night") end
actions["reset-all"] = function()
    for _, n in ipairs(PIC) do mp.set_property_number(n, 0) end
    mp.set_property_number("sub-delay", 0); mp.set_property_number("audio-delay", 0)
    mp.set_property_number("speed", 1); mp.set_property_number("video-zoom", 0); mp.set_property_number("video-rotate", 0)
    mp.set_property("video-aspect-override", "-1"); mp.set_property("video-crop", "")
    mp.set_property("ab-loop-a", "no"); mp.set_property("ab-loop-b", "no")
    st.aspect_i, st.crop_i = 1, 1
end

local function ab_state()
    local a, b = mp.get_property_number("ab-loop-a"), mp.get_property_number("ab-loop-b")
    if a and b then return "Looping" elseif a then return "A set" end
    return "Off"
end

local function signed(v, unit) return string.format("%+.2f %s", v, unit) end

-- rows: label, how to show the value, name of the action, step for the - and + buttons (nil for a single button)
local ROWS = {
    { "Subtitle delay", function() return signed(num("sub-delay"), "s") end, "sub-delay", 0.1 },
    { "Audio delay", function() return signed(num("audio-delay"), "s") end, "audio-delay", 0.1 },
    { "Speed", function() return string.format("%.2fx", num("speed", 1)) end, "speed", 0.05 },
    { "Zoom", function() return string.format("%d%%", math.floor(2 ^ num("video-zoom") * 100 + 0.5)) end, "zoom", 0.1 },
    { "Rotate", function() return string.format("%d deg", num("video-rotate")) end, "rotate" },
    { "Aspect ratio", function() return ASPECTS[st.aspect_i][2] end, "aspect" },
    { "Crop", function() return CROPS[st.crop_i][2] end, "crop" },
    { "Brightness", function() return string.format("%d", num("brightness")) end, "brightness", 5 },
    { "Contrast", function() return string.format("%d", num("contrast")) end, "contrast", 5 },
    { "Saturation", function() return string.format("%d", num("saturation")) end, "saturation", 5 },
    { "Gamma", function() return string.format("%d", num("gamma")) end, "gamma", 5 },
    { "Hue", function() return string.format("%d", num("hue")) end, "hue", 5 },
    { "A-B loop", ab_state, "ab-loop" },
    { "Jump to time", function() return "" end, "jump" },
    { "Media info", function() return "" end, "info" },
    { "Audio effects", function() return "" end, "fx-open" },
    { "HDR and tone mapping", function() return hdr_signal() end, "hdr-open" },
    { "Bookmarks", function() return tostring(#st.bookmarks) end, "bookmarks" },
    { "Sleep timer", function() return st.sleep_label or "Off" end, "sleep-cycle" },
    { "Record the stream", function() return st.recording and "Recording" or "Off" end, "record" },
    { "Miniplayer", function() return st.mini and "On" or "Off" end, "miniplayer" },
}

-- The sleep timer belongs to the app (it must outlast the restart between queue items). This only asks for changes and shows its state.
actions["sleep"] = function(arg) mp.commandv("script-message", "lumina-sleep", tostring(arg or "cycle")) end
actions["miniplayer"] = function() mp.commandv("script-message", "lumina-miniplayer") end
actions["sleep-cycle"] = function() mp.commandv("script-message", "lumina-sleep", "cycle") end
actions["record"] = function() mp.commandv("script-message", "lumina-record") end

actions["bookmarks"] = function()
    st.gopen = false; publish("gear", "")
    open_menu("bookmarks")
end

local function onoff(v) return v and "On" or "Off" end
local FX_ROWS = {
    { "Equalizer", function() return st.fx.eq end, "fx-eq" },
    { "Compressor", function() return st.fx.comp end, "fx-comp" },
    { "Spatial sound", function() return st.fx.space end, "fx-space" },
    { "Stereo width", function() return string.format("%d%%", st.fx.width) end, "fx-width", 10 },
    { "Even out loudness", function() return onoff(st.fx.norm) end, "fx-norm" },
    { "Night mode", function() return onoff(st.fx.night) end, "fx-night" },
}

local HDR_ROWS = {
    { "Signal", function() return hdr_signal() end, "hdr-info" },
    { "Tone mapping", function() return tostring(mp.get_property("tone-mapping") or "auto") end, "hdr-tonemap" },
    { "Peak brightness (nits)", function() return tostring(mp.get_property("target-peak") or "auto") end, "hdr-peak" },
    { "Detect peak per scene", function() return tostring(mp.get_property("hdr-compute-peak") or "auto") end, "hdr-computepeak" },
    { "Tell the display it is HDR", function() return tostring(mp.get_property("target-colorspace-hint") or "auto") end, "hdr-hint" },
    { "Back to automatic", function() return "" end, "hdr-reset" },
}

local function cur_rows() return st.gpage == "fx" and FX_ROWS or st.gpage == "hdr" and HDR_ROWS or ROWS end

-- A video's subtitle look (delay, size, height) is remembered by the app per file: tell it after every change.
local SUBSTYLE_ACTIONS = { ["sub-delay"] = true, ["sub-size"] = true, ["sub-pos"] = true, ["reset-all"] = true }
local function notify_substyle()
    local d, sc, p = num("sub-delay"), num("sub-scale", 1), num("sub-pos", 100)
    publish("substyle", string.format("%.3f|%.3f|%d", d, sc, p))
    mp.commandv("script-message", "lumina-substyle", string.format("%.3f", d), string.format("%.3f", sc), string.format("%d", p))
end

local function do_action(name, arg)
    local f = actions[name]
    if not f then mp.msg.warn("unknown action " .. tostring(name)); return end
    f(arg)
    if SUBSTYLE_ACTIONS[name] then notify_substyle() end
    dirty = true
end

actions["style"] = function(arg)
    st.style = (arg == "floating" or arg == "vlc" or arg == "modern") and arg or "classic"
    st.menu, st.gopen, st.qopen, st.panel = nil, false, false, nil
    publish("style", st.style); publish("menu", ""); publish("gear", "")
    dirty = true
end
publish("style", st.style)

-- A test seam: compute the layout for a window size and publish every region, so geometry can be checked without drawing.
-- usage: script-message lumina-do layout-test 1600x900
actions["layout-test"] = function(arg)
    local w, h = tostring(arg):match("^(%d+)x(%d+)$")
    w, h = tonumber(w), tonumber(h)
    if not w then return end
    local R = layout(w, h)
    local t = { s = R.s, win_w = w, win_h = h, compact = R.compact and 1 or 0 }     -- the size asked about travels with the answer so a reader can tell which answer it has
    for _, n in ipairs(REGION_NAMES) do
        local r = R[n]
        if r then t[n] = { x = r.x, y = r.y, w = r.w, h = r.h } end
    end
    t.qpanel = { x = R.qpanel.x, y = R.qpanel.y, w = R.qpanel.w, h = R.qpanel.h }
    publish("layout_test", require("mp.utils").format_json(t))
end

-- Hide UI: the controller stays out of the way until asked back. Keys still work.
actions["hide-ui"] = function()
    st.hideui = not st.hideui
    st.menu, st.gopen, st.qopen, st.panel = nil, false, false, nil
    publish("hide_ui", st.hideui and "on" or "off")
    publish("menu", ""); publish("gear", ""); publish("panel", "")
    mp.osd_message(st.hideui and "Controls hidden. Press the same key to bring them back." or "Controls shown", 1.8)
end
publish("hide_ui", "off")
publish("controls", table.concat(CONTROLS, "+"))

mp.register_script_message("lumina-controls", function(text)
    CONTROLS = parse_controls(text)
    publish("controls", table.concat(CONTROLS, "+"))
    st.menu = nil
    dirty = true
end)
mp.register_script_message("lumina-do", do_action)
mp.register_script_message("lumina-theme", function(v) end)      -- the player stays dark in Light mode too

local function draw_gear(R, w)
    st.grects = {}
    if not st.gopen then return end
    local s, P = R.s, R.qpanel
    shape(rrect(P.x, P.y, P.w, P.h, 16 * s), "1C1A18", 0x10)
    local rows_t = cur_rows()
    text(P.x + 22 * s, P.y + 20 * s, st.gpage == "fx" and "Audio effects" or st.gpage == "hdr" and "HDR and tone mapping" or "Player", 20 * s, C.text, 0, 7, true, FONT_BOLD)
    local ih = 31 * s
    local top = P.y + 50 * s
    local rows = math.max(1, math.floor((P.y + P.h - 58 * s - top) / ih))
    local n = #rows_t
    st.gscroll = clamp(st.gscroll, 0, math.max(0, n - rows))
    for k = 0, math.min(rows, n - st.gscroll) - 1 do
        local i = st.gscroll + k + 1
        local row = rows_t[i]
        local ry = top + k * ih
        text(P.x + 24 * s, ry + ih / 2, row[1], 15 * s, C.text, 0, 4, false)
        local function btn(name, label, bx, bw)
            local r = { x = bx, y = ry + 4 * s, w = bw, h = ih - 8 * s }
            st.grects[#st.grects + 1] = { name = name, x = r.x, y = r.y, w = r.w, h = r.h }
            shape(rrect(r.x, r.y, r.w, r.h, r.h / 2), C.text, st.hover == name and 0x90 or 0xE0)
            text(r.x + r.w / 2, r.y + r.h / 2, label, 16 * s, C.text, 0, 5, true)
        end
        local rx = P.x + P.w - 20 * s
        if row[4] then
            btn("g:" .. i .. ":p", "+", rx - 34 * s, 34 * s)
            text(rx - 34 * s - 12 * s, ry + ih / 2, row[2](), 15 * s, C.accent, 0, 6, true)
            btn("g:" .. i .. ":m", "-", rx - 34 * s - 12 * s - 96 * s - 34 * s, 34 * s)
        else
            local label = (row[3] == "jump" and "Go") or ((row[3] == "info" or row[3] == "hdr-info") and "Show") or (row[3] == "hdr-reset" and "Reset") or ((row[3] == "fx-open" or row[3] == "hdr-open") and "Open") or ((row[3] == "fx-norm" or row[3] == "fx-night") and "Switch") or "Change"
            btn("g:" .. i .. ":p", label, rx - 74 * s, 74 * s)
            local v = row[2]()
            if v ~= "" then text(rx - 74 * s - 12 * s, ry + ih / 2, v, 15 * s, C.accent, 0, 6, true) end
        end
    end
    -- a thin scroll bar when there are more rows than fit, so it is clear the panel scrolls
    if n > rows then
        local track = (P.y + P.h - 58 * s) - top
        local bar = math.max(28 * s, track * rows / n)
        local by = top + (track - bar) * (st.gscroll / (n - rows))
        shape(rrect(P.x + P.w - 9 * s, by, 4 * s, bar, 2 * s), C.text, 0x90)
    end
    -- reset all
    local r = { x = P.x + 16 * s, y = P.y + P.h - 50 * s, w = P.w - 32 * s, h = 36 * s }
    st.grects[#st.grects + 1] = { name = "g_reset", x = r.x, y = r.y, w = r.w, h = r.h }
    local fxpage = st.gpage ~= "main"      -- any sub page ends with a Back button
    r.name = fxpage and "g_back" or "g_reset"
    st.grects[#st.grects].name = r.name
    shape(rrect(r.x, r.y, r.w, r.h, r.h / 2), C.text, st.hover == r.name and 0x90 or 0xE0)
    text(r.x + r.w / 2, r.y + r.h / 2, fxpage and "Back" or "Reset everything above", 15 * s, C.text, 0, 5, true)
end

-- ---------- queue panel ----------

local function clip_text(str, n)
    if #str <= n then return str end
    return str:sub(1, math.max(1, n - 1)) .. "..."
end

local function draw_queue(R, w)
    st.qrects = {}
    if not st.qopen or #st.queue.items == 0 then return end
    local s, P = R.s, R.qpanel
    shape(rrect(P.x, P.y, P.w, P.h, 16 * s), "1C1A18", 0x10)
    text(P.x + 22 * s, P.y + 20 * s, "Queue", 20 * s, C.text, 0, 7, true, FONT_BOLD)
    text(P.x + P.w - 22 * s, P.y + 24 * s, string.format("%d of %d", st.queue.cur + 1, #st.queue.items), 14 * s, C.muted, 0, 9, false)

    -- shuffle, repeat, save, clear
    local bx, by, bh = P.x + 16 * s, P.y + 56 * s, 34 * s
    local function chip(name, label, cw, on)
        local r = { x = bx, y = by, w = cw, h = bh }
        st.qrects[#st.qrects + 1] = { name = name, x = r.x, y = r.y, w = r.w, h = r.h }
        shape(rrect(r.x, r.y, r.w, r.h, bh / 2), on and C.accent or C.text, st.hover == name and 0x90 or (on and 0xB0 or 0xE0))
        text(r.x + r.w / 2, r.y + r.h / 2, label, 14 * s, on and C.onprim or C.text, 0, 5, true)
        bx = bx + cw + 8 * s
    end
    chip("q_shuffle", "Shuffle", 84 * s, st.queue.shuffle)
    chip("q_repeat", "Repeat " .. st.queue.repeat_mode, 104 * s, st.queue.repeat_mode ~= "off")
    chip("q_save", "Save", 60 * s, false)
    chip("q_clear", "Clear", 62 * s, false)

    -- rows
    local ih = 42 * s
    local top = P.y + 106 * s
    local avail = P.y + P.h - 12 * s - top
    local rows = math.max(1, math.floor(avail / ih))
    local n = #st.queue.items
    st.qscroll = clamp(st.qscroll, 0, math.max(0, n - rows))
    local chars = math.floor((P.w - 150 * s) / (8.6 * s))
    for k = 0, math.min(rows, n - st.qscroll) - 1 do
        local i = st.qscroll + k + 1            -- 1-based index into items
        local ry = top + k * ih
        local r = { x = P.x + 10 * s, y = ry, w = P.w - 20 * s, h = ih - 4 * s }
        st.qrects[#st.qrects + 1] = { name = "qrow:" .. i, x = r.x, y = r.y, w = r.w - 96 * s, h = r.h }
        local hot = st.hover == "qrow:" .. i
        if hot then shape(rrect(r.x, r.y, r.w, r.h, 10 * s), C.text, 0xE4) end
        local isnow = (i - 1) == st.queue.cur
        if isnow then shape(rrect(r.x + 6 * s, r.y + r.h / 2 - 8 * s, 4 * s, 16 * s, 2 * s), C.accent, 0) end
        text(r.x + 22 * s, r.y + r.h / 2, tostring(i), 13 * s, C.muted, 0, 4, false)
        text(r.x + 52 * s, r.y + r.h / 2, clip_text(st.queue.items[i], chars), 15 * s, isnow and C.accent or C.text, 0, 4, isnow)
        -- move up, move down, remove
        for b, glyph in ipairs({ "up", "dn", "x" }) do
            local gx = r.x + r.w - (3 - b + 1) * 30 * s
            local g = { x = gx, y = r.y + 4 * s, w = 28 * s, h = r.h - 8 * s }
            local nm = "q" .. glyph .. ":" .. i
            st.qrects[#st.qrects + 1] = { name = nm, x = g.x, y = g.y, w = g.w, h = g.h }
            if st.hover == nm then shape(rrect(g.x, g.y, g.w, g.h, 8 * s), C.text, 0xC0) end
            local cx, cy = g.x + g.w / 2, g.y + g.h / 2
            local a = 5 * s
            if glyph == "up" then shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f", cx - a, cy + a * 0.5, cx, cy - a * 0.7, cx + a, cy + a * 0.5), C.muted, 0)
            elseif glyph == "dn" then shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f", cx - a, cy - a * 0.5, cx, cy + a * 0.7, cx + a, cy - a * 0.5), C.muted, 0)
            else text(cx, cy, "x", 15 * s, C.muted, 0, 5, true) end
        end
    end
end

local function dock_bar_h(h) return 56 * clamp(h / 1080, 0.78, 1.7) end
local function dock_docked() return st.style == "vlc" and not in_fullscreen and not st.hideui end
-- the picture is shrunk to leave room for the bar (not in fullscreen, where the bar floats over the picture)
local function apply_margin(h)
    local want = (dock_docked() and h > 0) and dock_bar_h(h) / h or 0
    if st.margin ~= want then st.margin = want; pcall(mp.set_property_number, "video-margin-ratio-bottom", want) end
end

-- ---------- the VLC style: a docked bar under the picture, and a tabbed panel on the right ----------
-- Proportions and colours follow the community redesign frames (Player, Controls, Player - Light).

local PALETTES = {
    dark  = { bar = "1B1B1B", trough = "090909", fill = "371F06", orange = "D37918", thumb = "E8851A", green = "7EA013", volempty = "090909",
              text = "8F8F8F", bright = "D2D2D2", muted = "6A6A6A", onorange = "1B1B1B", panel = "1B1B1B", head = "0F0F0F", box = "0F0F0F",
              boxline = "2C2C2C", hover = "2A2A2A", sel = "3A2308", scrim = "000000" },
}
-- The accent is the user's colour (Settings, Accent colour). It drives the play pill, the seek thumb and progress, the volume wedge,
-- selection marks and the active tab. Everything else is fixed charcoal: the player stays dark over the picture whatever the app's appearance mode.
local function hex_rgb(h)
    h = tostring(h or ""):gsub("^#", "")
    if #h ~= 6 then h = "5CE1C6" end
    return tonumber(h:sub(1, 2), 16) or 92, tonumber(h:sub(3, 4), 16) or 225, tonumber(h:sub(5, 6), 16) or 198
end
local function rgb_hex(r, g, b) return string.format("%02X%02X%02X", math.floor(r + 0.5), math.floor(g + 0.5), math.floor(b + 0.5)) end
local function lum(r, g, b)
    local function ch(v) v = v / 255; return v <= 0.04045 and v / 12.92 or ((v + 0.055) / 1.055) ^ 2.4 end
    return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b)
end
local function mix_hex(a, b, t)           -- t of a, the rest of b
    local ar, ag, ab = hex_rgb(a); local br, bg, bb = hex_rgb(b)
    return rgb_hex(ar * t + br * (1 - t), ag * t + bg * (1 - t), ab * t + bb * (1 - t))
end
local function on_accent(h)               -- near-black or white, whichever reads better on the accent
    local r, g, b = hex_rgb(h)
    local l = lum(r, g, b)
    return ((l + 0.05) / 0.05 >= 4.5) and "0C0D10" or "FFFFFF"
end
local function pal()
    local p = PALETTES.dark
    local acc = tostring(opt.accent or "5CE1C6"):gsub("^#", "")
    local o = {}
    for k, v in pairs(p) do o[k] = v end
    o.orange, o.thumb, o.green = acc, acc, acc
    o.fill = mix_hex(acc, p.trough, 0.30)
    o.sel = mix_hex(acc, p.panel, 0.22)
    o.onorange = on_accent(acc)
    local out = {}
    for k, v in pairs(o) do out[k] = v:sub(5, 6) .. v:sub(3, 4) .. v:sub(1, 2) end      -- ASS wants BGR
    return out
end
-- what the player is using, for tests: accent and the colour that sits on it
local function publish_accent()
    local acc = tostring(opt.accent or "5CE1C6"):gsub("^#", "")
    publish("accent_colors", acc .. "|" .. on_accent(acc))
end

local function poly(pts)
    local t = { string.format("m %.1f %.1f", pts[1], pts[2]) }
    for i = 3, #pts, 2 do t[#t + 1] = string.format("l %.1f %.1f", pts[i], pts[i + 1]) end
    return table.concat(t, " ")
end

-- a ring segment from angle a0 to a1 (degrees, 0 = right, clockwise on screen), thickness t
local function arc_band(cx, cy, r, a0, a1, t)
    local pts, n = {}, 14
    for i = 0, n do
        local a = math.rad(a0 + (a1 - a0) * i / n)
        pts[#pts + 1] = cx + r * math.cos(a); pts[#pts + 1] = cy + r * math.sin(a)
    end
    for i = n, 0, -1 do
        local a = math.rad(a0 + (a1 - a0) * i / n)
        pts[#pts + 1] = cx + (r - t) * math.cos(a); pts[#pts + 1] = cy + (r - t) * math.sin(a)
    end
    return poly(pts)
end

local DOCK_TABS_LIST = { { "queue", "Queue" }, { "bookmarks", "Bookmarks" }, { "chapters", "Chapters" } }
local DOCK_TABS_SET = { { "playback", "Playback" }, { "video", "Video" }, { "audio", "Audio" }, { "subs", "Subtitles" } }
local function tab_set(tab)
    for _, t in ipairs(DOCK_TABS_LIST) do if t[1] == tab then return DOCK_TABS_LIST end end
    return DOCK_TABS_SET
end

-- ---------- the modern style: one slim floating glass bar. Transport on the left, the seek bar in the middle, volume and tools on the right ----------
local function dock_layout_modern(w, h)
    local d = clamp(h / 1080, 0.78, 1.7)
    local s = clamp(h / 820, 0.85, 1.8)
    local R = { s = s, d = d, M = 20 * d, modern = true, compact = false }
    local bw = math.min(w - 2 * 20 * d, 1320 * d)
    local bh = 68 * d
    local bx, by = (w - bw) / 2, h - bh - 22 * d
    R.bar = { x = bx, y = by, w = bw, h = bh }
    R.close = { x = 20 * d, y = 14 * d, w = 76 * d, h = 34 * d }          -- Back, top left
    local pad = 18 * d
    local ix, ex = bx + pad, bx + bw - pad
    local cy = by + bh / 2

    -- left: previous, back, Play, forward, next
    local x = ix
    local function put(name, bw_, bh_) R[name] = { x = x, y = cy - bh_ / 2, w = bw_, h = bh_ }; x = x + bw_ + 4 * d end
    if st.has_prev then put("prev", 40 * d, 40 * d) end
    put("back", 40 * d, 40 * d); put("play", 58 * d, 44 * d); put("fwd", 40 * d, 40 * d)
    if st.has_next then put("next", 40 * d, 40 * d) end
    local left_end = x - 4 * d

    -- right: volume, then the tools, fullscreen last (the least needed ones give way when the bar is narrow)
    local function shown(name)
        if name == "snap" then return w >= 1150 end
        if name == "chap" then return w >= 1100 and #(mp.get_property_native("chapter-list") or {}) > 0 end
        return true
    end
    local dropped, volw = {}, 92 * d
    local right_start
    local function place()
        for _, n in ipairs({ "snap", "gear", "queue", "speed", "audio", "cc", "chap", "fs", "mute", "vol" }) do R[n] = nil end
        local rx = ex
        rx = rx - 40 * d; R.fs = { x = rx, y = cy - 20 * d, w = 40 * d, h = 40 * d }; rx = rx - 2 * d
        for i = #CONTROLS, 1, -1 do
            local n = CONTROLS[i]
            if shown(n) and not dropped[n] then
                rx = rx - 40 * d
                R[n] = { x = rx, y = cy - 20 * d, w = 40 * d, h = 40 * d }
                rx = rx - 2 * d
            end
        end
        rx = rx - 12 * d
        if volw > 0 then
            rx = rx - volw; R.vol = { x = rx, y = cy - 15 * d, w = volw, h = 30 * d }; rx = rx - 4 * d
            rx = rx - 40 * d; R.mute = { x = rx, y = cy - 20 * d, w = 40 * d, h = 40 * d }
        else
            rx = rx - 40 * d; R.mute = { x = rx, y = cy - 20 * d, w = 40 * d, h = 40 * d }
        end
        right_start = rx
    end
    place()
    local tw = 78 * d                                                       -- room for a time at each end of the seek bar
    local function room() return (right_start - 18 * d) - (left_end + 18 * d) - 2 * tw - 16 * d end
    for _, n in ipairs({ "snap", "chap", "speed", "queue", "cc", "audio" }) do
        if room() >= 170 * d then break end
        dropped[n] = true; place()
    end
    if room() < 170 * d then volw = 0; place() end                          -- last resort: the speaker alone, no slider

    -- the middle: elapsed time, the seek bar, total time
    local mx0, mx1 = left_end + 18 * d, right_start - 18 * d
    R.tl, R.tr = mx0, mx1
    R.seek = { x = mx0 + tw + 8 * d, y = cy - 18 * d, w = math.max(60 * d, mx1 - mx0 - 2 * tw - 16 * d), h = 36 * d }
    R.seek.draw_y, R.seek.draw_h = cy - 3 * d, 6 * d

    local panel_w = math.min((st.panel and (st.panel.tab == "queue" or st.panel.tab == "bookmarks" or st.panel.tab == "chapters") and 360 or 420) * d, w - 2 * 14 * d)
    local top = 22 * d
    local ph = math.min(R.bar.y - 14 * d - top, 720 * d)
    R.qpanel = { x = w - 14 * d - panel_w, y = R.bar.y - 14 * d - ph, w = panel_w, h = ph }
    return R
end

dock_layout = function(w, h)
    if st.style == "modern" and not (w < 760 or st.mini) then return dock_layout_modern(w, h) end
    local d = clamp(h / 1080, 0.78, 1.7)
    local s = clamp(h / 820, 0.85, 1.8)               -- the older styles' scale: menus and tests that read R.s keep working
    local R = { s = s, d = d, M = 20 * d }
    local compact = w < 760 or st.mini
    R.compact = compact
    local bar_h = dock_bar_h(h)
    R.bar = { x = 0, y = h - bar_h, w = w, h = bar_h }
    R.close = { x = 20 * d, y = 14 * d, w = (compact and 38 or 76) * d, h = 34 * d }     -- Back: leave the video, return to the show or movie page
    local cy = h - bar_h / 2
    local x = 20 * d

    -- left: previous, back, Play, forward, next
    local function put(name, bw, bh, gap)
        R[name] = { x = x, y = cy - bh / 2, w = bw, h = bh }
        x = x + bw + (gap or 6 * d)
    end
    if st.has_prev and not compact then put("prev", 32 * d, 32 * d) end
    if not compact and w >= 900 then put("back", 34 * d, 32 * d) end
    put("play", 56 * d, 30 * d, 8 * d)
    if not compact and w >= 900 then put("fwd", 34 * d, 32 * d) end
    if st.has_next and not compact then put("next", 32 * d, 32 * d) end
    x = x + 10 * d
    R.tl = x
    local tw = 84 * d
    x = x + tw

    -- right: the fullscreen button, then the chosen buttons, then the volume
    local rx = w - 16 * d
    local function putr(name, bw, bh)
        rx = rx - bw
        R[name] = { x = rx, y = cy - bh / 2, w = bw, h = bh }
        rx = rx - 6 * d
    end
    putr("fs", 34 * d, 30 * d)
    local sizes = { snap = 34, gear = 34, queue = 34, speed = 34, audio = 34, cc = 34, chap = 34 }
    local function shown(name)
        if compact then return name == "gear" end
        if name == "snap" then return w >= 1150 end
        if name == "chap" then return w >= 1100 and #(mp.get_property_native("chapter-list") or {}) > 0 end
        return true
    end
    local right_start = rx
    local dropped = {}
    local function place()
        rx = right_start
        for name in pairs(sizes) do R[name] = nil end
        for i = #CONTROLS, 1, -1 do
            local name = CONTROLS[i]
            if shown(name) and not dropped[name] then putr(name, sizes[name] * d, 30 * d) end
        end
        return rx
    end
    local left_edge = place()
    if not compact then
        R.vol = nil
        R.mute = nil
    end
    -- volume: speaker, then a wedge (not in the compact bar)
    local vol_w = 0
    if not compact then
        vol_w = 36 * d + 92 * d + 14 * d
    end
    local seek_min = 240 * d
    local limit = x + tw + vol_w + seek_min
    for _, name in ipairs({ "snap", "chap", "speed" }) do
        if left_edge >= limit then break end
        dropped[name] = true
        left_edge = place()
    end
    R.tr = left_edge - vol_w - 6 * d                    -- right edge of the total time
    if not compact then
        R.mute = { x = left_edge - vol_w + 2 * d, y = cy - 15 * d, w = 32 * d, h = 30 * d }
        R.vol = { x = R.mute.x + 36 * d, y = cy - 14 * d, w = 92 * d, h = 28 * d }
    end
    R.seek = { x = R.tl + tw, y = cy - 18 * d, w = math.max(60 * d, R.tr - tw - (R.tl + tw)), h = 36 * d }
    R.seek.draw_y, R.seek.draw_h = cy - 14 * d, 28 * d

    -- the panel hangs on the right, just above the bar
    local panel_w = math.min((st.panel and (st.panel.tab == "queue" or st.panel.tab == "bookmarks" or st.panel.tab == "chapters") and 360 or 420) * d, w - 2 * 14 * d)
    local top = 22 * d
    local ph = math.min(R.bar.y - 14 * d - top, 720 * d)
    R.qpanel = { x = w - 14 * d - panel_w, y = R.bar.y - 14 * d - ph, w = panel_w, h = ph }
    return R
end

local function ico_wave(cx, cy, d, col)
    local hs = { 0.35, 0.75, 1.25, 0.85, 0.45 }
    for i, k in ipairs(hs) do
        local bx = cx + (i - 3) * 4.4 * d
        shape(rrect(bx - 0.9 * d, cy - k * 7 * d, 1.8 * d, k * 14 * d, 0.9 * d), col, 0)
    end
end

local function ico_cc(cx, cy, d, col, bg)
    shape(rrect(cx - 10 * d, cy - 7.5 * d, 20 * d, 15 * d, 3 * d), col, 0)
    shape(rrect(cx - 8.6 * d, cy - 6.1 * d, 17.2 * d, 12.2 * d, 2 * d), bg, 0)
    shape(rrect(cx - 6 * d, cy - 1.8 * d, 5 * d, 1.8 * d, 0.9 * d), col, 0)
    shape(rrect(cx + 0.6 * d, cy - 1.8 * d, 5.4 * d, 1.8 * d, 0.9 * d), col, 0)
    shape(rrect(cx - 6 * d, cy + 1.8 * d, 8 * d, 1.8 * d, 0.9 * d), col, 0)
    shape(rrect(cx + 3 * d, cy + 1.8 * d, 3 * d, 1.8 * d, 0.9 * d), col, 0)
end

local function ico_gear(cx, cy, d, col, bg)
    local R1, R2 = 9.4 * d, 7.2 * d
    local pts = {}
    for i = 0, 7 do
        local a = math.rad(i * 45)
        local w = math.rad(11)
        for _, o in ipairs({ { a - w * 1.5, R2 }, { a - w, R1 }, { a + w, R1 }, { a + w * 1.5, R2 } }) do
            pts[#pts + 1] = cx + o[2] * math.cos(o[1]); pts[#pts + 1] = cy + o[2] * math.sin(o[1])
        end
    end
    shape(poly(pts), col, 0)
    shape(rrect(cx - 4.4 * d, cy - 4.4 * d, 8.8 * d, 8.8 * d, 4.4 * d), bg, 0)
    shape(poly({ cx - 1.6 * d, cy - 2.6 * d, cx + 2.8 * d, cy, cx - 1.6 * d, cy + 2.6 * d }), col, 0)
end

local function ico_list(cx, cy, d, col)
    for k = -1, 1 do
        shape(rrect(cx - 9.5 * d, cy + k * 5.6 * d - 1 * d, 2 * d, 2 * d, 0.6 * d), col, 0)
        shape(rrect(cx - 5.6 * d, cy + k * 5.6 * d - 0.9 * d, 15 * d, 1.8 * d, 0.9 * d), col, 0)
    end
end

local function ico_chap(cx, cy, d, col)
    shape(poly({ cx - 10 * d, cy - 8 * d, cx - 5.4 * d, cy - 5.2 * d, cx - 10 * d, cy - 2.4 * d }), col, 0)
    for k = 0, 2 do
        shape(rrect(cx - 3 * d, cy + (k - 1) * 5.6 * d - 0.9 * d, 13 * d, 1.8 * d, 0.9 * d), col, 0)
    end
end

local function ico_speed(cx, cy, d, col, bg)
    shape(arc_band(cx, cy + 1 * d, 9 * d, 150, 390, 1.8 * d), col, 0)
    shape(poly({ cx - 1 * d, cy + 2 * d, cx + 5.5 * d, cy - 4.5 * d, cx + 2.2 * d, cy + 3.2 * d }), col, 0)
end

local function ico_snap(cx, cy, d, col, bg)
    shape(rrect(cx - 10 * d, cy - 6 * d, 20 * d, 14 * d, 3 * d), col, 0)
    shape(rrect(cx - 4 * d, cy - 9 * d, 8 * d, 4 * d, 1.5 * d), col, 0)
    shape(rrect(cx - 3.6 * d, cy - 2 * d, 7.2 * d, 7.2 * d, 3.6 * d), bg, 0)
end

local function ico_fs(cx, cy, d, col)
    local a, t, l = 7.5 * d, 1.9 * d, 5 * d
    for _, q in ipairs({ { -1, -1 }, { 1, -1 }, { -1, 1 }, { 1, 1 } }) do
        local ox, oy = cx + q[1] * a, cy + q[2] * a
        shape(rrect(math.min(ox, ox - q[1] * l), oy - (q[2] > 0 and t or 0), l, t, 0.6 * d), col, 0)
        shape(rrect(ox - (q[1] > 0 and t or 0), math.min(oy, oy - q[2] * l), t, l, 0.6 * d), col, 0)
    end
end

local function ico_skip(cx, cy, d, col, dir, n)
    -- an open circle with an arrow head, and the number inside
    local r = 9.5 * d
    if dir < 0 then
        shape(arc_band(cx, cy + 0.5 * d, r, -60, 235, 1.8 * d), col, 0)
        shape(poly({ cx - r * 0.5 - 1.5 * d, cy - r * 0.86 - 3.6 * d, cx - r * 0.5 + 4 * d, cy - r * 0.86 + 0.8 * d, cx - r * 0.5 - 3 * d, cy - r * 0.86 + 3.5 * d }), col, 0)
    else
        shape(arc_band(cx, cy + 0.5 * d, r, -55, 240, 1.8 * d), col, 0)
        shape(poly({ cx + r * 0.5 + 1.5 * d, cy - r * 0.86 - 3.6 * d, cx + r * 0.5 - 4 * d, cy - r * 0.86 + 0.8 * d, cx + r * 0.5 + 3 * d, cy - r * 0.86 + 3.5 * d }), col, 0)
    end
    text(cx, cy + 1 * d, tostring(n), 8.5 * d, col, 0, 5, true)
end

local function ico_prevnext(cx, cy, d, col, dir)
    local a = 6.5 * d
    if dir < 0 then
        shape(poly({ cx + a * 0.9, cy - a, cx - a * 0.6, cy, cx + a * 0.9, cy + a }), col, 0)
        shape(rrect(cx - a * 1.1, cy - a, 2.2 * d, a * 2, 0.8 * d), col, 0)
    else
        shape(poly({ cx - a * 0.9, cy - a, cx + a * 0.6, cy, cx - a * 0.9, cy + a }), col, 0)
        shape(rrect(cx + a * 1.1 - 2.2 * d, cy - a, 2.2 * d, a * 2, 0.8 * d), col, 0)
    end
end

-- The rows each panel tab shows. Kinds: head (a heading), item (a choice with a check), val (a setting, with - and + or one button), btn.
local function panel_rows(tab)
    local rows = {}
    local function head(t, extra) rows[#rows + 1] = { t = "head", text = t, extra = extra } end
    local function item(label, detail, on, run) rows[#rows + 1] = { t = "item", label = label, detail = detail, on = on, run = run } end
    local function val(label, get, action, step, btn) rows[#rows + 1] = { t = "val", label = label, get = get, action = action, step = step, btn = btn } end
    local function btn(label, run) rows[#rows + 1] = { t = "btn", label = label, run = run } end
    local tl = mp.get_property_native("track-list") or {}
    local function msg(...) local a = { ... }; return function() mp.commandv("script-message", (unpack or table.unpack)(a)) end end
    if tab == "queue" then
        for i, name in ipairs(st.queue.items) do
            rows[#rows + 1] = { t = "queue", index = i, label = tostring(name), on = (i - 1) == st.queue.cur }
        end
    elseif tab == "bookmarks" then
        for _, b in ipairs(st.bookmarks) do
            item(b.label, fmt(b.t), false, function() mp.commandv("seek", string.format("%.3f", b.t), "absolute+exact") end)
        end
        btn("Add a bookmark here", msg("lumina-bookmark"))
        if #st.bookmarks > 0 then btn("Remove all bookmarks", msg("lumina-bookmark-clear")) end
    elseif tab == "chapters" then
        local cur = mp.get_property_number("chapter") or -1
        for i, c in ipairs(mp.get_property_native("chapter-list") or {}) do
            item(c.title ~= "" and c.title or ("Chapter " .. i), fmt(c.time), i - 1 == cur, function() mp.set_property_number("chapter", i - 1) end)
        end
    elseif tab == "playback" then
        head("General")
        val("Speed", function() return string.format("%.2fx", num("speed", 1)) end, "speed", 0.05)
        val("A-B loop", ab_state, "ab-loop")
        val("Jump to time", function() return "Go" end, "jump")
        val("Sleep timer", function() return st.sleep_label or "Off" end, "sleep-cycle")
        val("Record", function() return st.recording and "Recording" or "Off" end, "record")
        val("Miniplayer", function() return st.mini and "On" or "Off" end, "miniplayer")
        head("Information")
        val("Media information", function() return "Show" end, "info")
        val("Bookmarks", function() return tostring(#st.bookmarks) end, "bookmarks")
        val("Seek-bar preview", function() return opt.thumbs == "yes" and "On" or "Off" end, "thumbs-toggle")
        btn("Reset everything", function() do_action("reset-all") end)
    elseif tab == "video" then
        head("Defaults")
        val("Zoom", function() return string.format("%d%%", math.floor(2 ^ num("video-zoom") * 100 + 0.5)) end, "zoom", 0.1)
        val("Rotate", function() return string.format("%d deg", num("video-rotate")) end, "rotate")
        head("Aspect ratio")
        for i, a in ipairs(ASPECTS) do item(a[2], "", st.aspect_i == i, function() actions["aspect"](i) end) end
        head("Crop")
        for i, c in ipairs(CROPS) do item(c[2], "", st.crop_i == i, function() actions["crop"](i) end) end
        head("Colour")
        for _, p in ipairs({ { "Brightness", "brightness" }, { "Contrast", "contrast" }, { "Saturation", "saturation" }, { "Gamma", "gamma" }, { "Hue", "hue" } }) do
            val(p[1], function() return string.format("%d", num(p[2])) end, p[2], 5)
        end
        head("HDR")
        val("Signal", function() return hdr_signal() end, "hdr-info")
        val("Tone mapping", function() return tostring(mp.get_property("tone-mapping") or "auto") end, "hdr-tonemap")
        val("Peak (nits)", function() return tostring(mp.get_property("target-peak") or "auto") end, "hdr-peak")
        val("Detect peak", function() return tostring(mp.get_property("hdr-compute-peak") or "auto") end, "hdr-computepeak")
        val("Tell the display", function() return tostring(mp.get_property("target-colorspace-hint") or "auto") end, "hdr-hint")
        btn("Back to automatic", function() do_action("hdr-reset") end)
    elseif tab == "audio" then
        local aid = mp.get_property_native("aid")
        head("Audio tracks")
        local any = false
        for _, t in ipairs(tl) do
            if t.type == "audio" then
                any = true
                item(track_name(t), track_detail(t), aid == t.id, function() mp.set_property_number("aid", t.id) end)
            end
        end
        if not any then item("No audio tracks", "", false, function() end) end
        head("Output device")
        local cur = mp.get_property("audio-device") or "auto"
        for _, dv in ipairs(mp.get_property_native("audio-device-list") or {}) do
            item(clip_text_basic(dv.description or dv.name, 34), "", dv.name == cur, function()
                mp.set_property("audio-device", dv.name)
                mp.commandv("script-message", "lumina-audio-device", dv.name)
            end)
        end
        head("Adjustments")
        val("Audio delay", function() return signed(num("audio-delay"), "s") end, "audio-delay", 0.1)
        head("Effects")
        val("Equalizer", function() return st.fx.eq end, "fx-eq")
        val("Compressor", function() return st.fx.comp end, "fx-comp")
        val("Spatial sound", function() return st.fx.space end, "fx-space")
        val("Stereo width", function() return string.format("%d%%", st.fx.width) end, "fx-width", 10)
        val("Even out loudness", function() return onoff(st.fx.norm) end, "fx-norm")
        val("Night mode", function() return onoff(st.fx.night) end, "fx-night")
    elseif tab == "subs" then
        local sid = mp.get_property_native("sid")
        head("Embedded Tracks")
        item("None", "", not sid, function() mp.set_property("sid", "no") end)
        for _, t in ipairs(tl) do
            if t.type == "sub" and not t.external then item(track_name(t), track_detail(t), sid == t.id, function() mp.set_property_number("sid", t.id) end) end
        end
        head("External Tracks", "add")
        for _, t in ipairs(tl) do
            if t.type == "sub" and t.external then item(track_name(t), track_detail(t), sid == t.id, function() mp.set_property_number("sid", t.id) end) end
        end
        head("Appearance")
        val("Delay", function() return signed(num("sub-delay"), "s") end, "sub-delay", 0.1)
        val("Size", function() return string.format("%d%%", math.floor(num("sub-scale", 1) * 100 + 0.5)) end, "sub-size", 0.1)
        val("Position", function() return string.format("%d", num("sub-pos", 100)) end, "sub-pos", 5)
        btn("Find subtitles online", msg("lumina-subs"))
        btn("Choose from all matches", msg("lumina-subs-pick"))
    end
    return rows
end

dock_open_panel = function(tab)
    if tab == nil or tab == "" then st.panel = nil
    elseif st.panel and st.panel.tab == tab then st.panel = nil
    else st.panel = { tab = tab, scroll = 0 } end
    st.menu, st.gopen, st.qopen = nil, false, false
    publish("panel", st.panel and st.panel.tab or "")
    publish("gear", st.panel and st.panel.tab ~= "queue" and st.panel.tab ~= "bookmarks" and st.panel.tab ~= "chapters" and "open" or "")
    dirty = true
end

local function dock_draw_panel(R, w, h, c)
    st.grects = {}
    local p = st.panel
    if not p then return end
    local d, P = R.d, R.qpanel
    shape(rrect(P.x, P.y, P.w, P.h, 14 * d), c.panel, 0)
    local hh = 46 * d
    shape(rrect(P.x, P.y, P.w, hh, 14 * d), c.head, 0)
    shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f %.1f %.1f", P.x, P.y + hh - 14 * d, P.x + P.w, P.y + hh - 14 * d, P.x + P.w, P.y + hh, P.x, P.y + hh), c.head, 0)

    -- tabs
    local set = tab_set(p.tab)
    local tw = P.w / #set
    for i, tb in ipairs(set) do
        local r = { x = P.x + (i - 1) * tw, y = P.y, w = tw, h = hh, name = "ptab:" .. tb[1] }
        st.grects[#st.grects + 1] = r
        local on = p.tab == tb[1]
        local col = on and c.bright or (st.hover == r.name and c.bright or c.muted)
        local label_w = #tb[2] * 7.6 * d
        local ix = r.x + r.w / 2 - (label_w + 22 * d) / 2 + 8 * d
        local iy = r.y + hh / 2 - 1 * d
        if tb[1] == "queue" then ico_list(ix, iy, d * 0.8, col)
        elseif tb[1] == "bookmarks" then
            shape(poly({ ix - 5 * d, iy - 7 * d, ix + 5 * d, iy - 7 * d, ix + 5 * d, iy + 7 * d, ix, iy + 3 * d, ix - 5 * d, iy + 7 * d }), col, 0)
            shape(poly({ ix - 3.4 * d, iy - 5.4 * d, ix + 3.4 * d, iy - 5.4 * d, ix + 3.4 * d, iy + 3.6 * d, ix, iy + 0.6 * d, ix - 3.4 * d, iy + 3.6 * d }), c.head, 0)
        elseif tb[1] == "chapters" then ico_chap(ix, iy, d * 0.8, col)
        elseif tb[1] == "playback" then
            shape(poly({ ix - 5 * d, iy - 7 * d, ix + 6 * d, iy, ix - 5 * d, iy + 7 * d }), col, 0)
            shape(poly({ ix - 3 * d, iy - 3.4 * d, ix + 2.6 * d, iy, ix - 3 * d, iy + 3.4 * d }), c.head, 0)
        elseif tb[1] == "video" then
            shape(rrect(ix - 7 * d, iy - 5 * d, 14 * d, 11 * d, 2 * d), col, 0)
            shape(rrect(ix - 5.6 * d, iy - 3 * d, 11.2 * d, 8 * d, 1.4 * d), c.head, 0)
            shape(rrect(ix - 7 * d, iy - 8 * d, 14 * d, 3 * d, 1 * d), col, 0)
        elseif tb[1] == "audio" then ico_wave(ix, iy, d * 0.8, col)
        elseif tb[1] == "subs" then ico_cc(ix, iy, d * 0.8, col, c.head) end
        text(ix + 13 * d, r.y + hh / 2 - 1 * d, tb[2], 15.5 * d, col, 0, 4, false)
        if on then shape(rrect(r.x + 10 * d, r.y + hh - 3 * d, r.w - 20 * d, 3 * d, 1.5 * d), c.orange, 0) end
    end

    local rows = panel_rows(p.tab)
    st.prows = rows
    local labels = {}
    for _, r in ipairs(rows) do labels[#labels + 1] = (r.t == "head" and "#" or "") .. tostring(r.label or r.text or "") end
    publish("panel_rows", table.concat(labels, "|"))
    local ih = 42 * d
    local top = P.y + hh + 8 * d
    local bottom = P.y + P.h - 10 * d
    local avail = bottom - top
    local total = 0
    for _, r in ipairs(rows) do total = total + (r.t == "head" and 36 * d or ih) end
    -- the queue tab keeps a small header with the item count and the queue buttons
    local qhead = 0
    if p.tab == "queue" then
        qhead = 44 * d
        top = top + qhead
        avail = bottom - top
        local mins = 0
        text(P.x + 22 * d, P.y + hh + 8 * d + qhead / 2, string.format("%d items", #st.queue.items), 14 * d, c.muted, 0, 4, false)
        local bx = P.x + P.w - 20 * d
        for _, b in ipairs({ { "q_save", "plus" }, { "q_clear", "minus" }, { "q_shuffle", "shuffle" }, { "q_repeat", "repeat" } }) do
            local r = { x = bx - 28 * d, y = P.y + hh + 8 * d + qhead / 2 - 14 * d, w = 28 * d, h = 28 * d, name = b[1] }
            st.grects[#st.grects + 1] = r
            local on = (b[1] == "q_shuffle" and st.queue.shuffle) or (b[1] == "q_repeat" and st.queue.repeat_mode ~= "off")
            if st.hover == r.name or on then shape(rrect(r.x, r.y, r.w, r.h, 7 * d), on and c.sel or c.hover, 0) end
            local mx, my, col = r.x + r.w / 2, r.y + r.h / 2, on and c.orange or c.text
            if b[2] == "plus" then
                shape(rrect(mx - 6 * d, my - 0.9 * d, 12 * d, 1.8 * d, 0.9 * d), col, 0); shape(rrect(mx - 0.9 * d, my - 6 * d, 1.8 * d, 12 * d, 0.9 * d), col, 0)
            elseif b[2] == "minus" then
                shape(rrect(mx - 6 * d, my - 0.9 * d, 12 * d, 1.8 * d, 0.9 * d), col, 0)
            elseif b[2] == "shuffle" then
                shape(poly({ mx - 7 * d, my - 4.4 * d, mx - 3 * d, my - 4.4 * d, mx + 3 * d, my + 3.6 * d, mx + 7 * d, my + 3.6 * d, mx + 7 * d, my + 5.2 * d, mx + 2 * d, my + 5.2 * d, mx - 4 * d, my - 2.8 * d, mx - 7 * d, my - 2.8 * d }), col, 0)
                shape(poly({ mx - 7 * d, my + 5.2 * d, mx - 7 * d, my + 3.6 * d, mx - 3 * d, my + 3.6 * d, mx - 1.6 * d, my + 1.6 * d, mx - 0.6 * d, my + 3 * d, mx - 3 * d, my + 5.2 * d }), col, 0)
                shape(poly({ mx + 2 * d, my - 5.2 * d, mx + 7 * d, my - 5.2 * d, mx + 7 * d, my - 2.8 * d, mx + 3.6 * d, my - 2.8 * d, mx + 1.4 * d, my - 0.4 * d, mx + 0.4 * d, my - 1.8 * d }), col, 0)
            else
                shape(arc_band(mx, my, 6 * d, -40, 250, 1.7 * d), col, 0)
                shape(poly({ mx + 3 * d, my - 7.5 * d, mx + 8 * d, my - 4.4 * d, mx + 2.4 * d, my - 2.2 * d }), col, 0)
            end
            bx = bx - 32 * d
        end
    end
    local maxscroll = math.max(0, total - avail)
    p.scroll = clamp(p.scroll or 0, 0, maxscroll)
    local y = top - p.scroll
    
    local function visible(y0, hh0) return y0 + hh0 > top and y0 < bottom end
    if #rows == 0 then
        text(P.x + P.w / 2, top + 30 * d, p.tab == "queue" and "Nothing is queued" or "Nothing here", 14 * d, c.muted, 0, 5, false)
    end
    for k, row in ipairs(rows) do
        local rh = row.t == "head" and 36 * d or ih
        if visible(y, rh) and y >= top - 1 and y + rh <= bottom + 1 then
            local cy = y + rh / 2
            if row.t == "head" then
                text(P.x + 22 * d, cy + 4 * d, row.text, 15 * d, c.muted, 0, 4, false)
                if row.extra == "add" then
                    local r = { x = P.x + P.w - 22 * d - 28 * d, y = cy - 12 * d, w = 28 * d, h = 28 * d, name = "pr:add" }
                    st.grects[#st.grects + 1] = r
                    if st.hover == r.name then shape(rrect(r.x, r.y, r.w, r.h, 7 * d), c.hover, 0) end
                    local mx, my = r.x + r.w / 2, r.y + r.h / 2
                    shape(rrect(mx - 6 * d, my - 0.9 * d, 12 * d, 1.8 * d, 0.9 * d), c.text, 0); shape(rrect(mx - 0.9 * d, my - 6 * d, 1.8 * d, 12 * d, 0.9 * d), c.text, 0)
                end
            elseif row.t == "item" or row.t == "btn" or row.t == "queue" then
                local r = { x = P.x + 8 * d, y = y, w = P.w - 16 * d, h = rh, name = "pr:" .. k }
                st.grects[#st.grects + 1] = r
                if st.hover == r.name then shape(rrect(r.x, r.y + 2 * d, r.w, r.h - 4 * d, 8 * d), c.hover, 0) end
                local label = row.label
                local lx = P.x + 52 * d
                if row.t == "item" and row.on then
                    -- a check mark in the accent colour
                    local mx, my = P.x + 30 * d, cy
                    shape(poly({ mx - 6 * d, my + 0.5 * d, mx - 4.4 * d, my - 1.2 * d, mx - 1.8 * d, my + 1.6 * d, mx + 4.6 * d, my - 5 * d, mx + 6.2 * d, my - 3.4 * d, mx - 1.8 * d, my + 4.8 * d }), c.orange, 0)
                elseif row.t == "queue" then
                    shape(rrect(P.x + 24 * d, cy - 4.2 * d, 12 * d, 1.8 * d, 0.9 * d), c.muted, 0); shape(rrect(P.x + 24 * d, cy + 1.2 * d, 12 * d, 1.8 * d, 0.9 * d), c.muted, 0)
                end
                local detail_w = row.detail and #row.detail > 0 and 70 * d or 0
                local room = math.max(8, math.floor((P.w - 52 * d - 24 * d - detail_w) / (7.4 * d)))
                if row.t == "btn" then lx = P.x + P.w / 2; text(lx, cy, clip_text_basic(label, room), 17 * d, c.bright, 0, 5, false)
                else text(lx, cy, clip_text_basic(label, room), 17 * d, row.on and c.orange or c.text, 0, 4, false) end
                if detail_w > 0 then text(P.x + P.w - 24 * d, cy, row.detail, 14 * d, c.muted, 0, 6, false) end
                if row.t == "queue" and st.hover == r.name then
                    -- remove, shown only under the pointer so the list stays calm
                    local g = { x = P.x + P.w - 44 * d, y = cy - 13 * d, w = 26 * d, h = 26 * d, name = "qx:" .. row.index }
                    st.grects[#st.grects + 1] = g
                    text(g.x + g.w / 2, g.y + g.h / 2, "x", 15 * d, c.text, 0, 5, true)
                end
            elseif row.t == "val" then
                text(P.x + 22 * d, cy, row.label, 16.5 * d, c.muted, 0, 4, false)
                local bw = 150 * d
                local bx = P.x + P.w - 20 * d - bw
                if row.step then
                    local b = 30 * d
                    local rm = { x = bx, y = cy - 14 * d, w = b, h = 28 * d, name = "pr:" .. k .. ":m" }
                    local rp = { x = bx + bw - b, y = cy - 14 * d, w = b, h = 28 * d, name = "pr:" .. k .. ":p" }
                    st.grects[#st.grects + 1] = rm; st.grects[#st.grects + 1] = rp
                    for _, r in ipairs({ rm, rp }) do
                        shape(rrect(r.x, r.y, r.w, r.h, 7 * d), r.name == st.hover and c.hover or c.box, 0)
                    end
                    shape(rrect(rm.x + 9 * d, cy - 0.9 * d, 12 * d, 1.8 * d, 0.9 * d), c.bright, 0)
                    shape(rrect(rp.x + 9 * d, cy - 0.9 * d, 12 * d, 1.8 * d, 0.9 * d), c.bright, 0); shape(rrect(rp.x + 14.1 * d, cy - 6 * d, 1.8 * d, 12 * d, 0.9 * d), c.bright, 0)
                    text(bx + bw / 2, cy, row.get(), 16.5 * d, c.bright, 0, 5, false)
                else
                    local r = { x = bx, y = cy - 14 * d, w = bw, h = 28 * d, name = "pr:" .. k .. ":p" }
                    st.grects[#st.grects + 1] = r
                    shape(rrect(r.x, r.y, r.w, r.h, 7 * d), c.boxline, st.hover == r.name and 0x40 or 0x90)
                    shape(rrect(r.x + 1.5 * d, r.y + 1.5 * d, r.w - 3 * d, r.h - 3 * d, 6 * d), st.hover == r.name and c.hover or c.box, 0)
                    text(r.x + r.w / 2, cy, clip_text_basic(tostring(row.get()), 16), 16.5 * d, c.bright, 0, 5, false)
                end
            end
        end
        y = y + rh
    end
    if total > avail then
        local track = avail
        local bar = math.max(28 * d, track * avail / total)
        local by = top + (track - bar) * (p.scroll / maxscroll)
        shape(rrect(P.x + P.w - 8 * d, by, 3.5 * d, bar, 1.7 * d), c.text, 0x90)
    end
end

local function dock_draw_modern(R, w, h, c, d)
    scrim_bottom(w, h, 250 * d, 0.62)
    local B = R.bar
    -- the glass: a hairline edge, the body, a faint light edge along the top
    local rad = B.h * 0.34
    shape(rrect(B.x - 1, B.y - 1, B.w + 2, B.h + 2, rad + 1), c.boxline, 0x68)
    shape(rrect(B.x, B.y, B.w, B.h, rad), c.bar, 0x22)
    shape(rrect(B.x + rad, B.y + 1.5 * d, B.w - 2 * rad, 1.2 * d, 0.6 * d), "FFFFFF", 0xE4)

    local pos = st.dragging and st.drag_t or mp.get_property_number("time-pos") or 0
    local dur = mp.get_property_number("duration") or 0

    -- the seek bar: a slim line that thickens under the pointer, with the buffered stretch, chapter notches and a handle
    local sk = R.seek
    local sy = sk.draw_y + sk.draw_h / 2
    local active = st.dragging or in_rect(sk, st.mx or -1, st.my or -1)
    local th = (active and 9 or 5) * d
    local ty = sy - th / 2
    local frac = (dur > 0) and clamp(pos / dur, 0, 1) or 0
    shape(rrect(sk.x, ty, sk.w, th, th / 2), c.trough, 0x20)
    local cached = mp.get_property_number("demuxer-cache-time")
    if dur > 0 and cached and cached > pos then shape(rrect(sk.x, ty, sk.w * clamp(cached / dur, 0, 1), th, th / 2), c.text, 0xB8) end
    shape(rrect(sk.x, ty, math.max(th, sk.w * frac), th, th / 2), c.orange, 0)
    if dur > 0 then
        for _, ch in ipairs(mp.get_property_native("chapter-list") or {}) do
            if ch.time and ch.time > 1 and ch.time < dur - 1 then
                shape(rrect(sk.x + sk.w * (ch.time / dur) - 1 * d, ty - 1 * d, 2 * d, th + 2 * d, 1 * d), c.bar, 0)
            end
        end
        for _, b in ipairs(st.bookmarks) do
            if b.t >= 0 and b.t <= dur then
                local bx = sk.x + sk.w * (b.t / dur)
                shape(poly({ bx, ty - 11 * d, bx + 4 * d, ty - 6 * d, bx, ty - 1 * d, bx - 4 * d, ty - 6 * d }), c.orange, 0)
            end
        end
    end
    local tr_ = (active and 8.5 or 5.5) * d
    local tx = clamp(sk.x + sk.w * frac, sk.x + tr_, sk.x + sk.w - tr_)
    shape(rrect(tx - tr_, sy - tr_, 2 * tr_, 2 * tr_, tr_), "FFFFFF", 0)
    text(R.tl, sy, fmt_long(pos), 17 * d, c.bright, 0, 4, false)
    text(R.tr, sy, fmt_long(dur), 17 * d, c.text, 0, 6, false)

    -- buttons
    local function tile(r, name, on)
        local hov = st.hover == name
        if on then shape(rrect(r.x, r.y, r.w, r.h, 13 * d), c.sel, 0)
        elseif hov then shape(rrect(r.x, r.y, r.w, r.h, 13 * d), c.hover, 0x10) end
        return on and c.orange or (hov and "FFFFFF" or c.bright)
    end
    local dd = d * 1.06
    -- Play: the one filled button
    local p = R.play
    local phot = st.hover == "play"
    shape(rrect(p.x, p.y, p.w, p.h, 14 * d), c.orange, phot and 0x40 or 0)
    local px, py = p.x + p.w / 2, p.y + p.h / 2
    if paused() or eof() then
        local a = 8 * d
        shape(poly({ px - a * 0.6, py - a, px + a * 0.98, py, px - a * 0.6, py + a }), c.onorange, 0)
    else
        shape(rrect(px - 6.5 * d, py - 8 * d, 4.4 * d, 16 * d, 1.4 * d), c.onorange, 0)
        shape(rrect(px + 2.1 * d, py - 8 * d, 4.4 * d, 16 * d, 1.4 * d), c.onorange, 0)
    end
    for _, k in ipairs({ { "prev", -1 }, { "next", 1 } }) do
        local r = R[k[1]]
        if r then local col = tile(r, k[1], false); ico_prevnext(r.x + r.w / 2, r.y + r.h / 2, dd, col, k[2]) end
    end
    for _, k in ipairs({ { "back", -1 }, { "fwd", 1 } }) do
        local r = R[k[1]]
        if r then local col = tile(r, k[1], false); ico_skip(r.x + r.w / 2, r.y + r.h / 2, dd, col, k[2], opt.skip) end
    end

    -- volume: the speaker and a slim slider
    local mt, v = R.mute, R.vol or { x = R.mute.x, y = R.mute.y, w = 0, h = R.mute.h }
    local col = tile(mt, "mute", false)
    local vx, vy = mt.x + mt.w / 2 - 2 * d, mt.y + mt.h / 2
    local muted = mp.get_property_native("mute") == true
    local vol = mp.get_property_number("volume") or 100
    local vmax = mp.get_property_number("volume-max") or 100
    shape(poly({ vx - 8 * d, vy - 3.6 * d, vx - 3.6 * d, vy - 3.6 * d, vx + 1.4 * d, vy - 8 * d, vx + 1.4 * d, vy + 8 * d, vx - 3.6 * d, vy + 3.6 * d, vx - 8 * d, vy + 3.6 * d }), col, 0)
    if muted or vol <= 0 then
        shape(poly({ vx + 4.6 * d, vy - 5 * d, vx + 6 * d, vy - 6.4 * d, vx + 11 * d, vy + 6.4 * d, vx + 9.6 * d, vy + 7.8 * d }), col, 0)
    else
        shape(rrect(vx + 5 * d, vy - 2.6 * d, 1.8 * d, 5.2 * d, 0.9 * d), col, 0)
        if vol > 40 then shape(rrect(vx + 8.6 * d, vy - 5.4 * d, 1.8 * d, 10.8 * d, 0.9 * d), col, 0) end
    end
    local vact = st.vdrag or st.hover == "vol"
    local vth = (vact and 7 or 4) * d
    local vcy = v.y + v.h / 2
    local vf = muted and 0 or clamp(vol / vmax, 0, 1)
    if v.w > 0 then shape(rrect(v.x, vcy - vth / 2, v.w, vth, vth / 2), c.volempty, 0x20) end
    if v.w > 0 and vf > 0 then shape(rrect(v.x, vcy - vth / 2, math.max(vth, v.w * vf), vth, vth / 2), c.green, 0) end
    if vact and v.w > 0 then local vr = 6.5 * d; shape(rrect(clamp(v.x + v.w * vf, v.x + vr, v.x + v.w - vr) - vr, vcy - vr, 2 * vr, 2 * vr, vr), "FFFFFF", 0) end

    -- the right-hand tools
    local function active_tab(name)
        local tb = st.panel and st.panel.tab
        if name == "cc" then return tb == "subs" end
        if name == "audio" then return tb == "audio" end
        if name == "gear" then return tb == "playback" or tb == "video" end
        if name == "queue" then return tb == "queue" or tb == "bookmarks" or tb == "chapters" end
        return false
    end
    local function tool(name, icon)
        local r = R[name]
        if not r then return end
        local colr = tile(r, name, active_tab(name))
        icon(r.x + r.w / 2, r.y + r.h / 2, d * 1.05, colr, c.bar)
    end
    tool("chap", function(cx, cy2, dx, cl) ico_chap(cx, cy2, dx, cl) end)
    tool("audio", ico_wave); tool("cc", ico_cc); tool("speed", ico_speed); tool("snap", ico_snap); tool("gear", ico_gear)
    tool("queue", function(cx, cy2, dx, cl) ico_list(cx, cy2, dx, cl) end)
    tool("fs", function(cx, cy2, dx, cl) ico_fs(cx, cy2, dx, cl) end)
end

dock_draw = function(R, w, h)
    local d = R.d
    local c = pal()
    local docked = dock_docked()

    L = {}
    local playing_title = st.title or mp.get_property("media-title") or ""
    local recent = (mp.get_time() - st.last_move) < HIDE_AFTER or paused() or eof()
    if not docked then
        scrim_top(w, 130 * d, 0.5)
        text(R.close.x + R.close.w + 16 * d, 18 * d, clip_text_basic(playing_title, R.compact and 34 or 120), (R.compact and 17 or 20) * d, "F2F2F2", 0, 7, true, FONT_BOLD)
    end
    if not docked or recent then
        local r, hot = R.close, st.hover == "close"
        shape(rrect(r.x, r.y, r.w, r.h, 10 * d), hot and c.hover or c.box, hot and 0x20 or 0x40)
        local a, my = r.x + 14 * d, r.y + r.h / 2
        shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f %.1f %.1f %.1f %.1f %.1f %.1f", a + 6 * d, my - 7.5 * d, a - 0.5 * d, my, a + 6 * d, my + 7.5 * d,
            a + 8 * d, my + 5.5 * d, a + 4 * d, my, a + 8 * d, my - 5.5 * d), "F2F2F2", 0)
        if not R.compact then text(a + 18 * d, my, "Back", 15 * d, "F2F2F2", 0, 4, true, FONT_BOLD) end
    end
    if st.recording then
        shape(rrect(w - R.M - 70 * d, 24 * d, 12 * d, 12 * d, 6 * d), "3A3AE8", 0)
        text(w - R.M, 18 * d, "REC", 18 * d, "F2F2F2", 0, 9, true, FONT_BOLD)
    end

    local B = R.bar
    if R.modern then dock_draw_modern(R, w, h, c, d) else
    shape(string.format("m 0 %.1f l %d %.1f %d %d 0 %d", B.y, w, B.y, w, h, h), c.bar, docked and 0 or 0x18)
    local cy = B.y + B.h / 2
    local pos = st.dragging and st.drag_t or mp.get_property_number("time-pos") or 0
    local dur = mp.get_property_number("duration") or 0

    -- Play pill
    local p = R.play
    local hot = st.hover == "play"
    shape(rrect(p.x, p.y, p.w, p.h, 7 * d), c.orange, hot and 0x30 or 0)
    local px, py = p.x + p.w / 2, p.y + p.h / 2
    if paused() or eof() then
        local a = 7 * d
        shape(poly({ px - a * 0.6, py - a, px + a * 0.95, py, px - a * 0.6, py + a }), c.onorange, 0)
    else
        shape(rrect(px - 6 * d, py - 7 * d, 4 * d, 14 * d, 1 * d), c.onorange, 0)
        shape(rrect(px + 2 * d, py - 7 * d, 4 * d, 14 * d, 1 * d), c.onorange, 0)
    end
    for _, k in ipairs({ { "prev", -1 }, { "next", 1 } }) do
        local r = R[k[1]]
        if r then
            if st.hover == k[1] then shape(rrect(r.x, r.y, r.w, r.h, 7 * d), c.hover, 0) end
            ico_prevnext(r.x + r.w / 2, r.y + r.h / 2, d, st.hover == k[1] and c.bright or c.text, k[2])
        end
    end
    for _, k in ipairs({ { "back", -1 }, { "fwd", 1 } }) do
        local r = R[k[1]]
        if r then
            if st.hover == k[1] then shape(rrect(r.x, r.y, r.w, r.h, 7 * d), c.hover, 0) end
            ico_skip(r.x + r.w / 2, r.y + r.h / 2, d, st.hover == k[1] and c.bright or c.text, k[2], opt.skip)
        end
    end

    -- times and the seek trough
    text(R.tl, cy, fmt_long(pos), 17.5 * d, c.text, 0, 4, false)
    text(R.tr, cy, fmt_long(dur), 17.5 * d, c.text, 0, 6, false)
    local sk = R.seek
    local ty, th = sk.draw_y, sk.draw_h
    local frac = (dur > 0) and clamp(pos / dur, 0, 1) or 0
    shape(rrect(sk.x, ty, sk.w, th, 6 * d), c.trough, 0)
    if frac > 0 then shape(rrect(sk.x, ty, math.max(8 * d, sk.w * frac), th, 6 * d), c.fill, 0) end
    local chapters = mp.get_property_native("chapter-list") or {}
    if dur > 0 then
        for _, ch in ipairs(chapters) do
            if ch.time and ch.time > 1 and ch.time < dur - 1 then
                local cx = sk.x + sk.w * (ch.time / dur)
                shape(rrect(cx - 0.8 * d, ty + 6 * d, 1.6 * d, th - 12 * d, 0.8 * d), c.text, 0xA0)
            end
        end
        for _, b in ipairs(st.bookmarks) do
            if b.t >= 0 and b.t <= dur then
                local bx = sk.x + sk.w * (b.t / dur)
                shape(poly({ bx, ty + 3 * d, bx + 3.5 * d, ty + 8 * d, bx, ty + 13 * d, bx - 3.5 * d, ty + 8 * d }), c.orange, 0)
            end
        end
    end
    local active = st.dragging or in_rect(sk, st.mx or -1, st.my or -1)
    local tx = sk.x + sk.w * frac
    local tw_ = (active and 9 or 7) * d
    shape(rrect(clamp(tx - tw_ / 2, sk.x, sk.x + sk.w - tw_), ty, tw_, th, 3 * d), c.thumb, 0)

    -- volume: speaker and a wedge
    if R.mute and R.vol then
        local mt = R.mute
        if st.hover == "mute" then shape(rrect(mt.x, mt.y, mt.w, mt.h, 7 * d), c.hover, 0) end
        local vx, vy = mt.x + mt.w / 2 - 2 * d, mt.y + mt.h / 2
        local muted = mp.get_property_native("mute") == true
        local vol = mp.get_property_number("volume") or 100
        local vmax = mp.get_property_number("volume-max") or 100
        shape(poly({ vx - 8 * d, vy - 3.6 * d, vx - 3.6 * d, vy - 3.6 * d, vx + 1.4 * d, vy - 8 * d, vx + 1.4 * d, vy + 8 * d, vx - 3.6 * d, vy + 3.6 * d, vx - 8 * d, vy + 3.6 * d }), c.text, 0)
        if muted or vol <= 0 then
            shape(poly({ vx + 4.6 * d, vy - 5 * d, vx + 6 * d, vy - 6.4 * d, vx + 11 * d, vy + 6.4 * d, vx + 9.6 * d, vy + 7.8 * d }), c.text, 0)
        else
            shape(rrect(vx + 5 * d, vy - 2.6 * d, 1.8 * d, 5.2 * d, 0.9 * d), c.text, 0)
            if vol > 40 then shape(rrect(vx + 8.6 * d, vy - 5.4 * d, 1.8 * d, 10.8 * d, 0.9 * d), c.text, 0) end
        end
        local v = R.vol
        local vcy = v.y + v.h / 2
        local function wedge(t0, t1, col)       -- the slice of the wedge between two fractions of its length
            local function hgt(t) return (5 + 8 * t) * d end
            local x0, x1 = v.x + v.w * t0, v.x + v.w * t1
            shape(poly({ x0, vcy - hgt(t0), x1, vcy - hgt(t1), x1, vcy + hgt(t1), x0, vcy + hgt(t0) }), col, 0)
        end
        local vf = muted and 0 or clamp(vol / vmax, 0, 1)
        wedge(0, 1, c.volempty)
        if vf > 0 then wedge(0, vf, c.green) end
    end

    -- right-hand buttons
    local function active_tab(name)
        local t = st.panel and st.panel.tab
        if name == "cc" then return t == "subs" end
        if name == "audio" then return t == "audio" end
        if name == "gear" then return t == "playback" or t == "video" end
        if name == "queue" then return t == "queue" or t == "bookmarks" or t == "chapters" end
        return false
    end
    local function button(name, draw_icon)
        local r = R[name]
        if not r then return end
        local on = active_tab(name)
        local hov = st.hover == name
        local bg = on and c.orange or c.bar
        if on then shape(rrect(r.x, r.y, r.w, r.h, 7 * d), c.orange, 0) elseif hov then shape(rrect(r.x, r.y, r.w, r.h, 7 * d), c.hover, 0); bg = c.hover end
        draw_icon(r.x + r.w / 2, r.y + r.h / 2, d, on and c.onorange or (hov and c.bright or c.text), bg)
    end
    button("chap", function(cx, cy2, dd, col) ico_chap(cx, cy2, dd, col) end)
    button("audio", ico_wave)
    button("cc", ico_cc)
    button("speed", ico_speed)
    button("snap", ico_snap)
    button("gear", ico_gear)
    button("queue", function(cx, cy2, dd, col) ico_list(cx, cy2, dd, col) end)
    button("fs", function(cx, cy2, dd, col) ico_fs(cx, cy2, dd, col) end)

    end   -- legacy bar

    if mp.get_property_native("paused-for-cache") then
        text(w / 2, (h - B.h) / 2, "Buffering", 20 * d, "F2F2F2", 0x30, 5, true)
    end

    dock_draw_panel(R, w, h, c)
    draw_menu(R, w)
    publish_regions(R)
    toast_layer(w, h)
    ov.data = table.concat(L, "\n")
    ov:update()
end

-- ---------- on-screen notes ("Subtitles: English", "Volume 55%") ----------
-- One small rounded card at the top centre, drawn with the controller so it matches it. mpv's own text banner is switched off (osd-level=0).
local function show_toast(s, secs)
    s = tostring(s or "")
    if s == "" then st.toast = nil; dirty = true; return end
    st.toast = { text = s, untl = mp.get_time() + (secs or 2) }
    dirty = true
end
mp.osd_message = function(s, secs) show_toast(s, tonumber(secs) or 2) end
mp.register_script_message("lumina-toast", function(s, ms) show_toast(s, (tonumber(ms) or 2000) / 1000) end)

toast_layer = function(w, h)
    local t = st.toast
    if not t then return end
    local now = mp.get_time()
    if now >= t.untl then st.toast = nil; return end
    local d = clamp(h / 1080, 0.78, 1.7)
    local c = pal()
    local size = 30 * d                  -- big and bold: it has to be read at a glance while the picture moves
    local label = clip_text_basic(t.text, 70)
    local tw = #label * size * 0.6
    local bw, bh = math.min(w - 40 * d, tw + 76 * d), 70 * d
    local x, y = (w - bw) / 2, 72 * d
    local fade = clamp((t.untl - now) / 0.3, 0, 1)
    local saved = st.fade
    st.fade = fade                        -- alpha() scales by it, so the card fades out instead of vanishing
    shape(rrect(x - 1, y - 1, bw + 2, bh + 2, 17 * d), c.boxline, 0x30)
    shape(rrect(x, y, bw, bh, 16 * d), c.box, 0x08)
    text(x + bw / 2, y + bh / 2, label, size, "FFFFFF", 0, 5, true)
    st.fade = saved
end

local function draw()
    local w, h = mp.get_osd_size()
    if st.force then w, h = st.force[1], st.force[2] end      -- test seam: draw as if the window had this size
    if not w or w <= 0 or h <= 0 then return end
    ov.res_x, ov.res_y = w, h
    apply_margin(h)

    if st.hideui then st.fade = 0; st.regions = {} end
    if st.fade <= 0.003 then
        L = {}
        toast_layer(w, h)
        if #L == 0 and st.blank then return end      -- nothing to show and the screen is already clear: skip the redraw (every property change used to trigger one)
        st.blank = #L == 0
        ov.data = table.concat(L, "\n")
        ov:update()
        return
    end
    st.blank = false

    local R = layout(w, h)
    st.regions = R
    if is_dock() then dock_draw(R, w, h); return end
    local s, M = R.s, R.M
    L = {}

    -- scrims: ASS has no vector gradient, so stack bands whose individual opacities are solved
    -- so that the combined result follows a smooth curve (no visible stripes)
    if st.style == "floating" then
        shape(rrect(R.pill.x, R.pill.y, R.pill.w, R.pill.h, 30 * s), "1C1A18", 0x1C)
    else
        scrim_bottom(w, h, 230 * s, 0.80)
    end
    scrim_top(w, 150 * s, 0.50)

    -- title
    local title = st.title or mp.get_property("media-title") or ""
    local tx = R.close and R.close.x + R.close.w + 24 * s or M     -- the title starts after the Close button when there is one
    text(tx, 22 * s, clip_text_basic(title, R.compact and 34 or 120), (R.compact and 20 or 30) * s, C.text, 0, 7, true, FONT_BOLD)
    if R.compact then   -- the miniplayer's queue card: what comes next
        local nxt = st.queue.items[st.queue.cur + 2]
        if nxt then text(tx, 50 * s, "Up next: " .. clip_text_basic(tostring(nxt), 30), 15 * s, C.muted, 0, 7, false) end
    end
    if st.subtitle and st.subtitle ~= "" and not R.compact then text(tx, 60 * s, st.subtitle, 18 * s, C.muted, 0, 7, false) end

    if st.recording then
        shape(rrect(w - M - 86 * s, 30 * s, 14 * s, 14 * s, 7 * s), "3A3AE8", 0)
        text(w - M, 22 * s, "REC", 22 * s, C.text, 0, 9, true, FONT_BOLD)
    end

    -- time + seek bar
    local pos = st.dragging and st.drag_t or mp.get_property_number("time-pos") or 0
    local dur = mp.get_property_number("duration") or 0
    local sk = R.seek
    local ty = sk.y + sk.h / 2
    text(R.tl, ty, fmt(pos), 17 * s, C.text, 0, 4, false)
    text(R.tr, ty, "-" .. fmt(math.max(0, dur - pos)), 17 * s, C.muted, 0, 6, false)

    local hot = st.dragging or in_rect(sk, st.mx or -1, st.my or -1)
    local bar_h = (hot and 7 or 5) * s
    shape(rrect(sk.x, ty - bar_h / 2, sk.w, bar_h, bar_h / 2), C.text, 0xB8)
    local frac = (dur > 0) and clamp(pos / dur, 0, 1) or 0
    if frac > 0 then shape(rrect(sk.x, ty - bar_h / 2, math.max(bar_h, sk.w * frac), bar_h, bar_h / 2), C.accent, 0) end
    -- chapter ticks
    local chapters = mp.get_property_native("chapter-list") or {}
    if dur > 0 then
        for _, c in ipairs(chapters) do
            if c.time and c.time > 1 and c.time < dur - 1 then
                local cx = sk.x + sk.w * (c.time / dur)
                shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f %.1f %.1f", cx - 1, ty - bar_h, cx + 1, ty - bar_h, cx + 1, ty + bar_h, cx - 1, ty + bar_h), C.black, 0x60)
            end
        end
    end
    -- bookmarks: small accent marks on the bar
    if dur > 0 then
        for _, b in ipairs(st.bookmarks) do
            if b.t >= 0 and b.t <= dur then
                local bx = sk.x + sk.w * (b.t / dur)
                shape(rrect(bx - 2 * s, ty - bar_h - 4 * s, 4 * s, bar_h * 2 + 8 * s, 2 * s), C.accent, 0)
            end
        end
    end
    if hot or paused() then
        local kr = 9 * s
        shape(rrect(sk.x + sk.w * frac - kr, ty - kr, kr * 2, kr * 2, kr), C.primary, 0)
    end

    -- transport
    local p = R.play
    local pc_hot = st.hover == "play"
    shape(rrect(p.x, p.y, p.w, p.h, p.w / 2), C.primary, pc_hot and 0x10 or 0)
    local cx, cy = p.x + p.w / 2, p.y + p.h / 2
    if paused() or eof() then
        local a = 11 * s
        shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f", cx - a * 0.7, cy - a, cx + a * 1.05, cy, cx - a * 0.7, cy + a), C.onprim, 0)
    else
        local bw, bhh = 5 * s, 12 * s
        shape(rrect(cx - bw * 1.9, cy - bhh, bw * 1.5, bhh * 2, 1.5 * s), C.onprim, 0)
        shape(rrect(cx + bw * 0.4, cy - bhh, bw * 1.5, bhh * 2, 1.5 * s), C.onprim, 0)
    end
    for _, k in ipairs({ { "back", -1 }, { "fwd", 1 } }) do
        local r = R[k[1]]
        if not r then goto skip_glyph end
        if st.hover == k[1] then shape(rrect(r.x, r.y, r.w, r.h, r.w / 2), C.text, 0xD0) end
        local mx, my = r.x + r.w / 2, r.y + r.h / 2
        local a = 7 * s
        -- a small arrow head plus the number, so the jump size is readable
        if k[2] < 0 then
            shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f", mx - a * 1.9, my, mx - a * 0.5, my - a, mx - a * 0.5, my + a), C.text, 0)
            text(mx + a * 0.9, my, tostring(opt.skip), 15 * s, C.text, 0, 5, true)
        else
            shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f", mx + a * 1.9, my, mx + a * 0.5, my - a, mx + a * 0.5, my + a), C.text, 0)
            text(mx - a * 0.9, my, tostring(opt.skip), 15 * s, C.text, 0, 5, true)
        end
        ::skip_glyph::
    end

    -- right cluster
    local function label_pill(name, label, on)
        local r = R[name]
        if not r then return end
        pill_bg(r, st.hover == name)
        text(r.x + r.w / 2, r.y + r.h / 2, label, 15 * s, on and C.accent or C.text, 0, 5, true)
    end
    if R.close then label_pill("close", "Close", false) end
    label_pill("cc", "CC", track_label("sub") ~= nil)
    if R.chap then label_pill("chap", "Ch", false) end
    label_pill("audio", "Audio", false)
    local sp = mp.get_property_number("speed") or 1
    label_pill("speed", string.format("%gx", sp), sp ~= 1)
    if R.snap then
        local c = R.snap
        if st.hover == "snap" then shape(rrect(c.x, c.y, c.w, c.h, c.w / 2), C.text, 0xD0) end
        local mx, my = c.x + c.w / 2, c.y + c.h / 2
        shape(rrect(mx - 10 * s, my - 6 * s, 20 * s, 14 * s, 3 * s), C.text, 0)
        shape(rrect(mx - 4 * s, my - 9 * s, 8 * s, 4 * s, 1.5 * s), C.text, 0)
        shape(rrect(mx - 4 * s, my - 2.5 * s, 8 * s, 8 * s, 4 * s), C.black, 0x30)

    end

    do
        local c = R.gear
        if st.hover == "gear" or st.gopen then shape(rrect(c.x, c.y, c.w, c.h, c.w / 2), C.text, st.gopen and 0xB0 or 0xD0) end
        local mx, my = c.x + c.w / 2, c.y + c.h / 2
        for k, kx in ipairs({ -0.5, 0.4, -0.1 }) do
            local ly = my + (k - 2) * 7 * s
            shape(rrect(mx - 9 * s, ly - 1 * s, 18 * s, 2 * s, s), C.text, 0x40)
            shape(rrect(mx + kx * 9 * s - 2.6 * s, ly - 3.2 * s, 5.2 * s, 6.4 * s, 2.6 * s), C.text, 0)
        end
    end
    if R.queue then
        local c = R.queue
        if st.hover == "queue" or st.qopen then shape(rrect(c.x, c.y, c.w, c.h, c.w / 2), C.text, st.qopen and 0xB0 or 0xD0) end
        local mx, my = c.x + c.w / 2, c.y + c.h / 2
        for k = -1, 1 do shape(rrect(mx - 9 * s, my + k * 6 * s - 1.2 * s, 18 * s, 2.4 * s, 1.2 * s), C.text, 0) end
    end

    -- volume: speaker button and a short slider on the left (not in the compact controller)
    if R.mute and R.vol then
    local mt = R.mute
    if st.hover == "mute" then shape(rrect(mt.x, mt.y, mt.w, mt.h, mt.w / 2), C.text, 0xD0) end
    local vx, vy = mt.x + mt.w / 2, mt.y + mt.h / 2
    local muted = mp.get_property_native("mute") == true
    local vol = mp.get_property_number("volume") or 100
    local vmax = mp.get_property_number("volume-max") or 100
    shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f %.1f %.1f %.1f %.1f %.1f %.1f",
        vx - 10 * s, vy - 4 * s, vx - 5 * s, vy - 4 * s, vx + 1 * s, vy - 9 * s, vx + 1 * s, vy + 9 * s, vx - 5 * s, vy + 4 * s, vx - 10 * s, vy + 4 * s), C.text, 0)
    if muted or vol <= 0 then
        shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f %.1f %.1f", vx + 4 * s, vy - 6 * s, vx + 6 * s, vy - 8 * s, vx + 12 * s, vy + 8 * s, vx + 10 * s, vy + 10 * s), C.text, 0)
    else
        shape(rrect(vx + 5 * s, vy - 3 * s, 2 * s, 6 * s, s), C.text, 0)
        if vol > 40 then shape(rrect(vx + 9 * s, vy - 6 * s, 2 * s, 12 * s, s), C.text, 0) end
    end
    local v = R.vol
    local vcy = v.y + v.h / 2
    local vbh = 5 * s
    shape(rrect(v.x, vcy - vbh / 2, v.w, vbh, vbh / 2), C.text, 0xB8)
    local vf = muted and 0 or clamp(vol / vmax, 0, 1)
    if vf > 0 then shape(rrect(v.x, vcy - vbh / 2, math.max(vbh, v.w * vf), vbh, vbh / 2), C.accent, 0) end
    if st.vdrag or in_rect(v, st.mx or -1, st.my or -1) then
        local kr = 7 * s
        shape(rrect(v.x + v.w * vf - kr, vcy - kr, kr * 2, kr * 2, kr), C.primary, 0)
        text(v.x + v.w + 14 * s, vcy, string.format("%d%%", muted and 0 or math.floor(vol + 0.5)), 15 * s, C.text, 0, 4, true)
    end
    end

    -- previous / next episode
    for _, k in ipairs({ { "prev", -1 }, { "next", 1 } }) do
        local r = R[k[1]]
        if r then
            if st.hover == k[1] then shape(rrect(r.x, r.y, r.w, r.h, r.w / 2), C.text, 0xD0) end
            local mx, my = r.x + r.w / 2, r.y + r.h / 2
            local a = 8 * s
            if k[2] < 0 then
                shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f", mx + a * 0.9, my - a, mx - a * 0.7, my, mx + a * 0.9, my + a), C.text, 0)
                shape(rrect(mx - a * 1.2, my - a, 2.2 * s, a * 2, s), C.text, 0)
            else
                shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f", mx - a * 0.9, my - a, mx + a * 0.7, my, mx - a * 0.9, my + a), C.text, 0)
                shape(rrect(mx + a * 1.2 - 2.2 * s, my - a, 2.2 * s, a * 2, s), C.text, 0)
            end
        end
    end
    -- fullscreen glyph: four corner brackets
    local f = R.fs
    if st.hover == "fs" then shape(rrect(f.x, f.y, f.w, f.h, f.w / 2), C.text, 0xD0) end
    local fx, fy, a, t = f.x + f.w / 2, f.y + f.h / 2, 8 * s, 2.2 * s
    for _, q in ipairs({ { -1, -1 }, { 1, -1 }, { -1, 1 }, { 1, 1 } }) do
        local ox, oy = fx + q[1] * a, fy + q[2] * a
        shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f %.1f %.1f", ox, oy, ox - q[1] * 6 * s, oy, ox - q[1] * 6 * s, oy - q[2] * t, ox, oy - q[2] * t), C.text, 0)
        shape(string.format("m %.1f %.1f l %.1f %.1f %.1f %.1f %.1f %.1f", ox, oy, ox, oy - q[2] * 6 * s, ox - q[1] * t, oy - q[2] * 6 * s, ox - q[1] * t, oy), C.text, 0)
    end

    if mp.get_property_native("paused-for-cache") then
        text(w / 2, h / 2 - 80 * s, "Buffering", 22 * s, C.text, 0x30, 5, true)
    end

    draw_queue(R, w)
    draw_gear(R, w)
    draw_menu(R, w)
    publish_regions(R)

    toast_layer(w, h)
    ov.data = table.concat(L, "\n")
    ov:update()
end

-- ---------- visibility and animation ----------

local timer
local drag_flush     -- set below: sends the newest seek-bar position once the player has finished the last seek

local function tick()
    if st.dragging and drag_flush then drag_flush() end
    if st.toast then dirty = true end
    local now = mp.get_time()
    local want = 0
    local over_ui = st.mx and (st.my or 0) > (st.regions.seek and st.regions.seek.y - 20 or 1e9)
    local keep = opt.autohide == "never" or (opt.autohide == "fullscreen" and not in_fullscreen)
    if st.hideui then want = 0
    elseif keep or dock_docked() or st.dragging or st.vdrag or st.menu or st.gopen or st.qopen or (not in_fullscreen and (paused() or eof())) or (now - st.last_move) < HIDE_AFTER or over_ui then want = 1 end   -- in fullscreen the bar stays away, even when paused, until the pointer moves
    st.target = want
    if st.thumb_on and (want == 0 and st.fade <= 0.05) then thumb_off() end
    local speed = 0.2
    local before = st.fade
    if st.fade < st.target then st.fade = math.min(st.target, st.fade + speed)
    elseif st.fade > st.target then st.fade = math.max(st.target, st.fade - speed * 0.6) end
    if dirty or st.fade ~= before then draw(); dirty = false end
end

-- Test seams for the drawing code, which headless runs never reach (they have no window size):
--   gear-open <page>  opens the player panel on a page (main, fx, hdr); queue-open opens the queue panel
--   draw-test WxH     draws once as if the window were that size and publishes "ok" or the Lua error
-- A fence for tests: messages are handled in order, so when this answer appears everything sent before it has been done.
actions["sync"] = function(arg) publish("sync", tostring(arg)) end
actions["click"] = function(name) if name and name ~= "" then pcall(function() activate_name(name) end) end end
actions["panel-open"] = function(tab) st.panel = { tab = tostring(tab), scroll = 0 }; st.menu, st.gopen, st.qopen = nil, false, false; publish("panel", st.panel.tab); dirty = true end
actions["gear-open"] = function(page)
    if is_dock() then st.panel = { tab = (page == "fx" and "audio") or (page == "hdr" and "video") or "playback", scroll = 0 }; st.menu, st.gopen, st.qopen = nil, false, false; publish("panel", st.panel.tab); publish("gear", "open"); dirty = true; return end
    st.gopen = true; st.qopen = false; st.menu = nil; st.gpage = (page == "fx" or page == "hdr") and page or "main"; st.gscroll = 0; publish("gear", "open"); dirty = true end
actions["queue-open"] = function()
    if is_dock() then st.panel = { tab = "queue", scroll = 0 }; st.menu, st.gopen, st.qopen = nil, false, false; publish("panel", "queue"); dirty = true; return end
    if #st.queue.items > 0 then st.qopen = true; st.gopen = false; st.menu = nil; dirty = true end end
actions["draw-test"] = function(arg)
    local w, h = tostring(arg):match("^(%d+)x(%d+)$")
    if not w then return end
    st.force = { tonumber(w), tonumber(h) }
    st.fade = 1
    local ok, err = pcall(draw)
    st.force = nil
    publish("draw_result", ok and "ok" or ("error: " .. (tostring(err):gsub("^.*lumina%-osc%.lua:", "line "))))
end

timer = mp.add_periodic_timer(1 / 30, tick)

for _, prop in ipairs({ "pause", "eof-reached", "speed", "sid", "aid", "paused-for-cache", "osd-dimensions", "time-pos", "duration", "volume", "mute", "volume-max" }) do
    mp.observe_property(prop, "native", function() dirty = true end)
end

-- ---------- input ----------

local function hit(x, y)
    if st.panel then
        for _, q in ipairs(st.grects or {}) do
            if in_rect(q, x, y) then return q.name end
        end
        if in_rect(st.regions.qpanel, x, y) then return "qpanel" end
    end
    if st.gopen then
        for _, q in ipairs(st.grects or {}) do
            if in_rect(q, x, y) then return q.name end
        end
        if in_rect(st.regions.qpanel, x, y) then return "qpanel" end
    end
    if st.qopen then
        for _, q in ipairs(st.qrects) do
            if in_rect(q, x, y) then return q.name end
        end
        if in_rect(st.regions.qpanel, x, y) then return "qpanel" end
    end
    if st.menu then
        for i, r in ipairs(st.menu.rects or {}) do
            if in_rect(r, x, y) then return "menu:" .. i end
        end
    end
    for _, name in ipairs({ "chap", "play", "back", "fwd", "prev", "next", "mute", "vol", "cc", "audio", "speed", "gear", "queue", "snap", "fs", "close", "seek", "pill", "bar" }) do
        if in_rect(st.regions[name], x, y) then return name end
    end
    return nil
end

local function vol_to(x)
    local v = st.regions.vol
    if not v then return end
    local vmax = mp.get_property_number("volume-max") or 100
    local nv = math.floor(clamp((x - v.x) / v.w, 0, 1) * vmax + 0.5)
    mp.set_property_number("volume", nv)
    mp.set_property("mute", nv > 0 and "no" or "yes")
end

local function seek_to(x, final)
    local sk = st.regions.seek
    if not sk then return end
    local frac = clamp((x - sk.x) / sk.w, 0, 1)
    st.drag_t = frac * (mp.get_property_number("duration") or 0)    -- the playhead and time follow the pointer at once, whatever the decoder is doing
    dirty = true
    st.pending = frac
    if final then st.pending = nil; mp.commandv("seek", string.format("%.3f", frac * 100), "absolute-percent+exact"); return end
    drag_flush()
end

-- While the button is held: jump to keyframes (an exact seek is restarted by the next one and a slow file never shows a picture), but only start the next
-- seek when the last one has landed, always towards the newest position. That keeps the picture moving as fast as the file allows without a backlog.
drag_flush = function()
    if not st.pending or mp.get_property_bool("seeking") then return end
    local frac = st.pending
    st.pending = nil
    mp.commandv("seek", string.format("%.3f", frac * 100), "absolute-percent+keyframes")
end

-- A preview picture follows the pointer over the seek bar. The host draws it; this only says what time and where.
local function thumb_at(t, x, y)
    st.thumb_on = true
    st.thumb_t = t
    publish("thumb_time", string.format("%.2f", t))
    mp.commandv("script-message", "lumina-thumb", string.format("%.2f", t), string.format("%d", x), string.format("%d", y))
end
local function thumb_off()
    if not st.thumb_on then return end
    st.thumb_on = false
    publish("thumb_time", "")
    mp.commandv("script-message", "lumina-thumb-hide")
end
local function update_thumb(x, y)
    local sk = st.regions.seek
    local dur = mp.get_property_number("duration") or 0
    if opt.thumbs == "yes" and sk and dur > 0 and st.fade > 0.05 and (st.dragging or in_rect(sk, x, y)) then
        thumb_at(clamp((x - sk.x) / sk.w, 0, 1) * dur, clamp(x, sk.x, sk.x + sk.w), sk.y)
    else
        thumb_off()
    end
end
actions["thumb-test"] = function(arg)     -- test seam: pretend the pointer is on the seek bar at this time (seconds), or off it
    local t = tonumber(arg)
    if t then thumb_at(t, 400, 300) else thumb_off() end
end

mp.add_forced_key_binding("mouse_move", "lumina_move", function()
    local x, y = mp.get_mouse_pos()
    st.mx, st.my = x, y
    st.last_move = mp.get_time()
    st.hover = hit(x, y)
    update_thumb(x, y)
    publish("hover", st.hover or "")
    dirty = true
    if st.dragging then seek_to(x) end
    if st.vdrag then vol_to(x) end
end)

-- The host sends the pointer position on every move while the left button is held (a drag on the seek bar or the volume).
mp.register_script_message("lumina-drag", function(xs, ys)
    local x, y = tonumber(xs), tonumber(ys)
    if not x then return end
    st.mx, st.my = x, y or st.my
    st.last_move = mp.get_time()
    if st.dragging then seek_to(x) end
    if st.vdrag then vol_to(x) end
    dirty = true
end)

local function toast(s) mp.osd_message(s, 1.4) end

local function dock_activate(name)
    local function toggle(tab) dock_open_panel(tab); return true end
    if name == "bar" then dirty = true; return true      -- the bar's own background: clicking it must not pause the video
    elseif name == "cc" then return toggle("subs")
    elseif name == "audio" then return toggle("audio")
    elseif name == "speed" then return toggle("playback")
    elseif name == "chap" then return toggle("chapters")
    elseif name == "queue" then return toggle("queue")
    elseif name == "gear" then
        local t = st.panel and st.panel.tab
        return toggle((t == "playback" or t == "video") and t or "playback")
    elseif name:sub(1, 5) == "ptab:" then
        st.panel = { tab = name:sub(6), scroll = 0 }; publish("panel", st.panel.tab); dirty = true; return true
    elseif name == "pr:add" then mp.commandv("script-message", "lumina-subs"); return true
    elseif name == "q_clear" then st.panel = nil; publish("panel", ""); mp.commandv("script-message", "lumina-queue-clear"); return true
    elseif name:sub(1, 3) == "pr:" then
        local k, which = name:match("^pr:(%d+):?(%a?)$")
        local row = (st.prows or {})[tonumber(k)]
        if not row then return true end
        if row.t == "val" then
            local arg = nil
            if row.step then arg = (which == "m") and tostring(-row.step) or tostring(row.step) end
            do_action(row.action, arg)
        elseif row.t == "queue" then mp.commandv("script-message", "lumina-queue-jump", tostring(row.index - 1))
        elseif row.run then row.run() end
        dirty = true
        return true
    end
    return false
end

local function activate(name)
    if is_dock() and dock_activate(name) then return end
    if name == "play" then
        if eof() then mp.commandv("seek", "0", "absolute") end
        mp.commandv("cycle", "pause")
    elseif name == "back" then mp.commandv("seek", tostring(-opt.skip))
    elseif name == "fwd" then mp.commandv("seek", tostring(opt.skip))
    elseif name == "cc" or name == "audio" or name == "speed" or name == "chap" then open_menu(name == "chap" and "chapters" or name)
    elseif name == "close" then mp.commandv("quit")
    elseif name == "pill" then dirty = true     -- the floating bar's own background: clicking it must not pause the video
    elseif name == "gear" then st.gopen = not st.gopen; st.qopen = false; st.menu = nil; dirty = true; publish("gear", st.gopen and "open" or "")
    elseif name == "g_reset" then do_action("reset-all")
    elseif name == "g_back" then st.gpage = "main"; st.gscroll = 0; dirty = true
    elseif name:sub(1, 2) == "g:" then
        local i, which = name:match("^g:(%d+):(%a)$")
        local row = cur_rows()[tonumber(i)]
        if row then
            local arg = nil
            if row[4] then arg = (which == "m") and tostring(-row[4]) or tostring(row[4]) end
            do_action(row[3], arg)
        end
    elseif name == "queue" then st.qopen = not st.qopen; st.gopen = false; dirty = true; publish("gear", "")
    elseif name == "q_shuffle" then mp.commandv("script-message", "lumina-queue-shuffle")
    elseif name == "q_repeat" then mp.commandv("script-message", "lumina-queue-repeat")
    elseif name == "q_save" then mp.commandv("script-message", "lumina-queue-save")
    elseif name == "q_clear" then st.qopen = false; mp.commandv("script-message", "lumina-queue-clear")
    elseif name:sub(1, 5) == "qrow:" then mp.commandv("script-message", "lumina-queue-jump", tostring(tonumber(name:sub(6)) - 1))
    elseif name:sub(1, 4) == "qup:" then mp.commandv("script-message", "lumina-queue-move", tostring(tonumber(name:sub(5)) - 1), "-1")
    elseif name:sub(1, 4) == "qdn:" then mp.commandv("script-message", "lumina-queue-move", tostring(tonumber(name:sub(5)) - 1), "1")
    elseif name:sub(1, 3) == "qx:" then mp.commandv("script-message", "lumina-queue-remove", tostring(tonumber(name:sub(4)) - 1))
    elseif name == "prev" then mp.commandv("script-message", "lumina-prev")
    elseif name == "next" then mp.commandv("script-message", "lumina-next")
    elseif name == "mute" then mp.commandv("cycle", "mute")
    elseif name == "snap" then
        mp.commandv("screenshot")
        toast("Snapshot saved")
    elseif name:sub(1, 5) == "menu:" then
        local it = st.menu and st.menu.items[tonumber(name:sub(6))]
        st.menu = nil
        publish("menu", "")
        if it then
            it.run()
            if it.reopen then open_menu(it.reopen) end
        end
    elseif name == "fs" then
        mp.commandv("script-message", "lumina-fullscreen")
    end
end

activate_name = activate

mp.add_forced_key_binding("mbtn_left", "lumina_click", function(e)
    local x, y = mp.get_mouse_pos()
    st.mx, st.my = x, y
    st.last_move = mp.get_time()
    if e.event == "down" then
        local name = hit(x, y)
        if st.menu and not (name and name:sub(1, 5) == "menu:") and name ~= (st.menu.name == "chapters" and "chap" or st.menu.name == "devices" and "audio" or st.menu.name) then st.menu = nil; publish("menu", ""); st.swallow = true; dirty = true; return end
        if st.panel and not name and not in_rect(st.regions.qpanel, x, y) then st.panel = nil; publish("panel", ""); publish("gear", ""); st.swallow = true; dirty = true; return end
        if st.qopen and not name and not in_rect(st.regions.qpanel, x, y) then st.qopen = false; st.swallow = true; dirty = true; return end
        if st.gopen and not name and not in_rect(st.regions.qpanel, x, y) then st.gopen = false; publish("gear", ""); st.swallow = true; dirty = true; return end
        st.down_on = name
        if name == "seek" then st.dragging = true; seek_to(x) end
        if name == "vol" then st.vdrag = true; vol_to(x) end
    elseif e.event == "up" then
        if st.swallow then st.swallow = false; return end
        local was = st.down_on
        st.down_on = nil
        if st.dragging then st.dragging = false; seek_to(x, true); st.drag_t = nil; st.pending = nil; return end
        if st.vdrag then st.vdrag = false; vol_to(x); return end
        if was and was == hit(x, y) then activate(was)
        elseif not was and not hit(x, y) and opt.click == "pause" then mp.commandv("cycle", "pause") end
    end
end, { complex = true })

mp.register_script_message("lumina-thumbs-state", function(v)
    opt.thumbs = (v == "yes") and "yes" or "no"
    if opt.thumbs ~= "yes" then thumb_off() end
    publish("thumbs", opt.thumbs)
    dirty = true
end)

mp.register_script_message("lumina-title", function(t, sub)
    st.title, st.subtitle = t, sub
    dirty = true
end)

mp.register_event("file-loaded", function()
    st.last_move = mp.get_time() -- show the controls briefly when a file opens
end)

mp.register_script_message("lumina-fx-state", function(json)
    local t = require("mp.utils").parse_json(json)
    if t then st.fx = t; dirty = true; publish("fx", string.format("%s|%s|%s|%d|%s|%s", t.eq, t.comp, t.space, t.width, tostring(t.norm), tostring(t.night))) end
end)

-- The host owns the bookmarks (they live in the database); it sends the list for the playing file whenever it changes.
mp.register_script_message("lumina-bookmarks", function(json)
    local t = require("mp.utils").parse_json(json)
    st.bookmarks = {}
    if type(t) == "table" then
        for _, b in ipairs(t) do
            if type(b) == "table" and tonumber(b.t) then st.bookmarks[#st.bookmarks + 1] = { t = tonumber(b.t), label = tostring(b.label or "Bookmark") } end
        end
    end
    table.sort(st.bookmarks, function(a, b) return a.t < b.t end)
    dirty = true
    publish("bookmark_count", tostring(#st.bookmarks))
end)

mp.register_script_message("lumina-mini-state", function(v)
    st.mini = (v == "yes")
    publish("mini", st.mini and "yes" or "no")
    dirty = true
end)

mp.register_script_message("lumina-record-state", function(v)
    st.recording = (v == "yes")
    publish("recording", st.recording and "yes" or "no")
    dirty = true
end)

mp.register_script_message("lumina-substyle-apply", function(d, sc, p)
    if tonumber(d) then mp.set_property_number("sub-delay", tonumber(d)) end
    if tonumber(sc) then mp.set_property_number("sub-scale", tonumber(sc)) end
    if tonumber(p) then mp.set_property_number("sub-pos", tonumber(p)) end
    publish("substyle", string.format("%.3f|%.3f|%d", num("sub-delay"), num("sub-scale", 1), num("sub-pos", 100)))
end)

mp.register_script_message("lumina-sleep-state", function(label)
    st.sleep_label = label
    publish("sleep_label", label)
    dirty = true
end)

mp.register_script_message("lumina-neighbors", function(p, n)
    st.has_prev, st.has_next = (p == "yes"), (n == "yes")
    dirty = true
end)

mp.register_script_message("lumina-fullscreen-state", function(v) in_fullscreen = (v == "yes"); dirty = true end)

-- Keys and wheel follow the Settings page.
-- Skipping is a message (not a fixed key) so the keys can be rebound in Settings and the skip amount still applies.
mp.register_script_message("lumina-skip", function(dir) mp.commandv("seek", tostring((tonumber(dir) or 1) * opt.skip)) end)

mp.add_forced_key_binding("mbtn_left_dbl", "lumina_dbl", function()
    if opt.dclick == "fullscreen" then mp.commandv("script-message", "lumina-fullscreen")
    elseif opt.dclick == "pause" then mp.commandv("cycle", "pause") end
end)

-- Volume up or down, with a short on-screen note. The text is built here: show-text did not expand ${volume} for us and printed it as it was.
local function change_volume(delta)
    mp.commandv("add", "volume", tostring(delta))
    show_toast(string.format("Volume %d%%", math.floor((mp.get_property_number("volume") or 0) + 0.5)), 0.9)
end
mp.register_script_message("lumina-volume", function(d) change_volume(tonumber(d) or 0) end)

-- v and b: next subtitle / audio track, and say which one it is now
mp.register_script_message("lumina-cycle", function(kind)
    local sub = kind == "sub"
    mp.commandv("cycle", sub and "sub" or "audio")
    mp.add_timeout(0.08, function()
        local sel = mp.get_property_native(sub and "sid" or "aid")
        local name
        if sel then
            for _, t in ipairs(mp.get_property_native("track-list") or {}) do
                if t.type == (sub and "sub" or "audio") and t.id == sel then
                    name = track_name(t); local det = track_detail(t); if det ~= "" and not sub then name = name .. "  " .. det end
                end
            end
        end
        show_toast((sub and "Subtitles: " or "Audio: ") .. (name or "Off"), 2.2)
    end)
end)

-- ] and [: speed up or down in plain steps of 0.1
mp.register_script_message("lumina-speed", function(d)
    local v = clamp(math.floor(((mp.get_property_number("speed") or 1) + (tonumber(d) or 0)) * 100 + 0.5) / 100, 0.1, 4)
    mp.set_property_number("speed", v)
    show_toast(string.format("Speed %gx", v), 1.4)
end)

local function wheel_at(sign, mx, my)
    do
        if st.panel and in_rect(st.regions.qpanel, mx, my) then st.panel.scroll = (st.panel.scroll or 0) - sign * 76 * (st.regions.d or 1); dirty = true; return end
        if st.menu then st.menu.scroll = (st.menu.scroll or 0) - sign * 2; dirty = true; return end
        if st.qopen and in_rect(st.regions.qpanel, mx, my) then st.qscroll = st.qscroll - sign * 2; dirty = true; return end
        if st.gopen and in_rect(st.regions.qpanel, mx, my) then st.gscroll = st.gscroll - sign * 2; dirty = true; return end
        -- over the seek bar the wheel scrubs: up goes forward, down goes back
        if st.regions and in_rect(st.regions.seek, mx, my) then
            mp.commandv("seek", tostring(sign * 5), "relative+exact")
            return
        end
        if opt.wheel == "volume" then
            change_volume(sign * 5)
        elseif opt.wheel == "seek" then mp.commandv("seek", tostring(sign * opt.skip)) end
    end
end

local function wheel(sign)
    return function()
        local mx, my = mp.get_mouse_pos()
        wheel_at(sign, mx, my)
    end
end
actions["wheel-at"] = function(arg)       -- a test seam: turn the wheel with the pointer at x,y (sign is 1 or -1)
    local x, y, s = tostring(arg):match("^(%-?%d+),(%-?%d+),(%-?%d+)$")
    if x then wheel_at(tonumber(s), tonumber(x), tonumber(y)) end
end
mp.add_forced_key_binding("wheel_up", "lumina_wheel_up", wheel(1), { repeatable = true })
mp.add_forced_key_binding("wheel_down", "lumina_wheel_down", wheel(-1), { repeatable = true })

publish("ready", "yes")
publish_accent()
mp.register_script_message("lumina-accent", function(h)
    opt.accent = (tostring(h or "5CE1C6"):gsub("^#", ""))
    publish_accent(); dirty = true
end)
publish("menu", "")

mp.register_script_message("lumina-queue", function(json)
    local t = require("mp.utils").parse_json(json)
    if not t then return end
    st.queue = { cur = t.cur or -1, shuffle = t.shuffle == true, repeat_mode = t["repeat"] or "off", items = t.items or {} }
    if #st.queue.items == 0 then st.qopen = false end
    dirty = true
    -- count goes last: it is the value tests and hosts wait on, so everything else is already in place when it appears
    publish("queue_cur", tostring(st.queue.cur))
    publish("queue_repeat", st.queue.repeat_mode)
    publish("queue_shuffle", tostring(st.queue.shuffle))
    publish("queue_count", tostring(#st.queue.items))
    publish("queue", string.format('{"count":%d,"cur":%d,"shuffle":%s,"repeat":"%s"}', #st.queue.items, st.queue.cur, tostring(st.queue.shuffle), st.queue.repeat_mode))
end)

-- The host decides what follows the last frame (queue, next episode, nothing).
mp.observe_property("eof-reached", "bool", function(_, v)
    if v == true then mp.commandv("script-message", "lumina-eof") end
end)

mp.register_event("start-file", function() st.aspect_i, st.crop_i = 1, 1; st.gpage = "main" end)
publish("gear", "")

mp.register_script_message("lumina-open-menu", function(name) open_menu(name) end)
