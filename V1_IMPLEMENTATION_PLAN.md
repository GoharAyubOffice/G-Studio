# G-Studio V1 implementation plan

## V1 objective
Ship a Windows-first MVP that records desktop interaction data and produces cinematic camera-follow motion with smooth cursor behavior and deterministic preview planning.

## Chosen stack
- Host app: WPF on .NET 9
- Capture prototype: desktop snapshot capture (GDI) + Win32 input polling
- Project storage: session folder with NDJSON event streams and JSON manifest
- Cinematic engine: click-driven auto-zoom + spring camera + cursor smoothing (One Euro)
- Export prototype: deterministic render plan + FFmpeg command script scaffold
  
## Pipeline status update
- Export now renders cinematic frames from camera transforms before encoding.
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
