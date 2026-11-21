using SkiaSharp;

namespace COW_AI.Services;

public class ImageCompressionService : IImageCompressionService
{
    private readonly ILogger<ImageCompressionService> _logger;

    public ImageCompressionService(ILogger<ImageCompressionService> logger)
    {
        _logger = logger;
    }

    public async Task<(bool Success, byte[] CompressedBytes, string Extension, string ContentType)> CompressImageAsync(
        Stream inputStream,
        string originalFileName,
        int maxDimension = 1920,
        int quality = 82,
        CancellationToken ct = default)
    {
        using var mem = new MemoryStream();
        await inputStream.CopyToAsync(mem, ct);
        return await CompressImageBytesAsync(mem.ToArray(), originalFileName, maxDimension, quality, ct);
    }

    public Task<(bool Success, byte[] CompressedBytes, string Extension, string ContentType)> CompressImageBytesAsync(
        byte[] inputBytes,
        string originalFileName,
        int maxDimension = 1920,
        int quality = 82,
        CancellationToken ct = default)
    {
        if (inputBytes == null || inputBytes.Length == 0)
        {
            return Task.FromResult((false, Array.Empty<byte>(), Path.GetExtension(originalFileName).ToLowerInvariant(), "application/octet-stream"));
        }

        var origExt = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(origExt)) origExt = ".png";

        // Tệp GIF giữ nguyên để bảo tồn chuyển động hoạt ảnh
        if (origExt == ".gif")
        {
            return Task.FromResult((true, inputBytes, ".gif", "image/gif"));
        }

        try
        {
            using var mem = new MemoryStream(inputBytes);
            using var originalBitmap = SKBitmap.Decode(mem);

            if (originalBitmap == null)
            {
                _logger.LogWarning("SkiaSharp không thể giải mã hình ảnh {FileName}, lưu trữ ảnh gốc", originalFileName);
                return Task.FromResult((false, inputBytes, origExt, "application/octet-stream"));
            }

            int w = originalBitmap.Width;
            int h = originalBitmap.Height;

            if (w > maxDimension || h > maxDimension)
            {
                if (w >= h)
                {
                    h = Math.Max(1, (int)((float)h / w * maxDimension));
                    w = maxDimension;
                }
                else
                {
                    w = Math.Max(1, (int)((float)w / h * maxDimension));
                    h = maxDimension;
                }
            }

            SKBitmap targetBitmap = originalBitmap;
            bool wasResized = false;

            if (w != originalBitmap.Width || h != originalBitmap.Height)
            {
                var resized = originalBitmap.Resize(new SKImageInfo(w, h), SKSamplingOptions.Default);
                if (resized != null)
                {
                    targetBitmap = resized;
                    wasResized = true;
                }
            }

            try
            {
                using var image = SKImage.FromBitmap(targetBitmap);
                // Nén sang định dạng WebP chất lượng 82% (tiết kiệm dung lượng tối đa, giữ màu sắc trung thực)
                using var encoded = image.Encode(SKEncodedImageFormat.Webp, quality);
                if (encoded != null && encoded.Size > 0)
                {
                    var compressedBytes = encoded.ToArray();
                    var savingsPercent = (1.0 - (double)compressedBytes.Length / inputBytes.Length) * 100;
                    _logger.LogInformation("Nén ảnh thành công {FileName}: {OrigKB:F1} KB -> {NewKB:F1} KB (Tiết kiệm {Savings:F1}%)",
                        originalFileName,
                        inputBytes.Length / 1024.0,
                        compressedBytes.Length / 1024.0,
                        Math.Max(0, savingsPercent));

                    return Task.FromResult((true, compressedBytes, ".webp", "image/webp"));
                }

                // Fallback nếu encoder Webp trả về null (thử Jpeg)
                using var fallbackJpeg = image.Encode(SKEncodedImageFormat.Jpeg, quality);
                if (fallbackJpeg != null && fallbackJpeg.Size > 0)
                {
                    return Task.FromResult((true, fallbackJpeg.ToArray(), ".jpg", "image/jpeg"));
                }
            }
            finally
            {
                if (wasResized)
                {
                    targetBitmap.Dispose();
                }
            }

            return Task.FromResult((true, inputBytes, origExt, "application/octet-stream"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra trong quá trình nén ảnh {FileName}, giữ nguyên bản gốc", originalFileName);
            return Task.FromResult((false, inputBytes, origExt, "application/octet-stream"));
        }
    }
}
