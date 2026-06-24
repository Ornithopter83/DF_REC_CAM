using DFBlackbox.Models;
using OpenCvSharp;

namespace DFBlackbox.Core;

public sealed class DetectionService : IDisposable
{
    private Mat? _baselineGray;
    private Mat? _homeReferenceGray;

    public DetectionResult Analyze(Mat frame, AppSettings settings)
    {
        var result = new DetectionResult { Timestamp = DateTime.Now };
        using var gray = new Mat();
        using var blurred = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(5, 5), 0);

        if (_baselineGray is null)
        {
            FillHomeResult(frame, blurred, settings, result);
            result.DebugText = "baseline missing";
            return result;
        }

        using var diff = new Mat();
        using var threshold = new Mat();
        using var baseline = CreateFrameSizedBaseline(blurred);
        Cv2.Absdiff(blurred, baseline, diff);
        Cv2.Threshold(diff, threshold, settings.Detection.MotionThreshold, 255, ThresholdTypes.Binary);
        Cv2.MorphologyEx(threshold, threshold, MorphTypes.Open, Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3)));
        Cv2.MorphologyEx(threshold, threshold, MorphTypes.Close, Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(7, 7)));
        var mainRoi = ScaleRoiToFrame(settings.Rois.PersonWatchRoi, settings, threshold.Width, threshold.Height);
        var ignoreRoi = ScaleRoiToFrame(settings.Rois.IgnoreRoi, settings, threshold.Width, threshold.Height);
        var ignoreRois = settings.Rois.IgnoreRois
            .Select(roi => ScaleRoiToFrame(roi, settings, threshold.Width, threshold.Height))
            .Append(ignoreRoi)
            .ToArray();
        using var detectionMask = CreateDetectionMask(threshold.Size(), mainRoi, ignoreRois);
        Cv2.BitwiseAnd(threshold, detectionMask, threshold);
        result.MotionBoxes = FindMotionBoxes(threshold, settings.Detection.MinMotionArea);
        var mainRoiPixels = Cv2.CountNonZero(detectionMask);
        var roiChangedPixels = Cv2.CountNonZero(threshold);
        var ignoreAreaDiff = 0;
        var roiDiff = mainRoiPixels <= 0 ? 0 : roiChangedPixels / (double)mainRoiPixels;
        result.PersonMotionScore = roiDiff;
        result.RodMotionScore = ignoreAreaDiff;
        result.HomeMotionScore = 0;
        result.PersonCandidateBoxes = [];
        result.PersonDetected = roiDiff >= settings.Detection.PersonMotionRatioThreshold;
        result.RodMotionDetected = result.PersonDetected;
        result.StrongRodMotionDetected = false;

        result.HomeSimilar = true;
        result.HomeMotionLow = true;
        result.HomeStable = !result.PersonDetected;
        result.HomeDiffScore = roiDiff;
        result.DebugText =
            $"ROI_Diff={roiDiff:0.000} Ignore={ignoreAreaDiff:0.000} th={settings.Detection.PersonMotionRatioThreshold:0.000}";
        return result;
    }

    public void SetBaseline(Mat frame)
    {
        _baselineGray?.Dispose();
        _baselineGray = PrepareGray(frame);
    }

    public void LoadBaselineReference(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var image = Cv2.ImRead(path);
        if (!image.Empty())
        {
            SetBaseline(image);
        }
    }

    public void SaveBaselineReference(Mat frame, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Cv2.ImWrite(path, frame);
        SetBaseline(frame);
    }

    public void SetHomeReference(Mat homeReference)
    {
        _homeReferenceGray?.Dispose();
        _homeReferenceGray = PrepareGray(homeReference);
    }

    public void LoadHomeReference(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var image = Cv2.ImRead(path);
        if (!image.Empty())
        {
            SetHomeReference(image);
        }
    }

    public void SaveHomeReference(Mat frame, RoiRect homeRoi, string path)
    {
        var rect = ClampRect(homeRoi.ToRectangle(), frame.Width, frame.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new InvalidOperationException("RodHomeRoi is outside the current frame.");
        }

        using var roi = new Mat(frame, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Cv2.ImWrite(path, roi);
        SetHomeReference(roi);
    }

    public void ResetBackground()
    {
        _baselineGray?.Dispose();
        _baselineGray = null;
    }

    private Mat CreateFrameSizedBaseline(Mat frameGray)
    {
        if (_baselineGray is null)
        {
            return new Mat(frameGray.Size(), frameGray.Type(), Scalar.Black);
        }

        var baseline = new Mat();
        if (_baselineGray.Size() != frameGray.Size())
        {
            Cv2.Resize(_baselineGray, baseline, frameGray.Size());
        }
        else
        {
            _baselineGray.CopyTo(baseline);
        }

        return baseline;
    }

    private void FillHomeResult(Mat frame, Mat blurred, AppSettings settings, DetectionResult result)
    {
        result.HomeSimilar = false;
        if (_homeReferenceGray is null)
        {
            result.HomeDiffScore = double.MaxValue;
            result.HomeMotionLow = result.HomeMotionScore < settings.Detection.HomeMotionRatioThreshold;
            result.HomeStable = false;
            return;
        }

        var homeRoi = ScaleRoiToFrame(settings.Rois.RodHomeRoi, settings, frame.Width, frame.Height);
        var rect = ClampRect(homeRoi.ToRectangle(), frame.Width, frame.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            result.HomeDiffScore = double.MaxValue;
            return;
        }

        using var currentRoi = new Mat(blurred, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
        using var resizedReference = new Mat();
        if (_homeReferenceGray.Size() != currentRoi.Size())
        {
            Cv2.Resize(_homeReferenceGray, resizedReference, currentRoi.Size());
        }
        else
        {
            _homeReferenceGray.CopyTo(resizedReference);
        }

        using var diff = new Mat();
        Cv2.Absdiff(currentRoi, resizedReference, diff);
        result.HomeDiffScore = Cv2.Mean(diff)[0];
        result.HomeSimilar = result.HomeDiffScore < settings.Detection.HomeDiffThreshold;
        result.HomeMotionLow = result.HomeMotionScore < settings.Detection.HomeMotionRatioThreshold;
        result.HomeStable = result.HomeSimilar && result.HomeMotionLow;
    }

    private static Mat PrepareGray(Mat image)
    {
        var gray = new Mat();
        if (image.Channels() == 1)
        {
            image.CopyTo(gray);
        }
        else
        {
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        }

        Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(5, 5), 0);
        return gray;
    }

    private static Mat CreateDetectionMask(OpenCvSharp.Size size, RoiRect mainRoi, IEnumerable<RoiRect> ignoreRois)
    {
        var mask = new Mat(size, MatType.CV_8UC1, Scalar.Black);
        FillRoi(mask, mainRoi, Scalar.White);
        foreach (var ignoreRoi in ignoreRois)
        {
            FillRoi(mask, ignoreRoi, Scalar.Black);
        }

        return mask;
    }

    private static void FillRoi(Mat mask, RoiRect roi, Scalar color)
    {
        var rect = ClampRect(roi.ToRectangle(), mask.Width, mask.Height);
        if (rect.Width > 0 && rect.Height > 0)
        {
            Cv2.Rectangle(mask, new Rect(rect.X, rect.Y, rect.Width, rect.Height), color, -1);
        }
    }

    private static List<Rectangle> FindMotionBoxes(Mat detectionThreshold, int minArea)
    {
        Cv2.FindContours(detectionThreshold, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        return contours
            .Where(contour => Cv2.ContourArea(contour) >= minArea)
            .Select(Cv2.BoundingRect)
            .Select(rect => new Rectangle(rect.X, rect.Y, rect.Width, rect.Height))
            .ToList();
    }

    private static double MotionRatio(Mat threshold, RoiRect roi)
    {
        var pixels = CountRoiPixels(threshold, roi);
        if (pixels <= 0)
        {
            return 0;
        }

        return CountChangedPixels(threshold, roi) / (double)pixels;
    }

    private static int CountChangedPixels(Mat threshold, RoiRect roi)
    {
        var rect = ClampRect(roi.ToRectangle(), threshold.Width, threshold.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return 0;
        }

        using var roiMat = new Mat(threshold, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
        return Cv2.CountNonZero(roiMat);
    }

    private static int CountRoiPixels(Mat threshold, RoiRect roi)
    {
        var rect = ClampRect(roi.ToRectangle(), threshold.Width, threshold.Height);
        return rect.Width * rect.Height;
    }

    public static RoiRect ScaleRoiToFrame(RoiRect roi, AppSettings settings, int frameWidth, int frameHeight)
    {
        var sourceWidth = Math.Max(1, Math.Max(
            settings.Camera.ActiveWidth,
            new[] { settings.Rois.PersonWatchRoi, settings.Rois.IgnoreRoi, settings.Rois.RodHomeRoi, roi }
                .Max(item => item.X + item.Width)));
        var sourceHeight = Math.Max(1, Math.Max(
            settings.Camera.ActiveHeight,
            new[] { settings.Rois.PersonWatchRoi, settings.Rois.IgnoreRoi, settings.Rois.RodHomeRoi, roi }
                .Max(item => item.Y + item.Height)));
        if (sourceWidth == frameWidth && sourceHeight == frameHeight)
        {
            return roi;
        }

        var scaleX = frameWidth / (double)sourceWidth;
        var scaleY = frameHeight / (double)sourceHeight;
        var scaled = new RoiRect
        {
            X = (int)Math.Round(roi.X * scaleX),
            Y = (int)Math.Round(roi.Y * scaleY),
            Width = (int)Math.Round(roi.Width * scaleX),
            Height = (int)Math.Round(roi.Height * scaleY)
        };
        var rect = ClampRect(scaled.ToRectangle(), frameWidth, frameHeight);
        return RoiRect.FromRectangle(rect);
    }

    private static Rectangle ClampRect(Rectangle rect, int width, int height)
    {
        var x = Math.Clamp(rect.X, 0, width);
        var y = Math.Clamp(rect.Y, 0, height);
        var right = Math.Clamp(rect.Right, 0, width);
        var bottom = Math.Clamp(rect.Bottom, 0, height);
        return new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static bool Intersects(Rectangle rectangle, Rectangle other) => rectangle.IntersectsWith(other);

    public void Dispose()
    {
        _baselineGray?.Dispose();
        _homeReferenceGray?.Dispose();
    }
}
