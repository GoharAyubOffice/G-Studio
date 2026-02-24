using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GStudio.Common.Events;
using GStudio.Common.Geometry;
using GStudio.Project.Store;
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
        string? frameTimelinePath = null,
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
        var frameTimeline = await LoadFrameTimelineAsync(frameTimelinePath, cancellationToken).ConfigureAwait(false);
        var timelineCursor = 0;

        string? previousRenderedFramePath = null;
        ScreenPoint? previousCenter = null;

        for (var frameIndex = 0; frameIndex < plan.Frames.Count; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceIndex = ResolveSourceFrameIndex(
                plan.Frames[frameIndex].Time,
                frameIndex,
                sourceFrames.Length,
                frameTimeline,
                ref timelineCursor);

            var sourcePath = sourceFrames[sourceIndex];
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

            var encoder = GetPngEncoder();
            var encoderParams = GetHighQualityEncoderParams();
            renderedBitmap.Save(outputPath, encoder, encoderParams);

            previousCenter = TransformPoint(plan.Frames[frameIndex].Camera.Center, outputWidth, outputHeight, safeDesignWidth, safeDesignHeight);
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

    private static int ResolveSourceFrameIndex(
        double outputTime,
        int outputFrameIndex,
        int sourceFrameCount,
        IReadOnlyList<FrameTimestampEvent> frameTimeline,
        ref int timelineCursor)
    {
        if (sourceFrameCount <= 1)
        {
            return 0;
        }

        if (frameTimeline.Count == 0)
        {
            return Math.Clamp(outputFrameIndex, 0, sourceFrameCount - 1);
        }

        if (outputTime <= frameTimeline[0].Time)
        {
            return Math.Clamp(frameTimeline[0].Index, 0, sourceFrameCount - 1);
        }

        while (
            timelineCursor + 1 < frameTimeline.Count &&
            frameTimeline[timelineCursor + 1].Time <= outputTime)
        {
            timelineCursor++;
        }

        return Math.Clamp(frameTimeline[timelineCursor].Index, 0, sourceFrameCount - 1);
    }

    private static async Task<IReadOnlyList<FrameTimestampEvent>> LoadFrameTimelineAsync(
        string? frameTimelinePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(frameTimelinePath) || !File.Exists(frameTimelinePath))
        {
            return Array.Empty<FrameTimestampEvent>();
        }

        var timeline = await NdjsonStreamReader
            .ReadAllAsync<FrameTimestampEvent>(frameTimelinePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (timeline.Count == 0)
        {
            return Array.Empty<FrameTimestampEvent>();
        }

        return timeline
            .Where(eventItem => eventItem.Index >= 0 && double.IsFinite(eventItem.Time))
            .OrderBy(static eventItem => eventItem.Time)
            .ToArray();
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
        var mappedCenter = TransformPoint(frame.Camera.Center, outputWidth, outputHeight, designWidth, designHeight);
        var mappedCursor = TransformPoint(frame.Cursor.Position, outputWidth, outputHeight, designWidth, designHeight);
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
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

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

        DrawCursorGlyph(
            graphics,
            mappedCursor,
            frame.Cursor.Hidden,
            cropX,
            cropY,
            cropWidth,
            cropHeight,
            outputWidth,
            outputHeight);

        return targetBitmap;
    }

    private static ScreenPoint TransformPoint(
        ScreenPoint point,
        int outputWidth,
        int outputHeight,
        int designWidth,
        int designHeight)
    {
        var scaleX = outputWidth / (double)designWidth;
        var scaleY = outputHeight / (double)designHeight;

        return new ScreenPoint(
            point.X * scaleX,
            point.Y * scaleY);
    }

    private static void DrawCursorGlyph(
        Graphics graphics,
        ScreenPoint mappedCursor,
        bool hidden,
        double cropX,
        double cropY,
        double cropWidth,
        double cropHeight,
        int outputWidth,
        int outputHeight)
    {
        if (hidden)
        {
            return;
        }

        var projectedX = (mappedCursor.X - cropX) * (outputWidth / cropWidth);
        var projectedY = (mappedCursor.Y - cropY) * (outputHeight / cropHeight);

        if (projectedX < -40.0d || projectedY < -40.0d || projectedX > outputWidth + 40.0d || projectedY > outputHeight + 40.0d)
        {
            return;
        }

        var scale = Math.Clamp(outputWidth / 1920.0d, 0.8d, 1.8d);
        var points = new[]
        {
            new PointF(0f, 0f),
            new PointF(0f, 24f),
            new PointF(6f, 18f),
            new PointF(11f, 30f),
            new PointF(16f, 27f),
            new PointF(11f, 15f),
            new PointF(19f, 15f)
        };

        using var cursorPath = new GraphicsPath();
        cursorPath.AddPolygon(points.Select(p =>
            new PointF(
                (float)(projectedX + (p.X * scale)),
                (float)(projectedY + (p.Y * scale)))).ToArray());

        using var shadowPath = new GraphicsPath();
        shadowPath.AddPath(cursorPath, false);
        using (var matrix = new Matrix())
        {
            matrix.Translate((float)(2.0d * scale), (float)(2.0d * scale));
            shadowPath.Transform(matrix);
        }

        using var shadowBrush = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
        using var fillBrush = new SolidBrush(Color.WhiteSmoke);
        using var borderPen = new Pen(Color.FromArgb(180, 0, 0, 0), (float)Math.Max(1.0d, 1.2d * scale));
        using var hotspotBrush = new SolidBrush(Color.FromArgb(220, 10, 125, 115));

        graphics.FillPath(shadowBrush, shadowPath);
        graphics.FillPath(fillBrush, cursorPath);
        graphics.DrawPath(borderPen, cursorPath);

        var hotspotSize = (float)(4.0d * scale);
        graphics.FillEllipse(
            hotspotBrush,
            (float)(projectedX - hotspotSize * 0.35f),
            (float)(projectedY - hotspotSize * 0.35f),
            hotspotSize,
            hotspotSize);
    }

    private static ImageCodecInfo GetPngEncoder()
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == ImageFormat.Png.Guid)
            {
                return codec;
            }
        }
        return codecs[0];
    }

    private static EncoderParameters GetHighQualityEncoderParams()
    {
        var param = new EncoderParameters(1);
        param.Param[0] = new EncoderParameter(Encoder.Quality, 100L);
        return param;
    }
}
