using System.Drawing;
using System.Drawing.Imaging;
using GStudio.Cinematic.Engine;
using GStudio.Common.Configuration;
using GStudio.Common.Geometry;
using GStudio.Export.Pipeline;
using GStudio.Project.Store;
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

            var writer = new ExportPackageWriter();
            var result = await writer.WriteAsync(new ExportRequest(
                Session: session,
                PreviewPlan: plan,
                OutputDirectory: Path.Combine(root, "exports"),
                OutputName: "demo"));

            Assert.True(File.Exists(result.PlanFilePath));
            Assert.True(File.Exists(result.EncodeScriptPath));
            Assert.True(Directory.Exists(result.RenderedFramesDirectory));
            Assert.Equal(2, result.RenderedFrameCount);

            var renderedFrames = Directory.GetFiles(result.RenderedFramesDirectory, "frame_*.png");
            Assert.Equal(2, renderedFrames.Length);

            var script = await File.ReadAllTextAsync(result.EncodeScriptPath);
            Assert.Contains("rendered_frames", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ffmpeg -y -framerate 30", script, StringComparison.OrdinalIgnoreCase);
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
}
