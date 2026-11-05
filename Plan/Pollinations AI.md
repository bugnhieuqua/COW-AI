
```markdown
## 19. Quản lý Prompt Tập trung & Tối ưu Prompt cho Pollinations AI

### 19.1. Tạo Thư mục Quản lý Prompt (Không Hardcode)
Để dễ dàng tinh chỉnh prompt (thêm bớt từ khóa chất lượng, phong cách) mà không cần sửa code C#, chúng ta sẽ tách toàn bộ Prompt ra file văn bản riêng.

**Tạo thư mục ở gốc dự án:** `Prompts/`
**Tạo file:** `Prompts/ImageGeneration.txt`

**Nội dung file `ImageGeneration.txt`:**
```text
A masterpiece, best quality, ultra-detailed, 8k resolution, highly detailed image of {0}. 
Professional lighting, sharp focus, vibrant colors, trending on artstation, cinematic composition.
```
*(Giải thích: `{0}` là nơi hệ thống sẽ chèn yêu cầu thực tế của người dùng vào.)*

### 19.2. Xây dựng `PromptService.cs` (Đọc file Prompt)
Service này chịu trách nhiệm đọc nội dung file `.txt` và thay thế các biến (như `{0}`) bằng tham số truyền vào.

**Tạo file `Services/PromptService.cs`:**
```csharp
public class PromptService
{
    private readonly string _promptsPath;

    public PromptService(IWebHostEnvironment env)
    {
        // Trỏ đến thư mục Prompts ở gốc dự án
        _promptsPath = Path.Combine(env.ContentRootPath, "Prompts");
    }

    public string GetImageGenerationPrompt(string userPrompt)
    {
        var filePath = Path.Combine(_promptsPath, "ImageGeneration.txt");
        
        if (!File.Exists(filePath))
            return userPrompt; // Fallback nếu không tìm thấy file

        var template = File.ReadAllText(filePath);
        
        // Lọc bỏ các từ ngữ giao tiếp thừa của người dùng (VD: "Hãy vẽ cho tôi...", "Tạo ảnh...")
        // Chỉ giữ lại phần mô tả cốt lõi để AI tạo ảnh hiểu đúng ý.
        var cleanPrompt = CleanUserIntent(userPrompt);

        // Thay thế {0} bằng prompt đã được làm sạch
        return string.Format(template, cleanPrompt);
    }

    private string CleanUserIntent(string prompt)
    {
        // Loại bỏ các cụm từ yêu cầu thông thường để AI không bị rối
        string[] fillers = { "hãy vẽ cho tôi", "tạo ảnh", "vẽ một bức ảnh", "tạo cho tôi một bức ảnh", "hãy tạo một bức ảnh" };
        var clean = prompt.ToLower();
        
        foreach (var filler in fillers)
        {
            clean = clean.Replace(filler, "");
        }

        return clean.Trim();
    }
}
```

### 19.3. Tích hợp vào Luồng Xử lý (`HomeController.cs`)
Khi người dùng yêu cầu tạo ảnh, Backend sẽ không gửi thẳng prompt thô của người dùng cho Pollinations, mà sẽ đi qua `PromptService` trước.

**Cập nhật `HomeController.cs`:**
```csharp
// 1. Inject PromptService vào Constructor
private readonly PromptService _promptService;

public HomeController(..., PromptService promptService)
{
    // ...
    _promptService = promptService;
}

// 2. Sử dụng trong action StreamChat
[HttpPost]
public async Task<IActionResult> StreamChat(string prompt, string? selectedModel)
{
    // ...
    // Nếu là Tạo ảnh (Pollinations)
    if (modelToUse == "pollinations")
    {
        // Lấy prompt đã được tối ưu từ file .txt
        var enhancedPrompt = _promptService.GetImageGenerationPrompt(prompt);
        
        // Gọi Pollinations với prompt đã được bơm "mắm muối" (8k, masterpiece...)
        var imageUrl = _pollinations.GetImageUrl(enhancedPrompt);
        
        Response.Headers.Append("Content-Type", "text/event-stream");
        var payload = System.Text.Json.JsonSerializer.Serialize(new { is_image_url = imageUrl });
        await Response.WriteAsync($"data: {payload}\n\n");
        await Response.WriteAsync("data: [DONE]\n\n");
        return new EmptyResult();
    }
    // ...
}
```

### 19.4. Đăng ký DI trong `Program.cs`
```csharp
builder.Services.AddSingleton<PromptService>();
```

### 19.5. Lưu ý Bảo mật & Tối ưu
- **Thư mục `Prompts`:** Đảm bảo thư mục này được copy vào thư mục Output khi build (trong `.csproj`, thêm `<Content Include="Prompts\**" CopyToOutputDirectory="PreserveNewest" />`).
- **Ngôn ngữ:** Pollinations AI (dựa trên Stable Diffusion) hiểu tiếng Anh tốt hơn rất nhiều so với tiếng Việt. Mặc dù template có thể nhận tiếng Việt, nhưng để có kết quả tốt nhất, bạn có thể thêm một bước dùng LLM (như Gemini) để dịch prompt của người dùng sang tiếng Anh trước khi đưa vào template. Tuy nhiên, với template "mắm muối" ở trên, kết quả tạo ảnh bằng tiếng Việt vẫn sẽ được cải thiện đáng kể.
- **Tránh lỗi URL:** Hàm `GetImageUrl` trong `PollinationsService` vẫn phải dùng `Uri.EscapeDataString(enhancedPrompt)` để đảm bảo các ký tự đặc biệt (dấu phẩy, khoảng trắng) không làm hỏng URL.

### 19.6. Prompt cho AI (Ra lệnh thiết lập)
> "Hãy tạo một `PromptService` trong ASP.NET Core để đọc nội dung từ file `Prompts/ImageGeneration.txt` ở gốc dự án. File này chứa template: `A masterpiece, best quality, ultra-detailed, 8k resolution, highly detailed image of {0}. Professional lighting, sharp focus`. 
> 1. Viết hàm nhận vào `userPrompt`, lọc bỏ các từ thừa như 'tạo ảnh', 'vẽ cho tôi', sau đó dùng `string.Format` để chèn vào template.
> 2. Cập nhật `HomeController` để khi gọi Pollinations AI, sử dụng prompt đã qua xử lý từ `PromptService` thay vì dùng trực tiếp prompt của người dùng. 
> 3. Hướng dẫn tôi cách thêm file `.txt` vào `.csproj` để nó được copy khi publish."
```