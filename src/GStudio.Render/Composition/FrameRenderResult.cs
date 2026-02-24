namespace GStudio.Render.Composition;

public sealed record FrameRenderResult(
    string FramesDirectory,
    int FrameCount,
    int Width,
    int Height);
