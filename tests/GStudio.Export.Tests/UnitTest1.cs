using System.Drawing;
using System.Drawing.Imaging;
using GStudio.Cinematic.Engine;
using GStudio.Common.Configuration;
using GStudio.Common.Geometry;
using GStudio.Export.Pipeline;
using GStudio.Project.Store;
using GStudio.Render.Composition;
using GStudio.Render.Preview;

namespace GStudio.Export.Tests;

public sealed class ExportPipelineTests
{
    [Fact]
    public async Task ExportPackageWriter_RendersFramesAndWritesEncodeScript()
    {
        var root = Path.Combine(Path.GetTempPath(), "GStudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var settings = SessionSettings.CreateDefault() with
            {
                Video = SessionSettings.CreateDefault().Video with
                {
                    Width = 320,
                    Height = 180,
                    Fps = 30,
                    CaptureFrames = true
                }
            };

            var store = new ProjectSessionStore(Path.Combine(root, "projects"));
            var session = await store.CreateSessionAsync(settings);

            CreateTestFrame(session.Paths.CaptureFramesDirectory, 0, Color.DarkRed, Color.White);
            CreateTestFrame(session.Paths.CaptureFramesDirectory, 1, Color.DarkBlue, Color.White);
            WriteFakeWave(session.Paths.MicrophoneAudioPath);
            WriteFakeWave(session.Paths.SystemAudioPath);

            var plan = new PreviewRenderPlan(
                Frames:
                [
                    new PreviewFrame(
                        FrameIndex: 0,
                        Time: 0.0d,
                        Camera: new CameraTransform(0.0d, new ScreenPoint(160.0d, 90.0d), 1.0d),
                        Cursor: new CursorSample(0.0d, new ScreenPoint(160.0d, 90.0d), false)),
                    new PreviewFrame(
                        FrameIndex: 1,
                        Time: 1.0d / 30.0d,
                        Camera: new CameraTransform(1.0d / 30.0d, new ScreenPoint(120.0d, 70.0d), 1.6d),
                        Cursor: new CursorSample(1.0d / 30.0d, new ScreenPoint(120.0d, 70.0d), false))
                ],
                ZoomSegments: Array.Empty<ZoomSegment>(),
                DurationSeconds: 1.0d / 30.0d,
                Fps: 30);

            var fakeEncoder = new FakeVideoEncoder();
            var writer = new ExportPackageWriter(videoEncoder: fakeEncoder);
            var result = await writer.WriteAsync(new ExportRequest(
                Session: session,
                PreviewPlan: plan,
                OutputDirectory: Path.Combine(root, "exports"),
                OutputName: "demo",
                EncodeVideo: true));

            Assert.True(File.Exists(result.PlanFilePath));
            Assert.True(File.Exists(result.EncodeScriptPath));
            Assert.True(Directory.Exists(result.RenderedFramesDirectory));
            Assert.Equal(2, result.RenderedFrameCount);
            Assert.True(result.VideoEncoded);
            Assert.True(File.Exists(result.OutputMp4Path));
            Assert.Single(fakeEncoder.Calls);
            Assert.NotNull(fakeEncoder.Calls[0].MicrophoneAudioPath);
            Assert.NotNull(fakeEncoder.Calls[0].SystemAudioPath);

            var renderedFrames = Directory.GetFiles(result.RenderedFramesDirectory, "frame_*.png");
            Assert.Equal(2, renderedFrames.Length);

            var script = await File.ReadAllTextAsync(result.EncodeScriptPath);
            Assert.Contains("rendered_frames", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ffmpeg -y -framerate 30", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("amix=inputs=2", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("aresample=async=1:first_pts=0", script, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AdaptiveVideoEncoder_FallsBackWhenNativeEncoderFails()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "GStudioTests", Guid.NewGuid().ToString("N"), "out.mp4");
        var fallback = new FakeVideoEncoder();
        var adaptive = new AdaptiveVideoEncoder(
            mediaFoundationEncoder: new ThrowingVideoEncoder(),
            ffmpegEncoder: fallback);

        await adaptive.EncodeAsync(new VideoEncodeRequest(
            FrameInputPattern: "C:\\tmp\\frame_%06d.png",
            OutputMp4Path: outputPath,
            Fps: 30,
            FrameCount: 30,
            TargetDurationSeconds: 1.0d,
            PreferMediaFoundation: true,
            AllowFfmpegFallback: true));

        Assert.Single(fallback.Calls);
        Assert.True(File.Exists(outputPath));
        Directory.Delete(Path.GetDirectoryName(outputPath)!, recursive: true);
    }

    [Fact]
    public async Task CinematicFrameRenderer_DrawsCursorWhenVisible()
    {
        var root = Path.Combine(Path.GetTempPath(), "GStudioTests", Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "source");
        var outputDir = Path.Combine(root, "output");
        Directory.CreateDirectory(sourceDir);

        try
        {
            using (var bitmap = new Bitmap(320, 180, PixelFormat.Format32bppArgb))
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.Black);
                bitmap.Save(Path.Combine(sourceDir, "frame_000000.png"), ImageFormat.Png);
            }

            var plan = new PreviewRenderPlan(
                Frames:
                [
                    new PreviewFrame(
                        FrameIndex: 0,
                        Time: 0.0d,
                        Camera: new CameraTransform(0.0d, new ScreenPoint(160.0d, 90.0d), 1.0d),
                        Cursor: new CursorSample(0.0d, new ScreenPoint(160.0d, 90.0d), false))
                ],
                ZoomSegments: Array.Empty<ZoomSegment>(),
                DurationSeconds: 0.033d,
                Fps: 30);

            var renderer = new CinematicFrameRenderer();
            await renderer.RenderAsync(plan, sourceDir, outputDir, 320, 180, 0.0d);

            using var rendered = new Bitmap(Path.Combine(outputDir, "frame_000000.png"));
            var foundCursorColor = false;

            for (var y = 84; y <= 96 && !foundCursorColor; y++)
            {
                for (var x = 154; x <= 166 && !foundCursorColor; x++)
                {
                    var pixel = rendered.GetPixel(x, y);
                    if (pixel.G > 100 && pixel.B > 90 && pixel.R < 90)
                    {
                        foundCursorColor = true;
                    }
                }
            }

            Assert.True(foundCursorColor);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CreateTestFrame(string frameDirectory, int index, Color background, Color inset)
    {
        Directory.CreateDirectory(frameDirectory);
        using var bitmap = new Bitmap(320, 180, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(background);

        using var brush = new SolidBrush(inset);
        graphics.FillEllipse(brush, 100, 50, 120, 80);

        var path = Path.Combine(frameDirectory, $"frame_{index:D06}.png");
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void WriteFakeWave(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllBytes(path,
        [
            0x52, 0x49, 0x46, 0x46,
            0x24, 0x00, 0x00, 0x00,
            0x57, 0x41, 0x56, 0x45,
            0x66, 0x6D, 0x74, 0x20,
            0x10, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00,
            0x44, 0xAC, 0x00, 0x00,
            0x88, 0x58, 0x01, 0x00,
            0x02, 0x00, 0x10, 0x00,
            0x64, 0x61, 0x74, 0x61,
            0x00, 0x00, 0x00, 0x00
        ]);
    }

    private sealed class FakeVideoEncoder : IVideoEncoder
    {
        public List<VideoEncodeRequest> Calls { get; } = [];

        public Task EncodeAsync(VideoEncodeRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputMp4Path) ?? ".");
            return File.WriteAllBytesAsync(request.OutputMp4Path, [0x00, 0x00, 0x00, 0x18], cancellationToken);
        }
    }

    private sealed class ThrowingVideoEncoder : IVideoEncoder
    {
        public Task EncodeAsync(VideoEncodeRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated native encoder failure.");
        }
    }
}
