using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GStudio.Common.Geometry;
using GStudio.Render.Preview;

namespace GStudio.Render.Composition;

public sealed class CinematicFrameRenderer
{
    public async Task<FrameRenderResult> RenderAsync(
        PreviewRenderPlan plan,
        string sourceFramesDirectory,
        string outputFramesDirectory,
        int designWidth,
        int designHeight,
        double motionBlurStrength,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceFramesDirectory))
        {
            throw new DirectoryNotFoundException($"Source frame directory not found: {sourceFramesDirectory}");
        }

        var sourceFrames = Directory
            .GetFiles(sourceFramesDirectory, "frame_*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sourceFrames.Length == 0)
        {
            throw new InvalidOperationException($"No source frames found in {sourceFramesDirectory}");
        }

        if (plan.Frames.Count == 0)
        {
            throw new InvalidOperationException("Preview plan contains no frames.");
        }

        Directory.CreateDirectory(outputFramesDirectory);

        using var firstSource = new Bitmap(sourceFrames[0]);
        var outputWidth = firstSource.Width;
        var outputHeight = firstSource.Height;

        var safeDesignWidth = Math.Max(1, designWidth);
        var safeDesignHeight = Math.Max(1, designHeight);

        string? previousRenderedFramePath = null;
        ScreenPoint? previousCenter = null;

        for (var frameIndex = 0; frameIndex < plan.Frames.Count; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = sourceFrames[Math.Min(frameIndex, sourceFrames.Length - 1)];
            var outputPath = Path.Combine(outputFramesDirectory, $"frame_{frameIndex:D06}.png");

            using var sourceBitmap = new Bitmap(sourcePath);
            using var renderedBitmap = RenderFrame(
                sourceBitmap,
                plan.Frames[frameIndex],
                outputWidth,
                outputHeight,
                safeDesignWidth,
                safeDesignHeight,
                motionBlurStrength,
                previousCenter,
                previousRenderedFramePath);

            renderedBitmap.Save(outputPath, ImageFormat.Png);

            previousCenter = TransformCenter(plan.Frames[frameIndex].Camera.Center, outputWidth, outputHeight, safeDesignWidth, safeDesignHeight);
            previousRenderedFramePath = outputPath;

            if (frameIndex % 24 == 0)
            {
                await Task.Yield();
            }
        }

        return new FrameRenderResult(
            FramesDirectory: outputFramesDirectory,
            FrameCount: plan.Frames.Count,
            Width: outputWidth,
            Height: outputHeight);
    }

    private static Bitmap RenderFrame(
        Bitmap source,
        PreviewFrame frame,
        int outputWidth,
        int outputHeight,
        int designWidth,
        int designHeight,
        double motionBlurStrength,
        ScreenPoint? previousCenter,
        string? previousRenderedFramePath)
    {
        var mappedCenter = TransformCenter(frame.Camera.Center, outputWidth, outputHeight, designWidth, designHeight);
        var cameraScale = Math.Max(1.0d, frame.Camera.Scale);

        var cropWidth = outputWidth / cameraScale;
        var cropHeight = outputHeight / cameraScale;

        cropWidth = Math.Clamp(cropWidth, 2.0d, outputWidth);
        cropHeight = Math.Clamp(cropHeight, 2.0d, outputHeight);

        var cropX = Math.Clamp(mappedCenter.X - (cropWidth / 2.0d), 0.0d, Math.Max(0.0d, outputWidth - cropWidth));
        var cropY = Math.Clamp(mappedCenter.Y - (cropHeight / 2.0d), 0.0d, Math.Max(0.0d, outputHeight - cropHeight));

        var sourceRect = new RectangleF((float)cropX, (float)cropY, (float)cropWidth, (float)cropHeight);

        var targetBitmap = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(targetBitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;

        graphics.DrawImage(
            source,
            new Rectangle(0, 0, outputWidth, outputHeight),
            sourceRect,
            GraphicsUnit.Pixel);

        if (
            motionBlurStrength > 0.001d &&
            previousCenter is { } priorCenter &&
            !string.IsNullOrWhiteSpace(previousRenderedFramePath) &&
            File.Exists(previousRenderedFramePath))
        {
            var centerVelocity = mappedCenter.DistanceTo(priorCenter);
            var blurAlpha = Math.Clamp((centerVelocity / 110.0d) * motionBlurStrength, 0.0d, 0.28d);

            if (blurAlpha > 0.001d)
            {
                using var previousBitmap = new Bitmap(previousRenderedFramePath);
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(new ColorMatrix
                {
                    Matrix33 = (float)blurAlpha
                });

                graphics.DrawImage(
                    previousBitmap,
                    new Rectangle(0, 0, outputWidth, outputHeight),
                    0,
                    0,
                    outputWidth,
                    outputHeight,
                    GraphicsUnit.Pixel,
                    attributes);
            }
        }

        return targetBitmap;
    }

    private static ScreenPoint TransformCenter(
        ScreenPoint center,
        int outputWidth,
        int outputHeight,
        int designWidth,
        int designHeight)
    {
        var scaleX = outputWidth / (double)designWidth;
        var scaleY = outputHeight / (double)designHeight;

        return new ScreenPoint(
            center.X * scaleX,
            center.Y * scaleY);
    }
}
