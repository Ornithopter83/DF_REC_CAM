using OpenCvSharp;

namespace DFBlackbox.Utils;

public static class BitmapConverter
{
    public static Bitmap ToBitmap(Mat mat)
    {
        Cv2.ImEncode(".bmp", mat, out var bytes);
        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }
}
