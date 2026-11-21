using System.Collections.Concurrent;
using System.Security.Claims;

namespace COW_AI.Services;

public record ImageQuotaSnapshot(
    int ImagePageUsed,
    int ImagePageLimit,
    int ImagePageRemaining,
    int HomePageUsed,
    int HomePageLimit,
    int HomePageRemaining,
    int TotalUsed,
    int TotalLimit,
    int TotalRemaining
);

/// <summary>
/// Quản lý hạn mức tạo ảnh: tối đa 5 ảnh tại Trang tạo ảnh (/Image) + 5 ảnh tại Trang chủ (/) = tổng 10 ảnh.
/// </summary>
public class ImageQuotaService
{
    public const int MaxImagePageGenerations = 5;
    public const int MaxHomePageGenerations = 5;
    public const int MaxTotalGenerations = 10;

    private sealed class QuotaBucket
    {
        public int ImagePageCount;
        public int HomePageCount;
        public readonly object SyncRoot = new();
    }

    private readonly ConcurrentDictionary<string, QuotaBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);

    public static string ResolveCallerKey(HttpContext? httpContext)
    {
        if (httpContext == null) return "global";

        if (httpContext.User?.Identity?.IsAuthenticated == true)
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                return $"user:{userId}";
            }
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(ip) ? "anon:local" : $"ip:{ip}";
    }

    public ImageQuotaSnapshot GetQuota(HttpContext? httpContext)
    {
        var bucket = _buckets.GetOrAdd(ResolveCallerKey(httpContext), _ => new QuotaBucket());
        lock (bucket.SyncRoot)
        {
            return BuildSnapshotLocked(bucket);
        }
    }

    public bool TryConsumeQuota(
        HttpContext? httpContext,
        string source,
        out ImageQuotaSnapshot snapshot,
        out string? errorMessage)
    {
        var bucket = _buckets.GetOrAdd(ResolveCallerKey(httpContext), _ => new QuotaBucket());
        lock (bucket.SyncRoot)
        {
            var total = bucket.ImagePageCount + bucket.HomePageCount;
            if (total >= MaxTotalGenerations)
            {
                snapshot = BuildSnapshotLocked(bucket);
                errorMessage = $"Bạn đã sử dụng hết tổng hạn mức tạo ảnh ({MaxTotalGenerations}/{MaxTotalGenerations} ảnh: {MaxImagePageGenerations} Trang tạo ảnh + {MaxHomePageGenerations} Trang chủ).";
                return false;
            }

            if (string.Equals(source, "image_page", StringComparison.OrdinalIgnoreCase))
            {
                if (bucket.ImagePageCount >= MaxImagePageGenerations)
                {
                    snapshot = BuildSnapshotLocked(bucket);
                    errorMessage = $"Đã đạt giới hạn tạo tối đa {MaxImagePageGenerations}/{MaxImagePageGenerations} ảnh tại Trang tạo ảnh. Bạn vẫn còn {Math.Max(0, MaxHomePageGenerations - bucket.HomePageCount)}/{MaxHomePageGenerations} lượt tạo ảnh ở Trang chủ (tổng {MaxTotalGenerations} ảnh).";
                    return false;
                }
                bucket.ImagePageCount++;
            }
            else
            {
                if (bucket.HomePageCount >= MaxHomePageGenerations)
                {
                    snapshot = BuildSnapshotLocked(bucket);
                    errorMessage = $"Đã đạt giới hạn tạo tối đa {MaxHomePageGenerations}/{MaxHomePageGenerations} ảnh tại Trang chủ. Bạn vẫn còn {Math.Max(0, MaxImagePageGenerations - bucket.ImagePageCount)}/{MaxImagePageGenerations} lượt tại Trang tạo ảnh (tổng {MaxTotalGenerations} ảnh).";
                    return false;
                }
                bucket.HomePageCount++;
            }

            snapshot = BuildSnapshotLocked(bucket);
            errorMessage = null;
            return true;
        }
    }

    public ImageQuotaSnapshot RefundQuota(HttpContext? httpContext, string source)
    {
        var bucket = _buckets.GetOrAdd(ResolveCallerKey(httpContext), _ => new QuotaBucket());
        lock (bucket.SyncRoot)
        {
            if (string.Equals(source, "image_page", StringComparison.OrdinalIgnoreCase))
            {
                if (bucket.ImagePageCount > 0) bucket.ImagePageCount--;
            }
            else
            {
                if (bucket.HomePageCount > 0) bucket.HomePageCount--;
            }
            return BuildSnapshotLocked(bucket);
        }
    }

    private static ImageQuotaSnapshot BuildSnapshotLocked(QuotaBucket bucket)
    {
        int imgUsed = bucket.ImagePageCount;
        int homeUsed = bucket.HomePageCount;
        int totalUsed = imgUsed + homeUsed;
        return new ImageQuotaSnapshot(
            ImagePageUsed: imgUsed,
            ImagePageLimit: MaxImagePageGenerations,
            ImagePageRemaining: Math.Max(0, MaxImagePageGenerations - imgUsed),
            HomePageUsed: homeUsed,
            HomePageLimit: MaxHomePageGenerations,
            HomePageRemaining: Math.Max(0, MaxHomePageGenerations - homeUsed),
            TotalUsed: totalUsed,
            TotalLimit: MaxTotalGenerations,
            TotalRemaining: Math.Max(0, MaxTotalGenerations - totalUsed)
        );
    }
}
