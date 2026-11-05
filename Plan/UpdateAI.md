Dưới đây là **Mục 18** để bạn bổ sung vào `KeHoach.md`. Mục này giải quyết triệt để 3 yêu cầu của bạn: **Bổ sung OmniRoute & DuckDuckGo vào UI**, **Xóa các dòng mô tả (span)**, và **Tối ưu CSS để không tràn màn hình**.

---

```markdown
## 18. Cập nhật Giao diện Model Selector (Tối ưu không gian & Bổ sung OmniRoute/DuckDuckGo)

### 18.1. Cập nhật HTML (Xóa mô tả, thêm OmniRoute & DuckDuckGo)
- **Xóa bỏ:** Các thẻ `<span class="model-option__desc">` (dòng mô tả dưới tên model).
- **Bổ sung:** Thêm `OmniRoute` (dùng để định tuyến tự động tổng hợp) và `DuckDuckGo` (dùng để tra cứu web).

**`Views/Shared/_ModelSelector.cshtml`:**
```html
<div class="model-menu" id="modelMenu" role="listbox" aria-labelledby="modelTrigger">
    <div class="model-menu__header">Chọn Model AI</div>
    
    <!-- Tùy chọn Auto -->
    <div class="model-option selected" role="option" aria-selected="true" data-value="auto">
        <span class="model-option__name">Auto (Tự động)</span>
        <svg class="model-option__check" ...><path d="M20 6L9 17l-5-5"/></svg>
    </div>

    <!-- Các Model AI -->
    <div class="model-option" role="option" aria-selected="false" data-value="gemini">
        <span class="model-option__name">Gemini 2.0 Flash</span>
        <svg class="model-option__check" ...><path d="M20 6L9 17l-5-5"/></svg>
    </div>
    <div class="model-option" role="option" aria-selected="false" data-value="llama3">
        <span class="model-option__name">Llama 3 8B</span>
        <svg class="model-option__check" ...><path d="M20 6L9 17l-5-5"/></svg>
    </div>
    <div class="model-option" role="option" aria-selected="false" data-value="deepseek">
        <span class="model-option__name">DeepSeek R1</span>
        <svg class="model-option__check" ...><path d="M20 6L9 17l-5-5"/></svg>
    </div>
    <div class="model-option" role="option" aria-selected="false" data-value="pollinations">
        <span class="model-option__name">Pollinations AI</span>
        <svg class="model-option__check" ...><path d="M20 6L9 17l-5-5"/></svg>
    </div>
    
    <!-- Bổ sung OmniRoute & DuckDuckGo -->
    <div class="model-option" role="option" aria-selected="false" data-value="omniroute">
        <span class="model-option__name">OmniRoute</span>
        <svg class="model-option__check" ...><path d="M20 6L9 17l-5-5"/></svg>
    </div>
    <div class="model-option" role="option" aria-selected="false" data-value="duckduckgo">
        <span class="model-option__name">DuckDuckGo Search</span>
        <svg class="model-option__check" ...><path d="M20 6L9 17l-5-5"/></svg>
    </div>
</div>
```

### 18.2. Tối ưu CSS (Chống tràn màn hình, thu gọn kích thước)
Thêm hoặc cập nhật vào `wwwroot/css/components.css`:

```css
/* Thu nhỏ không gian tổng thể của Menu */
.model-menu {
    position: absolute;
    bottom: calc(100% + 8px);
    left: 0;
    width: 240px; /* Thu hẹp chiều ngang */
    max-height: 320px; /* Chiều cao tối đa, chống tràn màn hình */
    overflow-y: auto; /* Thêm thanh cuộn nếu có quá nhiều model */
    background: rgba(20, 20, 25, 0.95);
    backdrop-filter: blur(16px);
    border: 1px solid rgba(255, 255, 255, 0.1);
    border-radius: 10px;
    padding: 6px; /* Giảm padding tổng */
    z-index: 1000;
    
    opacity: 0;
    visibility: hidden;
    transform: translateY(10px) scale(0.98);
    transition: all 0.2s ease;
}

/* Tối ưu thanh cuộn cho gọn */
.model-menu::-webkit-scrollbar { width: 6px; }
.model-menu::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.2); border-radius: 4px; }

/* Từng dòng Option */
.model-option {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 8px 10px; /* Thu gọn padding */
    border-radius: 6px;
    cursor: pointer;
    transition: background 0.2s;
    margin-bottom: 2px;
}
.model-option:hover { background: rgba(255, 255, 255, 0.08); }
.model-option.selected { background: rgba(59, 130, 246, 0.15); }

/* Chỉ giữ lại tên Model, thu nhỏ font */
.model-option__name { 
    font-size: 13px; 
    font-weight: 500; 
    color: #f8fafc; 
}

/* Ẩn icon check mặc định, chỉ hiện khi selected */
.model-option__check { 
    color: var(--neon-blue, #3b82f6); 
    opacity: 0; 
    width: 14px; 
    height: 14px;
}
.model-option.selected .model-option__check { opacity: 1; }
```

### 18.3. Cập nhật Logic Backend (`HomeController.cs`)
Vì đã thêm 2 tùy chọn mới vào UI, hàm `StreamChat` cần xử lý thêm 2 trường hợp này:

```csharp
// ... (bên trong hàm StreamChat)

// Nếu người dùng chọn thủ công OmniRoute hoặc DuckDuckGo
if (modelToUse == "omniroute")
{
    // Dùng OmniRoute mặc định (không qua auto-route)
    var stream = await _omniRoute.StreamChatAsync(prompt);
    // ... (Code stream trả về client)
    return new EmptyResult();
}
else if (modelToUse == "duckduckgo")
{
    // Chỉ tra cứu web và trả về kết quả dạng Text
    var searchResults = await _duckDuckGo.SearchAsync(prompt);
    
    // Giả lập stream trả về text trực tiếp (không qua AI)
    Response.Headers.Append("Content-Type", "text/event-stream");
    var fakeJson = System.Text.Json.JsonSerializer.Serialize(new { 
        choices = new[] { new { delta = new { content = searchResults } } } 
    });
    await Response.WriteAsync($"data: {fakeJson}\n\n");
    await Response.WriteAsync("data: [DONE]\n\n");
    return new EmptyResult();
}
// ... (Các trường hợp gemini, llama3, deepseek giữ nguyên)
```

### 18.4. Prompt cho AI (Ra lệnh cập nhật giao diện)
> "Hãy cập nhật file `_ModelSelector.cshtml` và `components.css` của tôi:
> 1. Thêm 2 option mới vào danh sách model là 'OmniRoute' (data-value='omniroute') và 'DuckDuckGo Search' (data-value='duckduckgo').
> 2. Xóa toàn bộ các thẻ `<span class="model-option__desc">` (dòng mô tả) trong menu.
> 3. Sửa CSS của `.model-menu`: set `width: 240px`, `max-height: 320px`, `overflow-y: auto` để chống tràn màn hình.
> 4. Giảm padding của `.model-option` xuống `8px 10px` và font-size của tên model xuống `13px`. Ẩn thanh cuộn mặc định của trình duyệt và thay bằng thanh cuộn mỏng."
```