Dưới đây là **Mục 22** để bạn chèn vào `KeHoach.md`. Mục này sẽ tái cấu trúc lại UI/UX: Đưa tính năng Edge TTS vào chung menu chọn Model (dạng tùy chọn con Nam/Nữ) và cập nhật logic tự động đổi Placeholder.

---

```markdown
## 22. Tái cấu trúc UI: Tích hợp Edge TTS vào Model Selector

### 22.1. Cập nhật Giao diện (HTML)
Xóa bỏ nút "Giọng nam" đang bị tách rời. Đưa Edge TTS vào bên trong Menu chọn Model, chia thành 2 lựa chọn con (Nam/Nữ).

**Cập nhật `Views/Shared/_ModelSelector.cshtml`:**
```html
<div class="model-menu" id="modelMenu" role="listbox" aria-labelledby="modelTrigger">
    <div class="model-menu__header">Chọn Model AI</div>
    
    <!-- (Giữ nguyên các option Auto, Gemini, Llama, DeepSeek, Pollinations...) -->
    
    <!-- Thêm nhóm Edge TTS -->
    <div class="model-menu__divider"></div>
    <div class="model-menu__header">Chuyển văn bản thành giọng nói (TTS)</div>
    
    <div class="model-option" role="option" aria-selected="false" data-value="edge-female">
        <span class="model-option__name">Edge TTS (Giọng Nữ)</span>
        <svg class="model-option__check" ...><path d="M20 6L9 17l-5-5"/></svg>
    </div>
    
    <div class="model-option" role="option" aria-selected="false" data-value="edge-male">
        <span class="model-option__name">Edge TTS (Giọng Nam)</span>
        <svg class="model-option__check" ...><path d="M20 6L9 17l-5-5"/></svg>
    </div>
</div>
```

### 22.2. Cập nhật Logic JavaScript (Đổi Placeholder & Gửi Request)
Khi người dùng chọn các option có `data-value` bắt đầu bằng `edge-`, tự động đổi placeholder của ô nhập liệu.

**Cập nhật `wwwroot/js/site.js` (phần xử lý chọn model):**
```javascript
document.addEventListener('DOMContentLoaded', () => {
    const inputField = document.getElementById('chatInput');
    const options = document.querySelectorAll('.model-option');

    options.forEach(option => {
        option.addEventListener('click', () => {
            // ... (Code cũ: cập nhật selected state, text trên trigger) ...

            // Lấy data-value của model vừa chọn
            const selectedValue = option.getAttribute('data-value');

            // Đổi Placeholder nếu chọn Edge TTS
            if (selectedValue === 'edge-male' || selectedValue === 'edge-female') {
                inputField.placeholder = "Nhập văn bản để chuyển sang giọng nói...";
            } else {
                inputField.placeholder = "Hỏi bất kỳ điều gì hoặc nhập lệnh cho COW.AI...";
            }
        });
    });
});
```

### 22.3. Cập nhật Backend (`HomeController.cs`)
Thêm nhánh xử lý riêng cho `edge-male` và `edge-female`, gọi đến `EdgeTtsService`.

```csharp
[HttpPost]
public async Task<IActionResult> StreamChat(string prompt, string? selectedModel)
{
    try
    {
        // 1. Xử lý Edge TTS (Nếu người dùng chọn)
        if (selectedModel == "edge-male" || selectedModel == "edge-female")
        {
            // Chọn giọng đọc dựa trên model
            string voiceName = selectedModel == "edge-male" ? "vi-VN-NamMinhNeural" : "vi-VN-HoaiMyNeural";
            
            // Gọi EdgeTtsService (Cần inject service này vào Controller)
            // Lưu ý: Phải trả về dạng stream audio thay vì text SSE
            Response.Headers.Append("Content-Type", "audio/mpeg");
            Response.Headers.Append("Cache-Control", "no-cache");
            
            await _edgeTtsService.StreamToResponseAsync(prompt, Response.Body, voiceName);
            
            return new EmptyResult();
        }

        // 2. Xử lý các Model AI khác (Giữ nguyên logic cũ)
        // ... (Auto-routing, OmniRoute, Pollinations...) ...
    }
    catch (Exception ex) { /*...*/ }
}
```

### 22.4. Cập nhật Logic JavaScript (Xử lý phát Audio)
Do Backend trả về `audio/mpeg` thay vì text stream khi chọn Edge TTS, logic `handleSend` cần phân biệt để phát âm thanh.

**Cập nhật `handleSend()` trong `chat-logic.js`:**
```javascript
async function handleSend() {
    const prompt = document.getElementById('chatInput').value;
    const selectedModelId = document.querySelector('.model-option.selected')?.getAttribute('data-value') || 'auto';

    // ... (Code UI chuyển sang chế độ chat, hiển thị bong bóng user) ...

    if (selectedModelId === 'edge-male' || selectedModelId === 'edge-female') {
        // Xử lý riêng cho Edge TTS
        const audioUrl = `/Home/StreamChat?prompt=${encodeURIComponent(prompt)}&selectedModel=${selectedModelId}`;
        const audio = new Audio(audioUrl);
        
        // Hiển thị text "Đang tạo âm thanh..."
        const aiBubble = appendMessage('ai', 'Đang tạo âm thanh...');
        
        audio.play().catch(e => {
            aiBubble.innerText = "Lỗi phát âm thanh.";
        });
        
        audio.onplaying = () => {
            aiBubble.innerHTML = `<i class="icon-volume"></i> Đang phát âm thanh...`;
        };
        audio.onended = () => {
            aiBubble.innerHTML = `[Âm thanh đã phát xong]`;
        };
        
    } else {
        // Xử lý Stream Text cho các model AI khác như cũ
        const response = await fetch('/Home/StreamChat', { /*...*/ });
        // ... (Code đọc stream text) ...
    }
}
```

### 22.5. Prompt cho AI (Ra lệnh sửa UI)
> "Giao diện của tôi đang bị tách rời nút 'Giọng nam' khỏi menu chọn Model. Hãy giúp tôi tái cấu trúc lại:
> 1. Đưa tùy chọn 'Edge TTS (Giọng Nam)' và 'Edge TTS (Giọng Nữ)' vào chung trong menu `.model-menu`. Gắn `data-value="edge-male"` và `data-value="edge-female"`.
> 2. Viết JS để khi người dùng chọn 1 trong 2 option này, ô input `#chatInput` sẽ tự động đổi placeholder thành 'Nhập văn bản để chuyển sang giọng nói...'.
> 3. Sửa lại hàm `handleSend` trong JS: Nếu `selectedModel` là `edge-male` hoặc `edge-female`, không dùng `fetch` đọc stream mà tạo một đối tượng `new Audio('/Home/StreamChat?...')` để phát trực tiếp file MP3 trả về từ server."
```