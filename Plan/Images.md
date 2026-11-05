Dựa trên các tìm kiếm, giải pháp **tối ưu và duy nhất** phù hợp nhất cho dự án ASP.NET Core của bạn là thư viện **`Kavel`** (NuGet: `Kavel`). Đây là một SDK C# chính thức, cho phép tạo và chỉnh sửa ảnh **không cần API Key, không cần tài khoản**, tích hợp trực tiếp vào code C# mà không cần chạy thêm server phụ. 

Dưới đây là file kế hoạch Markdown chi tiết để bạn đưa vào dự án.

---

```markdown
# KẾ HOẠCH TÍCH HỢP KAVEL AI (THAY THẾ POLLINATIONS)

## 1. Lý do chọn Kavel
- **Không cần API Key/Account:** Thư viện tự tạo `client id` ẩn, không cần đăng ký, không cần cấu hình secret trong `appsettings.json`.
- **Tích hợp trực tiếp C#/.NET:** Hỗ trợ .NET 8 và .NET Standard 2.0, chạy tốt trên ASP.NET Core.
- **Hỗ trợ tạo và chỉnh sửa ảnh:** Có sẵn hàm `GenerateAsync` và `EditAsync`.
- **Xử lý lỗi rõ ràng:** Phân loại lỗi `Quota` (hết lượt), `Rejected` (prompt bị từ chối), `SignIn` (yêu cầu đăng nhập).

## 2. Cài đặt
Chạy lệnh trong Terminal:
```bash
dotnet add package Kavel --version 0.2.0
```

## 3. Cấu hình (Không cần API Key)
Thêm vào `appsettings.json` các tham số mặc định (có thể điều chỉnh sau):
```json
{
  "Kavel": {
    "DefaultAspectRatio": "16:9",
    "MaxPollingMinutes": 6
  }
}
```

## 4. Tạo `KavelImageService.cs`
Service này bọc `KavelClient` và đọc cấu hình từ `IConfiguration`.

```csharp
using Kavel;
using Microsoft.Extensions.Configuration;

public class KavelImageService
{
    private readonly KavelClient _client;
    private readonly string _defaultAspectRatio;
    private readonly TimeSpan _timeout;

    public KavelImageService(IConfiguration config)
    {
        _client = new KavelClient();
        _defaultAspectRatio = config["Kavel:DefaultAspectRatio"] ?? "16:9";
        var minutes = config.GetValue<int>("Kavel:MaxPollingMinutes", 6);
        _timeout = TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// Tạo ảnh từ prompt. Trả về URL ảnh.
    /// </summary>
    public async Task<string> GenerateImageAsync(string prompt, string? aspectRatio = null, CancellationToken ct = default)
    {
        try
        {
            var img = await _client.GenerateAsync(
                prompt,
                aspectRatio: aspectRatio ?? _defaultAspectRatio,
                cancellationToken: ct
            );
            return img.Url;
        }
        catch (KavelException ex) when (ex.Kind == KavelErrorKind.Quota)
        {
            throw new InvalidOperationException("Đã hết lượt tạo ảnh miễn phí hôm nay. Vui lòng thử lại sau.", ex);
        }
        catch (KavelException ex) when (ex.Kind == KavelErrorKind.Rejected)
        {
            throw new InvalidOperationException("Nội dung yêu cầu không hợp lệ. Vui lòng thử lại với mô tả khác.", ex);
        }
        catch (KavelException ex) when (ex.Kind == KavelErrorKind.SignIn)
        {
            throw new InvalidOperationException("Tính năng này cần đăng nhập. Vui lòng liên hệ quản trị viên.", ex);
        }
    }
}
```

## 5. Đăng ký DI trong `Program.cs`
```csharp
builder.Services.AddSingleton<KavelImageService>();
```

## 6. Cập nhật `HomeController.cs`
Thay thế hoàn toàn `PollinationsService` bằng `KavelImageService` trong action `StreamChat`.

```csharp
// Inject KavelImageService
private readonly KavelImageService _kavelImage;

public HomeController(..., KavelImageService kavelImage)
{
    // ...
    _kavelImage = kavelImage;
}

// Trong StreamChat, thay block xử lý Pollinations
if (modelToUse == "pollinations" || modelToUse == "kavel") // Giữ id cũ để tương thích UI
{
    try
    {
        var imageUrl = await _kavelImage.GenerateImageAsync(prompt);
        
        Response.Headers.Append("Content-Type", "text/event-stream");
        var payload = System.Text.Json.JsonSerializer.Serialize(new { is_image_url = imageUrl });
        await Response.WriteAsync($"data: {payload}\n\n");
        await Response.WriteAsync("data: [DONE]\n\n");
        return new EmptyResult();
    }
    catch (InvalidOperationException ex)
    {
        // Trả lỗi thân thiện cho frontend
        Response.StatusCode = 400;
        return Json(new { error = ex.Message });
    }
}
```

## 7. Cập nhật Frontend (JS)
Không cần thay đổi logic hiển thị vì vẫn nhận `is_image_url` từ SSE. Chỉ cần đảm bảo xử lý lỗi khi server trả về JSON `{ error: "..." }`.

```javascript
if (!response.ok) {
    const err = await response.json();
    throw new Error(err.error || "Lỗi tạo ảnh");
}
```

## 8. Lưu ý quan trọng về giới hạn của Kavel (Free Tier)
- **15 credits** miễn phí ban đầu, mỗi ảnh tốn 5 credits → khoảng 3 ảnh đầu tiên.
- **30 credits/IP/ngày** → khoảng 6 ảnh/ngày từ một IP.
- **Ảnh miễn phí có watermark và độ phân giải 1K**.
- **Thời gian chờ:** 25–80 giây trước khi ảnh được tạo.
- Nếu cần chất lượng cao hơn (không watermark, 4K), có thể đăng nhập tài khoản Kavel (không bắt buộc cho đồ án).

## 9. Prompt cho AI (Ra lệnh tích hợp)
> "Hãy tích hợp thư viện NuGet `Kavel` vào dự án ASP.NET Core của tôi để thay thế Pollinations AI.
> 1. Cài đặt `dotnet add package Kavel --version 0.2.0`.
> 2. Tạo `KavelImageService` với phương thức `GenerateImageAsync(string prompt, string aspectRatio)` sử dụng `KavelClient.GenerateAsync()`. Đọc `DefaultAspectRatio` từ `appsettings.json`.
> 3. Xử lý các ngoại lệ `KavelException` với `Kind` là `Quota`, `Rejected`, `SignIn` và ném ra `InvalidOperationException` với thông báo tiếng Việt thân thiện.
> 4. Đăng ký service trong `Program.cs` dạng Singleton.
> 5. Trong `HomeController`, thay thế lời gọi `PollinationsService` bằng `KavelImageService` cho model `pollinations` (hoặc thêm model `kavel`).
> 6. Trả về URL ảnh qua SSE cho frontend như cũ."
```

Đối với những chức năng chưa quen thuộc, đừng viết từ con số 0. Hãy tìm kiếm các dự án mã nguồn mở trên Gthub để so sánh ý tưởng trước khi bắt tay vào làm.