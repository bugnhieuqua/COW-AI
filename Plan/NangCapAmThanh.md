
````markdown
# NHIỆM VỤ: Tái cấu trúc tính năng Text-to-Speech (TTS) sang thư viện SayIt

Bạn là một Senior .NET Developer. Hãy giúp tôi thay thế thư viện `EdgeTtsSharp` (đang bị lỗi WebSocketException) bằng thư viện `SayIt` trong dự án ASP.NET Core MVC hiện tại.

Hãy thực hiện chính xác các bước sau:

## Bước 1: Cập nhật Package

1. Mở file `.csproj` hoặc dùng lệnh CLI để xóa package `EdgeTtsSharp`.
2. Cài đặt package `SayIt` (phiên bản mới nhất) bằng lệnh: `dotnet add package SayIt`.

## Bước 2: Xóa bỏ các file/service cũ

1. Xóa file `Services/EdgeTtsClient.cs` (nếu có).
2. Xóa file `Services/EdgeTtsService.cs` (nếu có).

## Bước 3: Tạo Service mới `SayItTtsService.cs`

Tạo file `Services/SayItTtsService.cs` với nội dung sau:

```csharp
using SayIt;
using Microsoft.Extensions.Configuration;

namespace COW_AI.Services
{
    public class SayItTtsService
    {
        private readonly string _defaultVoice;

        public SayItTtsService(IConfiguration config)
        {
            // Đọc giọng mặc định từ config, fallback về giọng Nữ tiếng Việt
            _defaultVoice = config["EdgeTts:DefaultVoice"] ?? "vi-VN-HoaiMyNeural";
        }

        public async Task StreamToResponseAsync(string text, Stream outputStream, string? voiceName = null, CancellationToken ct = default)
        {
            var speaker = new SayItSpeaker(
                new SayItConfig()
                    .WithVoice(voiceName ?? _defaultVoice)
                    .WithTimeout(60) // Tránh treo kết nối
            );

            // Ghi trực tiếp âm thanh vào Response Body
            await speaker.StreamToAsync(text, outputStream);
        }
    }
}
```
````

## Bước 4: Đăng ký DI trong `Program.cs`

Mở `Program.cs` và thêm dòng sau (nếu chưa có):

```csharp
builder.Services.AddScoped<COW_AI.Services.SayItTtsService>();
```

_(Nhớ xóa đăng ký của `EdgeTtsService` cũ nếu có)._

## Bước 5: Cập nhật Controller (HomeController hoặc TtsController)

Tìm đến nơi đang xử lý logic TTS (thường là `HomeController.cs` hoặc `TtsController.cs`).

1. Thay đổi Dependency Injection: Thay `EdgeTtsService` thành `SayItTtsService`.
2. Cập nhật logic gọi hàm stream âm thanh:

```csharp
// Thay đổi Constructor
private readonly SayItTtsService _ttsService;
public HomeController(..., SayItTtsService ttsService)
{
    _ttsService = ttsService;
}

// Trong Action xử lý (ví dụ StreamChat hoặc Speak)
[HttpPost]
public async Task<IActionResult> StreamChat(string prompt, string? selectedModel, CancellationToken ct)
{
    try
    {
        // Xử lý Edge TTS (Nếu người dùng chọn)
        if (selectedModel == "edge-male" || selectedModel == "edge-female")
        {
            string voiceName = selectedModel == "edge-male" ? "vi-VN-NamMinhNeural" : "vi-VN-HoaiMyNeural";

            Response.Headers.Append("Content-Type", "audio/mpeg");
            Response.Headers.Append("Cache-Control", "no-cache");

            // Gọi Service mới
            await _ttsService.StreamToResponseAsync(prompt, Response.Body, voiceName, ct);

            return new EmptyResult();
        }

        // ... Các logic xử lý AI khác giữ nguyên ...
    }
    catch (Exception ex)
    {
        // Ghi log lỗi
        // _logger.LogError(ex, "Lỗi TTS");
        return StatusCode(500, "Không thể tạo âm thanh. Vui lòng thử lại sau.");
    }
}
```

## Bước 6: Kiểm tra và dọn dẹp

1. Đảm bảo không còn bất kỳ dòng code nào tham chiếu đến `EdgeTtsSharp` hay `EdgeTtsClient`.
2. Chạy lệnh `dotnet build` để kiểm tra lỗi biên dịch.
3. Báo cáo lại kết quả (thành công hay lỗi ở file nào).

```

---

```
