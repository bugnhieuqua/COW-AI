namespace COW_AI.Services;

public interface IImageCompressionService
{
    /// <summary>
    /// Tự động nén và thu nhỏ ảnh về kích thước tối ưu (mặc định max 1920px),
    /// chuyển đổi sang định dạng WebP chất lượng cao (quality 82%) để tiết kiệm 80-90% dung lượng đĩa.
    /// </summary>
    Task<(bool Success, byte[] CompressedBytes, string Extension, string ContentType)> CompressImageAsync(
        Stream inputStream,
        string originalFileName,
        int maxDimension = 1920,
        int quality = 82,
        CancellationToken ct = default);

    /// <summary>
    /// Nén mảng byte hình ảnh trực tiếp.
    /// </summary>
    Task<(bool Success, byte[] CompressedBytes, string Extension, string ContentType)> CompressImageBytesAsync(
        byte[] inputBytes,
        string originalFileName,
        int maxDimension = 1920,
        int quality = 82,
        CancellationToken ct = default);
}
