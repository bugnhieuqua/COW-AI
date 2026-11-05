
---

```markdown
## 21. Tích hợp Edge TTS (Text-to-Speech Miễn phí)

### 21.1. Tổng quan & Lý do chọn Edge TTS
Edge TTS là giải pháp chuyển văn bản thành giọng nói sử dụng chính công nghệ đằng sau tính năng "Đọc to" (Read Aloud) của trình duyệt Microsoft Edge. Ưu điểm vượt trội:
- **Hoàn toàn miễn phí**, không cần API key, không giới hạn request (hoặc giới hạn rất cao) [citation:6†L10-L11].
- **Chất lượng giọng đọc Neural** tự nhiên, hỗ trợ nhiều ngôn ngữ, bao gồm cả **tiếng Việt** với 2 giọng: `vi-VN-HoaiMyNeural` (Nữ) và `vi-VN-NamMinhNeural` (Nam) [citation:3†L7-L8].
- Thư viện .NET `EdgeTtsSharp` hỗ trợ **streaming trực tiếp** ra HTTP Response, cho phép trình duyệt phát audio ngay khi dữ liệu bắt đầu về thay vì phải chờ tải xong toàn bộ file [citation:4†L24-L31].

### 21.2. Cài đặt Thư viện
Chạy lệnh sau trong Package Manager Console hoặc Terminal:
```bash
dotnet add package EdgeTtsSharp --version 2.0.7
```
*(Thư viện yêu cầu .NET Standard 2.1, tương thích hoàn toàn với ASP.NET Core 8)* [citation:5†L2-L3].

### 21.3. Tạo `EdgeTtsService.cs`
Service này chịu trách nhiệm lấy giọng đọc và stream âm thanh. Chúng ta sẽ đọc giọng mặc định từ cấu hình để tránh hardcode.

**Cấu hình trong `appsettings.json`:**
```json
{
  "EdgeTts": {
    "DefaultVoice": "vi-VN-HoaiMyNeural"
  }
}
```

**Tạo file `Services/EdgeTtsService.cs`:**
```csharp
using EdgeTtsSharp;

public class EdgeTtsService
{
    private readonly string _defaultVoice;

    public EdgeTtsService(IConfiguration config)
    {
        // Đọc giọng mặc định, fallback về giọng nữ tiếng Việt nếu chưa cấu hình
        _defaultVoice = config["EdgeTts:DefaultVoice"] ?? "vi-VN-HoaiMyNeural";
    }

    /// <summary>
    /// Stream âm thanh trực tiếp vào HttpResponse
    /// </summary>
    public async Task StreamToResponseAsync(string text, Stream outputStream, string? voiceName = null)
    {
        var voice = await EdgeTts.GetVoice(voiceName ?? _defaultVoice);
        
        // StreamText sẽ ghi dữ liệu âm thanh trực tiếp vào outputStream (thường là Response.Body)
        // Trình duyệt có thể bắt đầu phát ngay khi gói dữ liệu đầu tiên đến
        await voice.StreamText(outputStream, text);
    }

    /// <summary>
    /// Lấy danh sách tất cả giọng đọc có sẵn (dùng cho dropdown nếu muốn mở rộng)
    /// </summary>
    public async Task<IEnumerable<Voice>> GetAvailableVoicesAsync()
    {
        return await EdgeTts.GetVoices();
    }
}
```

### 21.4. Tạo Controller Action (`HomeController.cs` hoặc `TtsController.cs`)
Thêm một action để nhận văn bản và trả về file âm thanh (MP3) dạng stream.

```csharp
[ApiController]
[Route("api/[controller]")]
public class TtsController : ControllerBase
{
    private readonly EdgeTtsService _ttsService;

    public TtsController(EdgeTtsService ttsService)
    {
        _ttsService = ttsService;
    }

    [HttpGet("speak")]
    [Produces("audio/mpeg")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Speak([FromQuery] string text, [FromQuery] string? voice = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest("Văn bản không được để trống.");

        try
        {
            // Thiết lập headers cho phép trình duyệt phát audio ngay lập tức
            Response.Headers.Append("Content-Type", "audio/mpeg");
            Response.Headers.Append("Cache-Control", "no-cache");
            
            // Stream âm thanh trực tiếp vào response body
            await _ttsService.StreamToResponseAsync(text, Response.Body, voice);
            
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            // Ghi log lỗi (không trả về chi tiết lỗi kỹ thuật cho người dùng)
            // _logger.LogError(ex, "Lỗi Edge TTS");
            return StatusCode(500, "Không thể tạo âm thanh. Vui lòng thử lại sau.");
        }
    }
}
```

### 21.5. Đăng ký DI trong `Program.cs`
```csharp
builder.Services.AddScoped<EdgeTtsService>();
// Nếu dùng TtsController riêng, cần thêm:
builder.Services.AddControllers(); // Hoặc đảm bảo đã có sẵn
```

### 21.6. Cập nhật Giao diện (Frontend)
Thêm nút "Đọc to" (loa) bên cạnh mỗi tin nhắn của AI trong khung chat.

**HTML (thêm vào template tin nhắn AI):**
```html
<button class="btn-speak" onclick="speakText(this)" title="Đọc to">
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"></polygon>
        <path d="M15.54 8.46a5 5 0 0 1 0 7.07"></path>
        <path d="M19.07 4.93a10 10 0 0 1 0 14.14"></path>
    </svg>
</button>
```

**JavaScript (`wwwroot/js/chat-logic.js`):**
```javascript
let currentAudio = null; // Biến toàn cục để quản lý audio đang phát

function speakText(buttonElement) {
    // Lấy nội dung văn bản từ bubble chứa nút
    const bubble = buttonElement.closest('.message-bubble');
    const text = bubble.innerText.trim();
    
    if (!text) return;

    // Nếu đang phát audio khác, dừng lại
    if (currentAudio) {
        currentAudio.pause();
        currentAudio = null;
    }

    // Gọi API TTS
    const voice = 'vi-VN-HoaiMyNeural'; // Có thể lấy từ dropdown
    const audioUrl = `/api/tts/speak?text=${encodeURIComponent(text)}&voice=${voice}`;
    
    currentAudio = new Audio(audioUrl);
    currentAudio.play().catch(err => {
        console.error("Lỗi phát audio:", err);
        alert("Không thể phát âm thanh. Vui lòng thử lại.");
    });

    // Tùy chọn: Đổi icon nút khi đang phát
    buttonElement.classList.add('speaking');
    currentAudio.onended = () => {
        buttonElement.classList.remove('speaking');
        currentAudio = null;
    };
}
```

### 21.7. CSS (Tùy chọn)
Thêm style cho nút loa trong `wwwroot/css/components.css`:
```css
.btn-speak {
    background: transparent;
    border: none;
    color: #94a3b8;
    cursor: pointer;
    padding: 4px;
    border-radius: 4px;
    transition: color 0.2s;
}
.btn-speak:hover {
    color: #3b82f6; /* Neon blue */
}
.btn-speak.speaking {
    color: #22c55e; /* Green */
    animation: pulse 1.5s infinite;
}
@keyframes pulse {
    0% { opacity: 1; }
    50% { opacity: 0.5; }
    100% { opacity: 1; }
}
```

### 21.8. Xử lý Lỗi & Lưu ý
- **Không có âm thanh:** Kiểm tra xem trình duyệt có chặn autoplay không (thường xảy ra nếu audio được kích hoạt mà không có tương tác người dùng). Vì hàm `speakText` được gọi từ sự kiện click, nên thường không bị chặn.
- **Văn bản quá dài:** Edge TTS có thể giới hạn độ dài. Nếu gặp lỗi với văn bản dài, có thể cắt bớt hoặc chia nhỏ.
- **Bảo mật:** Không cần API key, nhưng nên giới hạn tần suất gọi (Rate Limiting) nếu triển khai công khai để tránh bị lạm dụng.
- **Tương thích:** EdgeTtsSharp hoạt động trên mọi nền tảng hỗ trợ .NET Standard 2.1, bao gồm cả khi deploy lên Linux (MonsterASP, Render, v.v.) [citation:4†L14-L15].

### 21.9. Prompt cho AI (Ra lệnh tích hợp)
> "Hãy tích hợp Edge TTS vào dự án ASP.NET Core của tôi. 
> 1. Cài đặt package `EdgeTtsSharp`. 
> 2. Tạo `EdgeTtsService` với phương thức `StreamToResponseAsync(string text, Stream outputStream, string? voiceName)` sử dụng `EdgeTts.GetVoice()` và `voice.StreamText()`. 
> 3. Tạo `TtsController` với action `[HttpGet("speak")]` trả về `audio/mpeg` và stream trực tiếp vào `Response.Body`. 
> 4. Thêm nút "Đọc to" vào mỗi tin nhắn AI trong giao diện chat, viết JavaScript gọi API và phát audio bằng `new Audio(url)`. 
> 5. Đảm bảo đọc giọng mặc định từ `appsettings.json` (mục `EdgeTts:DefaultVoice`), không hardcode."
```