# G-Studio V1 implementation plan

## V1 objective
Ship a Windows-first MVP that records desktop interaction data and produces cinematic camera-follow motion with smooth cursor behavior and deterministic preview planning.

## Chosen stack
- Host app: WPF on .NET 9
- Capture runtime: D3D11 desktop duplication backend with GDI fallback + Win32 input polling
- Project storage: session folder with NDJSON event streams and JSON manifest
- Cinematic engine: click-driven auto-zoom + spring camera + cursor smoothing (One Euro)
- Export runtime: deterministic render plan + one-click FFmpeg encode with optional mic/system audio mix
  
## Pipeline status update
- Recorder now attempts GPU capture via DXGI duplication and falls back to GDI when unavailable.
- Export now renders cinematic frames from camera transforms before encoding.
- App export now performs one-click MP4 generation by invoking FFmpeg directly.
- Export automatically mixes microphone/system WAV tracks into MP4 when both tracks exist.
- Export applies baseline audio resample sync (`aresample=async=1:first_pts=0`) during encode.
- Rendered export frames now composite cursor sprite from cinematic cursor samples.
- Export package includes:
  - `render_plan.json`
  - `rendered_frames/frame_*.png` (camera-follow output)
  - `encode_with_ffmpeg.cmd` targeting rendered frames

## Implemented in this iteration
1. Solution scaffold with modular projects (`App`, `Common`, `Project`, `Capture`, `Cinematic`, `Render`, `Export`).
2. Session settings/contracts for video/audio/camera/cursor/blur and event schemas.
3. Crash-safe project store with NDJSON readers/writers and session manifest lifecycle.
4. Recorder coordinator and desktop snapshot capture backend:
   - frame sequence writing (`capture/frames/frame_000001.png`)
   - pointer move/down/up event logging
   - keyboard shortcut sampling
   - optional WASAPI mic + system loopback audio capture to WAV (`capture/audio`)
5. Cinematic pipeline:
   - auto-zoom segment generation from click events
   - overlap merge logic
   - spring camera solver with motion presets
   - cursor smoothing + shake removal + idle hide sampling
6. Deterministic preview plan generation at output FPS.
7. Export pipeline that renders camera-follow frames and emits `render_plan.json` + `encode_with_ffmpeg.cmd`.
8. WPF control panel wiring end-to-end: record -> stop -> preview -> export package.
9. Unit tests for cinematic engine behavior.

## Immediate next milestones
1. Replace GDI capture with WGC + D3D11 for performance and quality.
2. Add WASAPI mic + loopback with shared clock and drift handling.
3. Implement native Media Foundation H.264 export/mux path.
4. Add proxy generation and GPU preview compositor.
5. Add cursor sprite compositing and final audio mix in export path.

## Remaining focus
1. Replace duplication backend with Windows Graphics Capture interop path.
2. Add full timeline-based audio alignment/drift correction before final mux.
3. Replace FFmpeg runtime dependency with native Media Foundation mux export.
