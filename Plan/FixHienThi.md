Lỗi hiển thị các thông báo kỹ thuật thô (như thiếu API Key hay lỗi Exception) lên giao diện người dùng là một lỗi cực kỳ nghiêm trọng về mặt bảo mật và trải nghiệm người dùng (UX). Nó vừa làm lộ cấu trúc hệ thống, vừa khiến ứng dụng trông thiếu chuyên nghiệp.

Dưới đây là **Mục 15** bổ sung vào `KeHoach.md`, hướng dẫn chi tiết cách xử lý lỗi (Error Handling) từ Backend đến Frontend để giao diện luôn hiển thị thông báo thân thiện.

---

```markdown
## 15. Xử lý Lỗi (Error Handling) & Ẩn Thông tin Kỹ thuật

### 15.1. Nguyên tắc Cốt lõi
- **Tuyệt đối không** trả về `Exception.Message` (lỗi C#), StackTrace, hay các thông báo cấu hình (như thiếu Key, sai Model) ra giao diện người dùng.
- Mọi lỗi kỹ thuật phải được ghi vào Log (sử dụng `ILogger` trong ASP.NET Core) để lập trình viên kiểm tra.
- Người dùng cuối chỉ nhận được các thông báo chung chung, thân thiện (VD: "Hệ thống đang bận", "Đã có lỗi xảy ra, vui lòng thử lại sau").

### 15.2. Xử lý tại Backend (ASP.NET Core)

**Bước 1: Cấu hình Middleware xử lý lỗi toàn cục trong `Program.cs`**
```csharp
// Thêm vào trước app.UseRouting() hoặc app.MapControllerRoute()
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage(); // Chỉ hiện lỗi chi tiết khi code ở máy local
}
else
{
    // Khi deploy lên host thật, nếu có lỗi sẽ tự động chuyển hướng đến trang /Home/Error
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
```

**Bước 2: Bọc try-catch trong `OpenRouterService.cs` và `HomeController.cs`**
Thay vì văng lỗi `throw new InvalidOperationException("Chưa cấu hình...")` ra giao diện, hãy xử lý khéo léo.

Sửa `OpenRouterService.cs`:
```csharp
public OpenRouterService(HttpClient http, IConfiguration config, ILogger<OpenRouterService> logger)
{
    _http = http;
    var apiKey = config["OpenRouter:ApiKey"];
    
    // Nếu thiếu Key, ghi log nhưng KHÔNG văng lỗi ra UI
    if (string.IsNullOrEmpty(apiKey))
    {
        logger.LogError("CRITICAL: Chưa cấu hình OpenRouter:ApiKey.");
        _http.DefaultRequestHeaders.Authorization = null; // Không set auth
        return;
    }
    _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
}
```

Sửa `HomeController.cs` (Bọc logic trong try-catch):
```csharp
[HttpPost]
public async Task<IActionResult> StreamChat(string prompt)
{
    try
    {
        var stream = await _openRouter.StreamChatAsync(prompt);
        // ... Code stream ...
        return new EmptyResult();
    }
    catch (Exception ex)
    {
        // Ghi log lỗi chi tiết cho Dev xem
        _logger.LogError(ex, "Lỗi khi gọi API OpenRouter");
        
        // Trả về lỗi 500 và một JSON an toàn cho Frontend
        Response.StatusCode = 500;
        return Json(new { error = "Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút." });
    }
}
```

### 15.3. Xử lý tại Frontend (JavaScript)
Frontend phải kiểm tra trạng thái HTTP của response (thông qua `response.ok`), nếu có lỗi thì chỉ hiển thị thông báo chung, không in lỗi kỹ thuật.

Cập nhật hàm `handleSend()` trong JS:
```javascript
async function handleSend() {
    // ... (Lấy prompt, chuyển giao diện, hiển thị tin nhắn user) ...

    const aiBubble = appendMessage('ai', 'Đang xử lý...');

    try {
        const response = await fetch('/Home/StreamChat', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: `prompt=${encodeURIComponent(prompt)}`
        });

        // Bắt lỗi HTTP (400, 500, v.v.)
        if (!response.ok) {
            // Cố gắng đọc thông báo lỗi an toàn từ Backend
            let errorMsg = "Đã xảy ra lỗi kết nối.";
            try {
                const errorData = await response.json();
                errorMsg = errorData.error || errorMsg; // Lấy câu "Hệ thống đang bận..."
            } catch (e) {
                // Bỏ qua nếu Backend không trả về JSON
            }
            throw new Error(errorMsg);
        }

        // Nếu response.ok -> Tiến hành đọc Stream như bình thường
        const reader = response.body.getReader();
        // ... (Code đọc stream) ...
        
    } catch (error) {
        // Hiển thị lỗi thân thiện, KHÔNG hiển thị error.message của C# (nếu có lọt ra)
        aiBubble.innerText = error.message || "Không thể kết nối đến máy chủ AI. Vui lòng kiểm tra mạng.";
        aiBubble.style.color = '#ef4444';
    } finally {
        inputField.disabled = false;
        inputField.focus();
    }
}
```

### 15.4. Tạo Trang Lỗi (Error Page) dự phòng
Trong thư mục `Views/Shared`, tạo file `Error.cshtml` để hứng các lỗi không lường trước:
```html
@{
    ViewData["Title"] = "Lỗi hệ thống";
}
<div class="error-container" style="text-align: center; margin-top: 100px;">
    <h1 style="color: #ef4444;">Rất tiếc!</h1>
    <p>Đã có lỗi xảy ra trong quá trình xử lý.</p>
    <p>Vui lòng thử lại sau hoặc liên hệ quản trị viên.</p>
    <a href="/" class="btn btn-primary">Quay lại Trang chủ</a>
</div>
```

### 15.5. Prompt cho AI (Yêu cầu dọn dẹp lỗi)
> "Hãy rà soát lại toàn bộ file `OpenRouterService.cs`, `HomeController.cs` và các file JavaScript. 
> 1. Xóa bỏ mọi đoạn code ném lỗi `throw new InvalidOperationException` chứa text cấu hình Key/Model ra ngoài.
> 2. Thay thế bằng khối `try-catch`, ghi log lỗi qua `ILogger` và trả về `StatusCode = 500` kèm thông báo chung: 'Hệ thống đang bận'.
> 3. Trong JS, bắt sự kiện `response.ok` để hiển thị text lỗi màu đỏ thân thiện: 'Không thể kết nối đến AI', tuyệt đối không hiển thị lỗi thô."
```