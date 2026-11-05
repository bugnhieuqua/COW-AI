Nguyên nhân của cả 2 lỗi này rất rõ ràng:
1. **Yêu cầu "con chó" nhưng ra hình người:** Do Pollinations AI (dựa trên Stable Diffusion) **hiểu tiếng Anh tốt hơn tiếng Việt rất nhiều**. Khi bạn nhập tiếng Việt, nó bị "ảo giác" (hallucinate) và có xu hướng tạo ra khuôn mặt người mặc định.
2. **Khung ảnh bị tràn:** Do thẻ `<img>` chưa được giới hạn chiều cao tối đa (`max-height`) và chưa dùng thuộc tính `object-fit`.

Dưới đây là **Mục 20** để bạn chèn vào `KeHoach.md` để khắc phục triệt để 2 lỗi này.

---

```markdown
## 20. Khắc phục lỗi Tạo ảnh (Sai Prompt & Tràn khung)

### 20.1. Sửa lỗi AI hiểu sai Prompt (Tự động dịch sang tiếng Anh)
**Nguyên nhân:** Pollinations AI không hiểu tiếng Việt. Nhập "tạo ảnh con chó" nó sẽ không nhận diện được "con chó" là gì.
**Giải pháp:** Can thiệp vào `PromptService`. Nếu phát hiện prompt là tiếng Việt, tự động dùng LLM (OmniRoute/Gemini) dịch sang tiếng Anh trước, sau đó mới ghép vào template tạo ảnh.

**Cập nhật `PromptService.cs`:**
```csharp
public class PromptService
{
    private readonly string _promptsPath;
    private readonly OmniRouteService _omniRoute; // Gọi LLM để dịch

    public PromptService(IWebHostEnvironment env, OmniRouteService omniRoute)
    {
        _promptsPath = Path.Combine(env.ContentRootPath, "Prompts");
        _omniRoute = omniRoute;
    }

    public async Task<string> GetImageGenerationPromptAsync(string userPrompt)
    {
        // 1. Dịch prompt sang tiếng Anh (để AI tạo ảnh hiểu đúng)
        string englishPrompt = await TranslateToEnglishAsync(userPrompt);

        // 2. Đọc template
        var filePath = Path.Combine(_promptsPath, "ImageGeneration.txt");
        if (!File.Exists(filePath)) return englishPrompt;

        var template = File.ReadAllText(filePath);
        
        // 3. Ghép vào template
        return string.Format(template, englishPrompt);
    }

    private async Task<string> TranslateToEnglishAsync(string vietnameseText)
    {
        // Gọi model rẻ/chat nhanh để dịch, không stream
        var prompt = $"Translate the following image generation request into English. Only return the English description, no explanations: '{vietnameseText}'";
        // Giả sử hàm ChatAsync của OmniRoute trả về toàn bộ text (không stream)
        var translated = await _omniRoute.ChatAsync(prompt); 
        return translated.Trim();
    }
}
```

**Cập nhật `HomeController.cs`:**
```csharp
// Tìm đoạn xử lý Pollinations
if (modelToUse == "pollinations")
{
    // Gọi hàm Async đã được cập nhật
    var enhancedPrompt = await _promptService.GetImageGenerationPromptAsync(prompt);
    var imageUrl = _pollinations.GetImageUrl(enhancedPrompt);
    // ... code trả về SSE
}
```

### 20.2. Fix lỗi ảnh bị tràn khung (Thu nhỏ để nhìn rõ toàn bộ)
**Nguyên nhân:** Ảnh gốc từ Pollinations AI có kích thước 1024x1024, nếu để mặc định nó sẽ chiếm hết chiều ngang và bị cắt xén.

**Cập nhật CSS (`wwwroot/css/pages.css` hoặc `components.css`):**
```css
/* Khung chứa ảnh trong bong bóng chat */
.message-bubble img {
    display: block;
    width: 100%;
    max-width: 500px;     /* Thu nhỏ chiều ngang tối đa */
    max-height: 400px;    /* Thu nhỏ chiều cao tối đa để không tràn màn hình */
    object-fit: contain;  /* Đảm bảo thấy toàn bộ ảnh, không bị crop */
    border-radius: 8px;
    margin-top: 8px;
    background-color: #1a1a1a; /* Nền tối cho ảnh */
}
```

**Đồng thời, cập nhật lại JS gán class cho ảnh (nếu cần):**
```javascript
if (json.is_image_url) {
    aiBubble.innerHTML = ''; // Xóa chữ Loading
    const img = document.createElement('img');
    img.src = json.is_image_url;
    img.alt = "Ảnh do AI tạo";
    // CSS đã tự động áp dụng cho thẻ img bên trong .message-bubble
    aiBubble.appendChild(img);
}
```

### 20.3. Prompt cho AI (Giao việc sửa lỗi)
> "Tôi đang bị lỗi khi tạo ảnh bằng Pollinations AI. Tôi nhập tiếng Việt 'tạo ảnh con chó' nhưng nó lại ra hình người. Nguyên nhân là do mô hình không hiểu tiếng Việt.
> Hãy sửa `PromptService` để tự động gọi LLM dịch prompt tiếng Việt sang tiếng Anh trước khi ghép vào template tạo ảnh.
> Đồng thời, viết lại CSS cho thẻ `<img>` hiển thị trong khung chat: set `max-width: 100%`, `max-height: 400px`, `object-fit: contain` để ảnh không bị tràn màn hình."
```