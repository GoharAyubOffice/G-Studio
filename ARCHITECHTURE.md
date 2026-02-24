1) Recommended Windows tech stack (fast + shippable)

Core (capture/render/export):

Language: C++ (or Rust)

Screen capture: Windows Graphics Capture (WGC) + Direct3D11

Audio capture: WASAPI (mic + loopback)

Real-time preview composition: Direct3D11 + Direct2D/DirectWrite (text overlays)

Export/encode:

Primary: Windows Media Foundation (MF) H.264/HEVC

Optional “Pro” encodes: integrate FFmpeg behind a “requires separate download / patent notice” gate (or license a commercial encoder). (Be careful: H.264/HEVC patent licensing can apply depending on distribution/usage.)

App shell/UI:

UI framework: WinUI 3 (C# or C++/WinRT)
(Alternative: Qt if you want cross-platform later, but WinUI 3 will feel native.)

Timeline UI: custom control (virtualized), GPU-accelerated thumbnails

ML (captions):

Whisper local inference via:

whisper.cpp (CPU) or

ONNX Runtime with Whisper ONNX models (GPU optional)
This matches Screen Studio’s “local Whisper” approach in principle.

2) High-level architecture

Modules:

Capture Service

Event Logger (cursor/keyboard)

Project Store (disk format)

Editing Engine (non-linear edits)

Cinematic Camera Engine (auto-zoom + motion)

Render Engine (compose frames)

Export Pipeline (encode mux)

UI Shell (recording + editor)

3) Data model (don’t let the LLM hallucinate—define it)

Project folder structure

/ProjectName.sswin/
  project.json
  capture/
    video_raw.mkv   (or segmented .mp4 chunks)
    audio_mic.wav
    audio_sys.wav
    thumbnails/...
  events/
    pointer.ndjson
    keyboard.ndjson
    windows.ndjson   (active window, bounds, DPI changes)
  edits/
    timeline.json
    zooms.json
    masks.json
    highlights.json
    captions.srt
    shortcuts.json
  cache/
    proxies/...
    render_cache/...

Core event schemas (NDJSON for streaming write + crash resilience)

pointer.ndjson

{"t":12.345,"type":"move","x":1032,"y":588}
{"t":12.410,"type":"down","btn":"left","x":1034,"y":590}
{"t":12.460,"type":"up","btn":"left","x":1034,"y":590}
{"t":12.700,"type":"wheel","dx":0,"dy":-120,"x":1200,"y":660}
{"t":13.010,"type":"cursorShape","shape":"ibeam"} 

keyboard.ndjson

{"t":15.220,"type":"shortcut","mods":["CTRL","SHIFT"],"key":"P"}
{"t":15.400,"type":"key","key":"A"} 

timeline.json (authoritative edit graph)

{
  "frameRate":60,
  "segments":[
    {"id":"s1","srcIn":0.0,"srcOut":32.4,"speed":1.0},
    {"id":"s2","srcIn":40.0,"srcOut":55.0,"speed":1.25}
  ],
  "cuts":[{"at":32.4},{"at":40.0}],
  "overrides":{
    "cursor":{"s2":{"smooth":false,"hide":true}}
  }
}
4) Auto-zoom + camera-follow algorithm (implementable, not proprietary)

Goal: produce a smooth “virtual camera” transform over the captured frame.

Inputs

Click events (primary triggers)

Cursor path (secondary focus)

Window bounds & DPI scaling

User settings (zoom level, animation style, spring params)

Outputs

A time-indexed camera transform:

C(t) = {centerX, centerY, scale, rotation(0), easingParams}

Auto-zoom segment generation

For each click at time tc:

define a target rect around click:

base size = min(viewW, viewH) * 0.35 (tunable)

clamp within screen bounds

create segment [tc - preRoll, tc + hold]

preRoll default: 0.15s

hold default: 0.6s

Merge segments if overlapping.

Camera motion

Use a damped spring for center and scale:

animation style presets map to spring constants (Slow→lower tension, Rapid→higher tension)

expose tension/friction/mass customization

Cursor smoothing

Resample pointer path to render FPS

Apply smoothing filter (e.g., OneEuroFilter) + optional “remove shakes” thresholding

If per-fragment “disable smoothing” is set, bypass filter in that fragment

Motion blur

Screen motion blur: based on delta of camera transform per frame

Cursor motion blur: based on delta of cursor position per frame

5) Rendering pipeline

Preview path (fast)

Decode proxies (downscaled)

GPU composite:

background layer (color/gradient/image)

transformed screen frame (camera transform)

cursor sprite (smoothed)

click effects (optional)

shortcut overlays

captions

masks/highlights

Export path (quality)

Decode source at full res

Re-run composition deterministically at output FPS

Encode + mux audio tracks (mix to stereo with gain controls)

6) Performance strategy (mandatory for 4K)

Always write events as NDJSON streaming (no giant JSON in memory)

Create proxy video after recording for smooth editing

Thumbnail generation async (but bounded CPU)

Export is a separate process (crash isolation)

7) Windows-specific gotchas

Multi-monitor HDR/DPI changes → store per-frame transform metadata (window bounds, DPI, color space)

Cursor shape capture: you’ll need Win32 cursor APIs + mapping to stable sprite set

Audio drift: resample to common clock, or use MF’s time-stamps carefully