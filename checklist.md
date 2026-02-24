# G-Studio V1 implementation checklist

## Foundation
- [x] Create solution and project layout (`App`, `Common`, `Project`, `Capture`, `Cinematic`, `Render`, `Export`)
- [x] Define shared contracts (settings, events, geometry, timeline)
- [x] Implement crash-safe project/session folder store and NDJSON event IO

## Recorder
- [x] Add recorder coordinator lifecycle (`Start`, `Stop`, `stats`, session manifest updates)
- [x] Implement desktop snapshot capture loop (frame sequence + pointer/click events)
- [x] Add keyboard shortcut event sampling for overlay timeline data

## Cinematic engine
- [x] Implement click-driven auto-zoom segment generation + overlap merge
- [x] Implement spring camera solver with motion presets
- [x] Implement cursor smoothing (One Euro) + shake reduction + per-fragment bypass ranges

## Render/Export
- [x] Implement deterministic preview plan generation at output FPS
- [x] Implement cinematic frame renderer (camera transform + basic screen motion blur)
- [x] Implement export package writer (render plan + rendered frames + encode script)
- [x] Implement one-click app export that runs FFmpeg encode automatically

## App integration
- [x] Build WPF control panel for recording and cinematic preview inspection
- [x] Wire recorder + project store + cinematic plan generation end-to-end
- [x] Validate with build/tests and update checklist status

## Next priorities
- [x] Add D3D11 desktop duplication capture backend with GDI fallback
- [ ] Upgrade capture backend from duplication API to Windows Graphics Capture interop path
- [x] Add WASAPI mic + loopback capture to WAV tracks
- [x] Mix captured audio tracks into MP4 export when available
- [x] Add baseline audio resample sync correction in FFmpeg export path
- [ ] Refine full audio clock alignment/drift correction across capture and export timeline
- [ ] Implement native Media Foundation H.264 mux export path
- [x] Add cursor sprite compositing in rendered export frames
