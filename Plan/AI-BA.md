Dưới đây là bản hoàn chỉnh **gộp chung Mục 11 và Mục 12** (đã tích hợp toàn bộ quy chuẩn bảo mật, tuyệt đối không hardcode key/model, lấy cấu hình từ `appsettings` và biến môi trường). 

Bạn hãy copy toàn bộ nội dung dưới đây và chèn vào file `KeHoach.md` (xóa bỏ các mục 11, 12 cũ nếu đã lỡ lưu).

---

```markdown
## 11. Kiến trúc Backend AI & Chuyển đổi sang ASP.NET Core (Tích hợp Cấu hình Bảo mật)

### 11.1. Repo Tham chiếu Duy nhất
- **Tên Repo:** `arvinpatel-ds/free-ChatGPT`
- **Link:** https://github.com/arvinpatel-ds/free-ChatGPT
- **Lý do chọn:**
  - Là ứng dụng hoàn chỉnh, tích hợp **cả OpenRouter (chat) và Pollinations AI (tạo ảnh)**.
  - Có xử lý **streaming (SSE)** cho chat - một kỹ thuật quan trọng và "ăn điểm" khi làm đồ án.
  - Logic gọi API rõ ràng, dễ dàng trích xuất chuyển sang C#.

### 11.2. Quy chuẩn Quản lý Cấu hình (Tuyệt đối không Hardcode)
Để bảo mật API Key và dễ dàng thay đổi Model, toàn bộ thông số phải được nạp từ `appsettings.json` và **User Secrets / Environment Variables**.

**Bước 1: Cấu trúc `appsettings.json` (Được commit lên Git, không chứa Key)**
```json
{
  "OpenRouter": {
    "BaseUrl": "https://openrouter.ai/api/v1",
    "DefaultModel": "google/gemini-2.0-flash-exp:free",
    "CodeGenModel": "deepseek/deepseek-r1:free",
    "SummaryModel": "meta-llama/llama-3-8b-instruct:free"
  },
  "Pollinations": {
    "BaseUrl": "https://image.pollinations.ai/prompt/"
  }
}
```

**Bước 2: Cấu hình Key qua User Secrets (Môi trường Dev)**
Mở Terminal tại thư mục project, chạy:
```bash
dotnet user-secrets init
dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-v1-xxxxxxxxxxxxx"
```
*(Khi deploy lên host, bạn sẽ thêm biến môi trường `OpenRouter__ApiKey` trên dashboard của host).*

### 11.3. Tạo các Service (Đọc cấu hình qua `IConfiguration`)

**a) `OpenRouterService.cs` (Xử lý Chat & Streaming):**
```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;

public class OpenRouterService
{
    private readonly HttpClient _http;
    private readonly string _defaultModel;

    public OpenRouterService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.BaseAddress = new Uri(config["OpenRouter:BaseUrl"]!);
        
        // Bảo mật: Đọc Key từ User Secrets/Env, báo lỗi nếu thiếu
        var apiKey = config["OpenRouter:ApiKey"] 
            ?? throw new InvalidOperationException("Lỗi: Chưa cấu hình OpenRouter:ApiKey");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Đọc Model mặc định từ cấu hình
        _defaultModel = config["OpenRouter:DefaultModel"] 
            ?? throw new InvalidOperationException("Lỗi: Chưa cấu hình DefaultModel");
    }

    public async Task<Stream> StreamChatAsync(string prompt, string? customModel = null)
    {
        var modelToUse = customModel ?? _defaultModel;

        var payload = new
        {
            model = modelToUse,
            messages = new[] { new { role = "user", content = prompt } },
            stream = true
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadAsStreamAsync();
    }
}
```

**b) `PollinationsService.cs` (Xử lý Tạo ảnh):**
```csharp
public class PollinationsService
{
    private readonly string _baseUrl;

    public PollinationsService(IConfiguration config)
    {
        _baseUrl = config["Pollinations:BaseUrl"] ?? "https://image.pollinations.ai/prompt/";
    }

    public string GetImageUrl(string prompt, int width = 1024, int height = 1024)
    {
        var encodedPrompt = Uri.EscapeDataString(prompt);
        return $"{_baseUrl}{encodedPrompt}?width={width}&height={height}&nologo=true";
    }
}
```

### 11.4. Đăng ký DI trong `Program.cs`
```csharp
builder.Services.AddHttpClient<OpenRouterService>();
builder.Services.AddSingleton<PollinationsService>();
```

### 11.5. Tạo Controller cho Trang chủ (Home)
**`HomeController.cs`:**
```csharp
public class HomeController : Controller
{
    private readonly OpenRouterService _openRouter;
    private readonly PollinationsService _pollinations;

    public HomeController(OpenRouterService openRouter, PollinationsService pollinations)
    {
        _openRouter = openRouter;
        _pollinations = pollinations;
    }

    public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> StreamChat(string prompt, string? model)
    {
        // Model nếu không được truyền từ Dropdown (Mục 10) sẽ dùng DefaultModel từ config
        var stream = await _openRouter.StreamChatAsync(prompt, model);
        
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line != null && line.StartsWith("data: "))
            {
                var data = line.Substring(6);
                if (data == "[DONE]") break;
                await Response.WriteAsync($"data: {data}\n\n");
                await Response.Body.FlushAsync();
            }
        }
        return new EmptyResult();
    }

    [HttpPost]
    public IActionResult GenerateImage(string prompt)
    {
        var imageUrl = _pollinations.GetImageUrl(prompt);
        return Json(new { url = imageUrl });
    }
}
```

### 11.6. Xử lý JavaScript phía Client (Dành cho Trang chủ)
**Gọi Chat (Streaming):**
```javascript
async function startChat() {
    const prompt = document.getElementById('promptInput').value;
    const selectedModel = document.getElementById('hiddenSelectedModel')?.value; // Lấy từ Dropdown ở Mục 10

    const response = await fetch('/Home/StreamChat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=${encodeURIComponent(prompt)}&model=${encodeURIComponent(selectedModel || '')}`
    });
    
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    const chatBox = document.getElementById('chatBox');
    
    while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        const chunk = decoder.decode(value);
        const lines = chunk.split('\n');
        for (const line of lines) {
            if (line.startsWith('data: ')) {
                const data = line.substring(6);
                if (data === '[DONE]') break;
                try {
                    const json = JSON.parse(data);
                    const content = json.choices[0].delta.content || '';
                    chatBox.innerHTML += content;
                } catch (e) {}
            }
        }
    }
}
```

**Gọi Tạo ảnh:**
```javascript
async function generateImage() {
    const prompt = document.getElementById('promptInput').value;
    const response = await fetch('/Home/GenerateImage', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=${encodeURIComponent(prompt)}`
    });
    const data = await response.json();
    document.getElementById('imageResult').src = data.url;
}
```

### 11.7. Checklist trước khi Commit (Tuân thủ Code Sạch)
- [ ] Không có bất kỳ chuỗi `sk-or-v1-...` nào trong mã nguồn C#.
- [ ] Mọi tên model (Gemini, Llama, DeepSeek) đều được lấy từ `IConfiguration` (Mục 11.2) hoặc truyền từ Dropdown (Mục 10).
- [ ] File `secrets.json` không bao giờ xuất hiện trong danh sách commit.
- [ ] File `appsettings.json` được push lên Git không chứa key thật.
```