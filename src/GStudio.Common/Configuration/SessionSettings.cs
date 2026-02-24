using GStudio.Common.Geometry;

namespace GStudio.Common.Configuration;

public enum CaptureSourceKind
{
    Display,
    Window,
    Region
}

public enum MotionPreset
{
    Slow,
    Mellow,
    Quick,
    Rapid
}

public sealed record CaptureRegion(int X, int Y, int Width, int Height)
{
    public RectD ToRect() => new(X, Y, Width, Height);
}

public sealed record VideoCaptureSettings(
    CaptureSourceKind Source,
    int Width,
    int Height,
    int Fps,
    bool CaptureFrames,
    CaptureRegion? Region = null);

public sealed record AudioCaptureSettings(
    bool CaptureMicrophone,
    bool CaptureSystemAudio,
    int SampleRate,
    int Channels);

public sealed record CameraFollowSettings(
    double FocusAreaRatio,
    double PreRollSeconds,
    double HoldSeconds,
    double MinScale,
    double MaxScale,
    MotionPreset Preset);

public sealed record CursorPolishSettings(
    bool SmoothEnabled,
    bool RemoveShakes,
    double ShakeThresholdPixels,
    bool HideIdle,
    double IdleSeconds,
    double OneEuroMinCutoff,
    double OneEuroBeta,
    double OneEuroDerivativeCutoff);

public sealed record MotionBlurSettings(
    double ScreenBlurStrength,
    double CursorBlurStrength);

public sealed record SessionSettings(
    VideoCaptureSettings Video,
    AudioCaptureSettings Audio,
    CameraFollowSettings Camera,
    CursorPolishSettings Cursor,
    MotionBlurSettings MotionBlur)
{
    public static SessionSettings CreateDefault()
    {
        return new SessionSettings(
            Video: new VideoCaptureSettings(
                Source: CaptureSourceKind.Display,
                Width: 2560,
                Height: 1440,
                Fps: 30,
                CaptureFrames: true),
            Audio: new AudioCaptureSettings(
                CaptureMicrophone: false,
                CaptureSystemAudio: false,
                SampleRate: 48000,
                Channels: 2),
            Camera: new CameraFollowSettings(
                FocusAreaRatio: 0.35d,
                PreRollSeconds: 0.15d,
                HoldSeconds: 0.6d,
                MinScale: 1.0d,
                MaxScale: 3.2d,
                Preset: MotionPreset.Quick),
            Cursor: new CursorPolishSettings(
                SmoothEnabled: true,
                RemoveShakes: true,
                ShakeThresholdPixels: 1.5d,
                HideIdle: true,
                IdleSeconds: 1.3d,
                OneEuroMinCutoff: 1.2d,
                OneEuroBeta: 0.09d,
                OneEuroDerivativeCutoff: 1.0d),
            MotionBlur: new MotionBlurSettings(
                ScreenBlurStrength: 0.25d,
                CursorBlurStrength: 0.35d));
    }
}
