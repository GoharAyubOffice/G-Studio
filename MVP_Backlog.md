Milestone 0 — Foundations (1–2 weeks)

Repo, CI, crash reporting

Project store format + NDJSON event writer

Minimal WinUI shell: record button → stop → opens editor

Milestone 1 — Recorder (2–4 weeks)

WGC capture display/window

WASAPI mic + loopback

Hotkeys: start/stop/pause

Write raw capture + timestamps

Log pointer moves/clicks + keyboard shortcuts

Exit criteria: can record 60fps 1440p with synced audio.

Milestone 2 — Cinematic playback (3–5 weeks)

Build camera engine:

generate auto-zoom segments from clicks

spring-based motion with presets (Slow→Rapid)

Cursor smoothing + remove shakes + hide idle

Motion blur (screen + cursor)

Preview renderer (GPU) with background padding

Exit criteria: one-take demo looks “cinematic” without editing.

Milestone 3 — Editor essentials (3–6 weeks)

Timeline UI

Trimming/cut tool (scissors)

Zoom timeline: add/remove/disable, adjust duration, auto vs manual zoom

Per-fragment overrides:

disable smooth mouse movement

hide cursor in fragment

Masks/highlights tool

Exit criteria: user can fix 90% of recordings in <2 minutes.

Milestone 4 — “Creator features” (2–4 weeks)

Shortcut overlay system + timeline disable + sizing

Captions generation (Whisper local) + edit transcript + export SRT

Background presets (color/gradient/image)

Milestone 5 — Export (2–4 weeks)

Export settings UI (resolution, fps, codec, compression)

MF H.264 export (baseline)

HEVC export (if system supports)

Optional ProRes/“mezzanine” export via FFmpeg (clearly labeled / licensing)

Deterministic render-to-encode pipeline + progress + cancel

Exit criteria: exported file matches preview (no desync, stable quality).

Milestone 6 — Polish (ongoing)

Presets save/apply/share (optional v1.1)

Crash recovery, autosave

Quick share (upload/link) (optional)