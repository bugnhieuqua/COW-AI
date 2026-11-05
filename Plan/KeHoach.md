Dưới đây là bản `KeHoach.md` đã được viết lại, **chỉ chọn duy nhất một repo** làm nền tảng giao diện để bạn không phải phân vân. Repo này đáp ứng đúng tiêu chí: giao diện AI hiện đại, dark mode, glassmorphism, và cấu trúc HTML/CSS dễ trích xuất để đưa vào ASP.NET MVC.

---

```markdown
# KẾ HOẠCH TRIỂN KHAI FRONT-END DỰ ÁN COW.AI

## 1. Mục tiêu Thiết kế (UX/UI)
- **Phong cách:** AI-native, tối giản, hiện đại (Dark mode chủ đạo, kết hợp Glassmorphism và điểm nhấn màu Neon).
- **Trọng tâm UX:** "Prompt Bar" (Thanh nhập lệnh) là trung tâm của mọi trang, cho phép chuyển đổi nhanh giữa các tác vụ (Tạo ảnh, Tóm tắt, Viết code).
- **Hiệu ứng:** Sử dụng Three.js để tạo nền 3D động (Particle Network) tạo cảm giác về một hệ thống AI đang hoạt động.

## 2. Repo Giao diện Duy nhất (Bắt buộc sử dụng)

**Repo:** `thakurcoderz/ai-chat-ui`
- **Link:** https://github.com/thakurcoderz/ai-chat-ui
- **Lý do chọn:** Đây là template giao diện chat AI được xây dựng bằng Next.js 14 và Tailwind CSS, có sẵn các hiệu ứng **glassmorphism**, **dark mode**, **animated background blobs**, và **typing indicator** – rất phù hợp với phong cách COW.AI. Cấu trúc HTML/CSS rõ ràng, dễ dàng trích xuất sang ASP.NET MVC.
- **Hướng dẫn Setup cho AI (ChatGPT/Claude/Anti):**
  ```text
  Hãy clone repo: git clone https://github.com/thakurcoderz/ai-chat-ui.git
  Chạy: npm install && npm run dev
  Mở trình duyệt, truy cập localhost:3000.
  Sử dụng DevTools (F12) để copy cấu trúc HTML và CSS của:
    (1) Navbar (có theme switcher)
    (2) Khung chat chính (bao gồm message bubbles, typing indicator)
    (3) Thanh nhập liệu (auto-expanding textarea)
  Chuyển đổi các class Tailwind CSS sang CSS thuần (hoặc giữ nguyên nếu dùng Tailwind CDN) và lưu vào các file:
    - wwwroot/css/components.css (cho Navbar, Buttons, Prompt Bar)
    - wwwroot/css/pages.css (cho layout từng trang)
  ```

## 3. Tích hợp hiệu ứng Three.js (Không cần repo riêng)

Thay vì tải thêm repo, bạn sẽ yêu cầu AI viết trực tiếp một hiệu ứng **Particle Network** đơn giản bằng Three.js. Hiệu ứng này lấy cảm hứng từ repo `aadit2805/drift` (Particle Field Background) nhưng được viết gọn để phù hợp với ASP.NET.

**Yêu cầu cho AI:**
```text
Viết file `wwwroot/js/three-background.js` sử dụng thư viện Three.js (tải qua CDN hoặc npm).
Tạo một trường hạt (particle field) gồm 400 hạt màu trắng, kích thước 0.08, độ trong suốt 0.5.
Các hạt phân bố ngẫu nhiên trong không gian 3D (x: ±30, y: ±20, z: ±15).
Khi khoảng cách giữa hai hạt nhỏ hơn một ngưỡng (ví dụ 5 đơn vị), vẽ một đường kẻ mảnh nối chúng.
Toàn bộ hệ thống xoay chậm theo trục Y và phản ứng nhẹ khi di chuyển chuột.
Gắn hiệu ứng vào thẻ <canvas id="three-canvas"></canvas> với position: fixed; z-index: -1; top: 0; left: 0.
Đảm bảo canvas tự động resize theo cửa sổ trình duyệt.
```

## 4. Cấu trúc Thư mục (Chuẩn ASP.NET Core MVC)

```text
COW-AI/
├── Controllers/
│   ├── HomeController.cs
│   ├── ImageController.cs
│   └── DocumentController.cs
├── Views/
│   ├── Shared/
│   │   ├── _Layout.cshtml         # Chứa canvas Three.js, Navbar, Footer
│   │   └── _ValidationScriptsPartial.cshtml
│   ├── Home/
│   │   └── Index.cshtml           # Landing Page (Hero + Prompt Bar)
│   ├── Image/
│   │   └── Index.cshtml           # Giao diện Tạo ảnh
│   └── Document/
│       ├── Summary.cshtml         # Giao diện Tóm tắt
│       └── CodeGen.cshtml         # Giao diện Viết code
├── wwwroot/
│   ├── css/
│   │   ├── site.css               # CSS toàn cục (Variables, Reset, Typography)
│   │   ├── components.css         # CSS cho Navbar, Buttons, Prompt Bar (trích từ repo)
│   │   └── pages.css              # CSS riêng cho từng trang
│   ├── js/
│   │   ├── site.js
│   │   ├── three-background.js    # Hiệu ứng Three.js (tự viết)
│   │   └── chat-logic.js          # Xử lý logic gọi API (giai đoạn sau)
│   ├── lib/
│   │   └── three/                 # Thư viện Three.js (tải từ CDN hoặc npm)
│   └── images/
│       └── logo.png
└── 

## 5. Kế hoạch Triển khai Giao diện (Views)

### 5.1. `_Layout.cshtml`
- Chứa thẻ `<canvas id="three-canvas"></canvas>` (position fixed, z-index -1).
- **Navbar:** Logo COW.AI, các link "Tạo ảnh", "Tóm tắt", "Viết Code" (lấy style glassmorphism từ repo).
- **Footer:** Đơn giản.

### 5.2. `Home/Index.cshtml` (Landing Page)
- **Hero Section:** Tiêu đề lớn "COW.AI - Trợ lý AI của bạn", phụ đề, **Prompt Bar** lớn ở giữa (lấy từ repo).
- **Tính năng:** 3 thẻ (Cards) giới thiệu các tính năng chính với icon và hiệu ứng hover.
- **Hiệu ứng:** Quả cầu Three.js hiển thị phía sau tiêu đề.

### 5.3. `Image/Index.cshtml` (Trang Tạo ảnh)
- **Layout:** 2 cột (CSS Grid).
  - **Cột trái:** Textarea nhập prompt, nút "Tạo ảnh", bộ đếm "Còn 5/5 lượt hôm nay".
  - **Cột phải:** Khung hiển thị ảnh kết quả (placeholder ban đầu).
- **UX:** Nút chuyển sang trạng thái "Đang tạo..." (loading spinner) khi ấn.

### 5.4. `Document/Summary.cshtml` & `CodeGen.cshtml`
- **Layout:** Tương tự trang Tạo ảnh.
  - **Cột trái:** Vùng kéo thả file (Drag & Drop zone) hoặc Textarea nhập văn bản/yêu cầu.
  - **Cột phải:** Khung hiển thị kết quả.
- **Đối với CodeGen:** Sử dụng thư viện `Prism.js` hoặc `Highlight.js` để tô màu cú pháp code Python.

## 6. Prompt Mẫu cho AI (Copy & Paste)

**Prompt 1: Tạo Layout & CSS cơ bản**
> "Hãy viết file `_Layout.cshtml` cho ASP.NET Core MVC với cấu trúc: Navbar (glassmorphism), một thẻ `<canvas id='three-canvas'>` cố định toàn màn hình, và một thẻ `<main>` chứa `@RenderBody()`. Viết kèm file `site.css` để style cho body (dark mode, font Inter), Navbar (backdrop-filter) và đảm bảo nội dung chính có z-index cao hơn canvas."

**Prompt 2: Tạo Prompt Bar (từ repo)**
> "Dựa trên giao diện của repo `thakurcoderz/ai-chat-ui`, hãy tạo một component 'Prompt Bar' bằng HTML/CSS. Nó là một thanh nhập liệu hình chữ nhật bo tròn, nền tối, có hiệu ứng đổ bóng mờ (glass effect). Bên trong có một icon micro, một input text và một nút gửi (icon mũi tên). Khi focus, viền của thanh này phát sáng màu xanh neon."

**Prompt 3: Viết hiệu ứng Three.js (Particle Network)**
> "Tạo file `wwwroot/js/three-background.js`. Sử dụng thư viện Three.js để tạo một trường hạt (particle field) gồm 400 hạt nhỏ màu trắng, di chuyển chậm và nối với nhau bằng các đường kẻ mảnh khi ở gần. Hiệu ứng phải mượt mà, tự động resize theo cửa sổ trình duyệt và có phản ứng nhẹ khi di chuyển chuột. Gắn nó vào thẻ canvas có id 'three-canvas'."

**Prompt 4: Tạo giao diện Tạo ảnh**
> "Viết file `Views/Image/Index.cshtml`. Tạo một layout 2 cột bằng CSS Grid. Cột trái chứa `textarea` cho prompt, nút 'Generate' và text 'Lượt còn lại: 5'. Cột phải là một `div` có viền nét đứt để hiển thị ảnh. Thêm CSS để khi hover vào nút Generate, nó phát sáng nhẹ."

---
## 8. Quy trình Viết Code & Kiểm soát AI (Tránh Code Bẩn, Commit Bẩn)

Để dự án không bị phình to, rối rắm và khó bảo trì (đặc biệt khi phụ thuộc vào AI để sinh code), bắt buộc phải tuân thủ quy trình làm việc dưới đây trong suốt quá trình code.

### 8.1. Quy trình làm việc "Chia để trị" (Atomic Coding)
- **Không code cả trang cùng lúc:** Khi nhờ AI viết giao diện, hãy chia nhỏ trang đó ra. (Ví dụ: "Viết cho tôi phần Navbar trước", chạy thử OK rồi mới "Viết tiếp phần Prompt Bar").
- **Code đến đâu, test đến đó:** Mỗi khi AI sinh ra một đoạn code HTML/CSS/JS, phải copy vào chạy thử ngay trên trình duyệt (F5). Thấy giao diện hiển thị đúng ý mới chuyển sang bước tiếp theo.
- **Commit ngay khi hoàn thành 1 cụm nhỏ:** Đừng đợi làm xong cả 3 trang mới commit. Làm xong cái Navbar chạy được -> Commit. Làm xong cái Footer chạy được -> Commit. (Điều này giúp bạn dễ dàng quay lại nếu sau này AI làm hỏng code).

### 8.2. Kỷ luật "Săn lùng Code rác" từ AI (AI Code Auditing)
AI (ChatGPT, Claude, Anti) rất giỏi viết code nhưng cũng rất giỏi "nhồi nhét" code thừa. Trước khi nhận code từ AI, bạn phải rà soát và ép AI tuân thủ:
- **Cấm Inline Styles/JS:** Bắt buộc AI phải đưa CSS vào file riêng, JS vào file riêng. Nếu AI tự ý dùng `style="color: red"` hay `onclick="doSomething()"`, hãy yêu cầu nó viết lại.
- **Xóa bỏ Boilerplate thừa:** Khi tạo mới một file `.cshtml`, AI thường sinh ra các thẻ `<html>`, `<head>`, `<body>` vô nghĩa. Bạn phải yêu cầu AI chỉ trả về phần thân (phần nội dung thực sự) để bạn nhét vào `_Layout.cshtml`.
- **Kiểm tra "Dead Code":** Cảnh giác với các đoạn code bị comment out (`<!-- ... -->` hoặc `// ...`). Nếu không dùng nữa, hãy xóa hẳn đi cho sạch file. Đừng để lại các biến rác như `var test = 1;` hay `console.log('debug')`.

### 8.3. Quy tắc Đặt tên chống "Code bẩn"
Đặt tên lung tung là nguyên nhân số 1 khiến code bẩn. Cấm sử dụng các tên như: `abc`, `div1`, `container2`, `test`, `temp`, `doSomething`.
- **HTML/CSS (BEM Standard):** 
  - Khối (Block): `prompt-bar`
  - Phần tử (Element): `prompt-bar__input`, `prompt-bar__button`
  - Trạng thái (Modifier): `prompt-bar__button--active`
- **C# (PascalCase):** Tên Controller (`ImageController`), Tên Action (`GenerateImage`), Tên Class (`OpenRouterService`).
- **JavaScript (camelCase):** Tên biến (`const promptValue = ...`), Tên hàm (`function handleSubmit()`).
- **Ảnh/Icon:** `logo-cowai.svg`, `icon-send.png`, `bg-particles.webp`. Tuyệt đối không dùng tên file tiếng Việt không dấu kiểu `anh_nen_trang.png`.

### 8.4. Quy tắc Commit Code Bẩn (Cấm gộp chung)
Để tránh tình trạng "commit bẩn" (một commit chứa hàng chục file thay đổi không liên quan), hãy áp dụng quy tắc **1 Commit = 1 Việc**:
- ❌ **SAI (Commit bẩn):** Commit một lần với message "update code" chứa: sửa navbar, thêm trang tạo ảnh, thêm logo, sửa lỗi font chữ.
- ✅ **ĐÚNG (Commit sạch):** 
  - `style: thêm logo cowai vào navbar` (Chỉ commit file `_Layout.cshtml` và ảnh logo).
  - `feat: tạo khung giao diện trang tạo ảnh` (Chỉ commit file `Image/Index.cshtml`).
  - `fix: sửa lỗi font chữ bị lệch` (Chỉ commit file `site.css`).

### 8.5. Checklist "Code sạch" trước khi gõ lệnh Git Commit
Hãy tự hỏi bản thân 5 câu này trước khi commit:
1. [ ] File code có bị thừa khoảng trắng (whitespace) hay không? (Dùng `Ctrl + K, Ctrl + D` để format).
2. [ ] Có còn file ảnh nháp nào tải từ AI về mà không dùng đến không? (Xóa khỏi thư mục `wwwroot/images`).
3. [ ] Đã xóa hết các dòng `console.log()`, `alert()` và các đoạn code bị comment vô nghĩa chưa?
4. [ ] File `appsettings.json` có vô tình chứa API Key thật không? (Phải dùng `secrets.json` hoặc biến môi trường).
5. [ ] Tên class CSS/JS đã đúng chuẩn và có nghĩa chưa? (Không có tên `div1`, `test`).

*Kỷ luật thép: Thà chậm 5 phút dọn dẹp code, còn hơn mất 5 tiếng để sửa lỗi rác do AI sinh ra.*

---

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

---

## 13. Quản lý Trạng thái Giao diện (UI State Management) & Fix lỗi Logic

### 13.1. Quy tắc chung về Trạng thái (4 States)
Mọi trang (Trang chủ, Tạo ảnh, Tóm tắt, Viết code) bắt buộc phải quản lý giao diện qua 4 trạng thái. **Tuyệt đối không hiển thị Spinner (vòng xoay) hay khung kết quả khi người dùng chưa bấm nút.**
1.  **Empty (Mặc định):** Chỉ hiện Form nhập liệu. Khung kết quả bị ẩn (hoặc hiển thị text mờ "Chưa có dữ liệu").
2.  **Loading:** Kích hoạt khi bấm nút. Hiển thị Spinner trong khung kết quả. Vô hiệu hóa nút bấm.
3.  **Success:** Hiển thị nội dung (Text/Ảnh/Code). Bật lại nút bấm. (Đối với ảnh, chỉ hiện nút "Tải ảnh về" lúc này).
4.  **Error:** Hiển thị thông báo lỗi màu đỏ (VD: "Mất kết nối, vui lòng thử lại").

### 13.2. Fix Trang chủ (`Home/Index.cshtml`)
**Yêu cầu:** Bỏ các khung (Gemini, Tác phẩm...) đang bị hardcode sẵn. Thêm hiệu ứng cuộn xuống khi AI bắt đầu trả lời.

**HTML (Chỉ giữ lại khung chat và khu vực kết quả ẩn):**
```html
<!-- Ô nhập Prompt Bar (Giữ nguyên) -->
<div class="prompt-bar">...</div>

<!-- Khung chứa kết quả (Mặc định ẩn) -->
<div id="chatResultWrapper" class="chat-result-wrapper hidden">
    <!-- Header kết quả (Sẽ cập nhật tên model bằng JS) -->
    <div class="result-header">
        <span class="model-name" id="activeModelName">Hệ thống AI</span>
        <button class="btn-copy">Sao chép</button>
    </div>
    <!-- Nội dung AI trả về (Stream text sẽ nhảy vào đây) -->
    <div id="chatResponseContent" class="response-content"></div>
</div>
```

**CSS (Thêm vào `pages.css`):**
```css
.hidden { display: none !important; }
.chat-result-wrapper {
    margin-top: 32px;
    background: rgba(20, 20, 25, 0.6);
    border: 1px solid rgba(255,255,255,0.1);
    border-radius: 12px;
    padding: 24px;
}
.response-content {
    white-space: pre-wrap; /* Giữ nguyên định dạng xuống dòng */
    line-height: 1.6;
}
```

**JS (Cập nhật trong `chat-logic.js`):**
```javascript
async function handleChatSubmit() {
    const prompt = document.getElementById('promptInput').value;
    if (!prompt) return;

    const wrapper = document.getElementById('chatResultWrapper');
    const content = document.getElementById('chatResponseContent');
    const modelLabel = document.getElementById('activeModelName');
    
    // 1. Chuyển sang trạng thái Loading
    content.innerHTML = '<span class="spinner"></span> Đang suy nghĩ...';
    modelLabel.textContent = document.getElementById('currentModelLabel').textContent;
    
    // 2. Hiển thị khung kết quả và cuộn xuống mượt mà
    wrapper.classList.remove('hidden');
    wrapper.scrollIntoView({ behavior: 'smooth', block: 'start' });

    try {
        // Gọi API (giả lập)
        const response = await fetch('/Home/StreamChat', { /*...*/ });
        
        // 3. Trạng thái Success (Xóa loading, bắt đầu stream text)
        content.innerHTML = ''; 
        // ... Code đọc stream ...
        
    } catch (error) {
        // 4. Trạng thái Error
        content.innerHTML = '<span style="color: red;">Lỗi kết nối API.</span>';
    }
}
```

### 13.3. Fix Trang Tạo ảnh (`Image/Index.cshtml`)
**Lỗi trước đây:** Mới vào trang đã hiện spinner "AI đang kiến tạo...". 

**HTML khung kết quả mới:**
```html
<div class="result-panel">
    <!-- Trạng thái Empty (Mặc định) -->
    <div id="imageEmptyState" class="empty-state">
        <p>Chưa có ảnh nào được tạo.</p>
        <p class="sub-text">Hãy nhập mô tả và bấm "Tạo ảnh ngay".</p>
    </div>

    <!-- Trạng thái Loading (Ẩn mặc định) -->
    <div id="imageLoadingState" class="loading-state hidden">
        <span class="spinner"></span>
        <p>AI đang kiến tạo từng nét vẽ...</p>
    </div>

    <!-- Trạng thái Success (Ẩn mặc định) -->
    <div id="imageSuccessState" class="success-state hidden">
        <img id="generatedImage" src="" alt="Ảnh do AI tạo">
    </div>
</div>
```

**JS xử lý:**
```javascript
async function generateImage() {
    const prompt = document.getElementById('promptInput').value;
    if (!prompt) return alert('Vui lòng nhập mô tả!');

    // Đổi State: Ẩn Empty, Hiện Loading
    document.getElementById('imageEmptyState').classList.add('hidden');
    document.getElementById('imageSuccessState').classList.add('hidden');
    document.getElementById('imageLoadingState').classList.remove('hidden');
    
    // Vô hiệu hóa nút
    const btn = document.getElementById('btnGenerate');
    btn.disabled = true;

    try {
        const response = await fetch('/Home/GenerateImage', { /*...*/ });
        const data = await response.json();
        
        // Đổi sang State Success
        document.getElementById('imageLoadingState').classList.add('hidden');
        document.getElementById('imageSuccessState').classList.remove('hidden');
        
        const img = document.getElementById('generatedImage');
        img.src = data.url;
        
        // Đợi ảnh load xong mới bật lại nút
        img.onload = () => { btn.disabled = false; };
    } catch (e) {
        alert('Lỗi khi tạo ảnh!');
        document.getElementById('imageLoadingState').classList.add('hidden');
        document.getElementById('imageEmptyState').classList.remove('hidden');
        btn.disabled = false;
    }
}
```

### 13.4. Áp dụng tương tự cho Trang Tóm tắt & Viết Code
- Tương tự như trang Tạo ảnh, khung kết quả bên phải ban đầu phải là `Empty State`.
- Khi bấm "Tóm tắt" / "Viết code", mới chuyển sang `Loading State`.
- Sử dụng kỹ thuật Streaming (như trang chủ) để chữ hiện dần ra, mang lại cảm giác AI đang gõ phím thời gian thực.

### 13.5. Prompt cho AI (Copy & Paste)
> "Hãy viết lại HTML/JS cho trang Tạo ảnh và Trang chủ dựa trên nguyên tắc Quản lý trạng thái (UI State).
> **Đối với Trang chủ:** Ô chat giữ nguyên. Khi bấm gửi, hãy dùng JS để `scrollIntoView` (cuộn mượt) xuống một khung kết quả mới xuất hiện (ban đầu khung này bị ẩn `display: none`).
> **Đối với Trang Tạo ảnh:** Ẩn tất cả các khung loading và ảnh mặc định. Chỉ khi click nút 'Tạo ảnh', đổi text nút thành 'Đang tạo...', hiện spinner trong khung kết quả. Khi có kết quả, ẩn spinner và hiện thẻ `<img>` với nguồn ảnh vừa tạo. Không hiển thị thẻ img trống hoặc icon ảnh lỗi."

---

## 19. Quản lý Prompt Tập trung & Tối ưu Prompt cho Pollinations AI

### 19.1. Tạo Thư mục Quản lý Prompt (Không Hardcode)
- **File mẫu:** `Prompts/ImageGeneration.txt`
```text
A masterpiece, best quality, ultra-detailed, 8k resolution, highly detailed image of {0}. 
Professional lighting, sharp focus, vibrant colors, trending on artstation, cinematic composition.
```

### 19.2. `PromptService.cs` (Đọc file Prompt & Tối ưu)
- Tự động đọc template từ `Prompts/ImageGeneration.txt`.
- Làm sạch từ khóa thừa của người dùng (`hãy vẽ cho tôi`, `tạo ảnh`, `vẽ tranh`,...).
- Ghép prompt đã làm sạch vào template `{0}` chất lượng cao.

### 19.3. Tích hợp vào `HomeController.cs` & `ImageController.cs`
- Khi người dùng gửi yêu cầu tạo ảnh (dù qua chọn model `pollinations` hay nhập lệnh tự nhiên `tạo ảnh...`), hệ thống đi qua `PromptService.GetImageGenerationPrompt(prompt)` trước khi chuyển đến `PollinationsService`.

### 19.4. Cấu hình DI & Xuất bản (.csproj)
- Đăng ký `builder.Services.AddSingleton<PromptService>();` trong `Program.cs`.
- Thêm `<Content Include="Prompts\**" CopyToOutputDirectory="PreserveNewest" />` trong `COW-AI.csproj`.

---

## 20. Khắc phục lỗi Tạo ảnh (Sai Prompt & Tràn khung)

### 20.1. Sửa lỗi AI hiểu sai Prompt (Tự động dịch sang tiếng Anh)
**Nguyên nhân:** Pollinations AI không hiểu tiếng Việt. Nhập "tạo ảnh con chó" nó sẽ không nhận diện được "con chó" là gì và bị ảo giác ra hình người.
**Giải pháp:** Can thiệp vào `PromptService`. Nếu phát hiện prompt là tiếng Việt, tự động dùng LLM (OpenRouter/Gemini) dịch sang tiếng Anh trước, sau đó mới ghép vào template tạo ảnh.

**Cập nhật `PromptService.cs`:**
```csharp
public class PromptService
{
    private readonly string _promptsPath;
    private readonly OpenRouterService _openRouter; // Gọi LLM để dịch
    private readonly ILogger<PromptService>? _logger;

    public PromptService(IWebHostEnvironment env, OpenRouterService openRouter, ILogger<PromptService>? logger = null)
    {
        _promptsPath = Path.Combine(env.ContentRootPath, "Prompts");
        _openRouter = openRouter;
        _logger = logger;
    }

    public async Task<string> GetImageGenerationPromptAsync(string userPrompt, CancellationToken cancellationToken = default)
    {
        var cleanPrompt = CleanUserIntent(userPrompt);
        string englishPrompt = cleanPrompt;

        // 1. Dịch prompt sang tiếng Anh nếu phát hiện tiếng Việt (để AI tạo ảnh hiểu đúng)
        if (ContainsVietnamese(cleanPrompt))
        {
            englishPrompt = await TranslateToEnglishAsync(cleanPrompt, cancellationToken);
        }

        // 2. Đọc template
        var filePath = Path.Combine(_promptsPath, "ImageGeneration.txt");
        if (!File.Exists(filePath)) return englishPrompt;

        var template = File.ReadAllText(filePath);
        
        // 3. Ghép vào template
        return string.Format(template, englishPrompt);
    }

    private async Task<string> TranslateToEnglishAsync(string vietnameseText, CancellationToken cancellationToken = default)
    {
        // Gọi model rẻ/chat nhanh để dịch, không stream
        var prompt = $"Translate the following image generation request into English for an AI image generator (Stable Diffusion). Only return the concise English visual description, no explanations: '{vietnameseText}'";
        var translated = await _openRouter.ChatAsync(prompt, "gemini", cancellationToken); 
        return translated.Trim();
    }
}
```

**Cập nhật `HomeController.cs` & `ImageController.cs`:**
```csharp
// Tìm đoạn xử lý Pollinations
if (DetectImageIntent(prompt, model, out var cleanPrompt))
{
    // Gọi hàm Async đã được cập nhật
    var enhancedPrompt = await _promptService.GetImageGenerationPromptAsync(cleanPrompt, cancellationToken);
    var imageUrl = _pollinations.GetImageUrl(enhancedPrompt);
    // ... code trả về SSE
}
```

### 20.2. Fix lỗi ảnh bị tràn khung (Thu nhỏ để nhìn rõ toàn bộ)
**Nguyên nhân:** Ảnh gốc từ Pollinations AI có kích thước 1024x1024, nếu để mặc định nó sẽ chiếm hết chiều ngang và bị cắt xén.

**Cập nhật CSS (`wwwroot/css/pages.css` hoặc `components.css`):**
```css
/* Khung chứa ảnh trong bong bóng chat (Mục 20.2) */
.message-bubble img,
.chat-bubble-ai img,
.ai-generated-image,
.ai-chat-image {
    display: block;
    width: 100%;
    max-width: 500px;     /* Thu nhỏ chiều ngang tối đa */
    max-height: 400px;    /* Thu nhỏ chiều cao tối đa để không tràn màn hình */
    object-fit: contain;  /* Đảm bảo thấy toàn bộ ảnh, không bị crop */
    border-radius: 8px;
    margin-top: 8px;
    background-color: #1a1a1a; /* Nền tối cho ảnh */
    border: 1px solid var(--cow-glass-border);
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.25);
    transition: transform 0.25s ease, box-shadow 0.25s ease;
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

### 20.3. Đã hoàn thành và kiểm thử
- Tự động nhận diện prompt tiếng Việt và dịch sang tiếng Anh qua `OpenRouterService.ChatAsync` trước khi ghép vào template.
- Giới hạn kích thước khung ảnh chat bubble không tràn màn hình (`max-width: 500px`, `max-height: 400px`, `object-fit: contain`).
- Đăng ký DI `PromptService` là `Transient` phù hợp với `OpenRouterService` typed HttpClient.