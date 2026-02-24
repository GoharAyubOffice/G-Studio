1) Product summary

A Windows desktop app that records screen + audio, then automatically produces a polished “camera-follow” video by:

auto-zooming to click locations (and optionally cursor focus),

smoothing cursor motion and optionally adding motion blur,

adding shortcut overlays, captions, backgrounds, masks/highlights,

exporting to share-ready codecs.

(These are the core behaviors Screen Studio advertises/documented: auto zoom to cursor/clicks, cursor smoothing, motion blur, editable zoom timeline, captions via Whisper, shortcut display, masks/highlights, background styling.)

2) Target users

SaaS founders / PMs recording product demos

educators/tutorial creators

indie devs (Unity/Unreal) recording walkthroughs

marketing teams producing short social clips

3) Primary workflows

Record

Select: display/window/region

Toggle: mic, system audio, webcam (optional for v1.1), cursor effects

Hotkeys: start/stop/pause marker

Auto-polish

Generate zooms based on clicks (default on)

Smooth cursor path + optional motion blur

Edit

Timeline trim/cut fragments

Adjust/remove/add zoom segments (auto/manual)

Cursor visibility tweaks (hide idle / hide per fragment / disable smoothing per fragment)

Add masks/highlights for privacy/emphasis

Add backgrounds (padding, color/gradient/image)

Add shortcut overlays

Generate/edit captions (local)

Export

Choose resolution / FPS / codec / compression profile (H.264, HEVC; ProRes optional)

4) Feature requirements (v1 scope)
A) Recording

Capture modes: entire display, window, region

FPS: 30/60

Audio: microphone (WASAPI), system audio loopback

Cursor tracking: record pointer position, click events, cursor shape changes, wheel scroll, drag start/stop

Keyboard: record shortcut chords (Ctrl/Alt/Shift/Win + key) for overlay timeline

B) Auto-zoom engine (signature)

Default: create zoom segments at click timestamps

Auto-zoom target: click coordinate → compute focus rect around it

Manual zoom: allow user to define a rect for a segment

Zoom timeline: each zoom is a segment with start/end + type + scale

C) Cursor polish

Smooth cursor animation toggle

Cursor animation speed presets: Slow/Mellow/Quick/Rapid

Advanced cursor controls:

always default cursor

hide cursor when idle

remove cursor shakes

optimize cursor type switching

rotate cursor while moving

stop cursor movement at end

loop cursor position (loopable videos)

Per-fragment overrides:

disable smooth mouse movement (for dropdown accuracy)

hide cursor in specific sections

D) Motion/animation system

Motion blur slider (global), plus advanced per-component blur tuning (cursor movement / screen zoom / screen moving)

“Physics” customization: spring parameters (tension/friction/mass) for camera and cursor interpolation

E) Editing tools

Trim & cut workflow with scissors tool and timeline zooming

Mask & highlight tool:

add rectangular/rounded rect overlays, opacity control

mask does not track scrolling (static in frame)

F) Look & feel

Background system (color/gradient/image), padding around captured frame

Rounded corners, drop shadow (GPU)

Optional click sound (if you include, keep toggleable; Screen Studio has related doc link in nav)

G) Captions

Local caption generation using Whisper models Base/Small/Medium, language selection, optional prompt, transcript editor, export transcript file

H) Shortcuts overlay

Detect shortcuts automatically, option to show all / hide specific, label size slider, ignore “typing bursts”, timeline to disable items

I) Presets (v1.1 if time)

Save/apply/share presets for “look” settings (background, cursor, blur, zoom styles)

5) Non-functional requirements

Smooth preview playback for 4K recordings on midrange machines (use proxy pipeline)

Export should be deterministic and stable (no desync)

Project files are portable and recoverable (crash-safe)

Privacy: captions generated locally by default

6) Acceptance criteria (examples)

A click generates an auto-zoom segment and plays with smooth camera motion

Disabling smooth mouse movement on a fragment preserves exact cursor path in that fragment

User can hide cursor on a fragment and it’s absent in export

Whisper captions can be generated offline and edited