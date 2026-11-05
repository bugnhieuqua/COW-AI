Lỗi bạn gặp phải là do giao diện hiện tại đang được thiết kế theo kiểu **"Form nhập liệu -> Kết quả hiển thị bên dưới"** (giống trang tra cứu), thay vì **"Giao diện Chat"** (như Duck.ai - thanh nhập lệnh trượt xuống đáy, tin nhắn xếp chồng lên nhau).

Để làm được như Duck.ai, chúng ta cần thay đổi cấu trúc HTML/CSS của trang chủ sang dạng **Flexbox (Cột)**, và dùng Javascript để điều khiển việc "di chuyển" thanh nhập lệnh từ giữa màn hình xuống đáy khi bắt đầu chat.

Dưới đây là **bản đầy đủ thay thế cho Mục 14**, hướng dẫn chi tiết từ giao diện (Frontend) đến xử lý (Backend) để tạo ra trải nghiệm chat chuẩn Duck.ai.

---

```markdown
## 14. Xử lý Chat Toàn diện (Giao diện & Logic Streaming)

### 14.1. Repo Tham chiếu (Backend)
- **Tên Repo:** `SwarajRenghe/AiSDK.UIStream.NET`
- **Link:** https://github.com/SwarajRenghe/AiSDK.UIStream.NET
- **Mục đích:** Xử lý định dạng SSE (Server-Sent Events) ở backend ASP.NET Core để stream text từ OpenRouter.

### 14.2. Cấu trúc Giao diện Chat (Giống Duck.ai)
Giao diện cần chia làm 2 trạng thái:
1. **Trạng thái ban đầu (Centered):** Thanh nhập lệnh nằm giữa màn hình, các thẻ gợi ý (Tạo ảnh, Tóm tắt...) nằm bên dưới.
2. **Trạng thái Chat (Bottom):** Khi gửi lệnh, thanh nhập lệnh trượt xuống đáy. Khu vực tin nhắn (Chat History) xuất hiện và chiếm phần lớn diện tích.

**HTML cập nhật cho `Home/Index.cshtml`:**
```html
<div class="chat-layout" id="chatLayout">
    <!-- 1. Khu vực hiển thị tin nhắn (Mặc định ẩn) -->
    <div class="chat-history" id="chatHistory">
        <!-- Tin nhắn sẽ được JS chèn vào đây -->
    </div>

    <!-- 2. Khu vực nhập lệnh (Ban đầu ở giữa, sau đó trượt xuống đáy) -->
    <div class="chat-input-container" id="chatInputContainer">
        <!-- Thẻ gợi ý (Chỉ hiện lúc đầu, ẩn đi khi bắt đầu chat) -->
        <div class="suggestion-cards" id="suggestionCards">
            <div class="suggestion-card">Tạo ảnh: Bò phi hành gia...</div>
            <div class="suggestion-card">Tóm tắt: Quy trình OWASP...</div>
        </div>

        <!-- Thanh Prompt Bar -->
        <div class="prompt-bar-wrapper">
            <div class="prompt-bar">
                <button class="btn-tools">+</button>
                <input type="text" id="chatInput" placeholder="Hỏi bất cứ điều gì..." autocomplete="off" />
                <div class="model-selector-mini" id="currentModelLabel">Gemini 2.0 Flash</div>
                <button class="btn-send" id="btnSend">
                    <svg>...</svg>
                </button>
            </div>
        </div>
    </div>
</div>
```

### 14.3. CSS (Quản lý Layout & Hiệu ứng trượt)
Thêm vào `wwwroot/css/pages.css`:

```css
/* Layout chính chiếm toàn màn hình */
.chat-layout {
    display: flex;
    flex-direction: column;
    height: 100vh;
    padding-top: 60px; /* Chừa chỗ cho Navbar */
}

/* Khu vực tin nhắn */
.chat-history {
    flex: 1;
    overflow-y: auto;
    padding: 20px 10%;
    display: none; /* Ẩn lúc đầu */
    flex-direction: column;
    gap: 24px;
}
.chat-history.active {
    display: flex; /* Hiện khi bắt đầu chat */
}

/* Container của thanh nhập lệnh */
.chat-input-container {
    width: 100%;
    max-width: 800px;
    margin: 0 auto;
    padding: 0 20px 20px;
    transition: all 0.5s cubic-bezier(0.25, 1, 0.5, 1);
    
    /* Trạng thái ban đầu: Nằm giữa màn hình */
    position: absolute;
    top: 50%;
    left: 50%;
    transform: translate(-50%, -50%);
}

/* Trạng thái khi đã vào Chat: Trượt xuống đáy */
.chat-input-container.bottom-mode {
    position: relative;
    top: auto;
    left: auto;
    transform: none;
    margin-top: auto; /* Đẩy xuống đáy */
}

/* Ẩn thẻ gợi ý khi đã vào Chat */
.chat-input-container.bottom-mode .suggestion-cards {
    display: none;
}

/* Giao diện Bong bóng Chat (Chat Bubbles) */
.message {
    display: flex;
    flex-direction: column;
    max-width: 80%;
}
.message.user {
    align-self: flex-end;
    align-items: flex-end;
}
.message.ai {
    align-self: flex-start;
    align-items: flex-start;
}
.message-bubble {
    padding: 12px 16px;
    border-radius: 12px;
    line-height: 1.6;
    font-size: 15px;
}
.message.user .message-bubble {
    background: #2563eb; /* Màu xanh */
    color: white;
    border-bottom-right-radius: 4px;
}
.message.ai .message-bubble {
    background: rgba(255, 255, 255, 0.05);
    border: 1px solid rgba(255, 255, 255, 0.1);
    color: #e2e8f0;
    border-bottom-left-radius: 4px;
}

/* Hiển thị ảnh trong chat */
.message-bubble img {
    max-width: 100%;
    border-radius: 8px;
    margin-top: 8px;
}
```

### 14.4. JavaScript (Xử lý Trượt & Streaming)

```javascript
const chatHistory = document.getElementById('chatHistory');
const inputContainer = document.getElementById('chatInputContainer');
const inputField = document.getElementById('chatInput');
const btnSend = document.getElementById('btnSend');

// Hàm thêm tin nhắn vào giao diện
function appendMessage(role, content, isImage = false) {
    const msgDiv = document.createElement('div');
    msgDiv.className = `message ${role}`;
    
    const bubble = document.createElement('div');
    bubble.className = 'message-bubble';
    
    if (isImage) {
        const img = document.createElement('img');
        img.src = content;
        bubble.appendChild(img);
    } else {
        bubble.innerText = content;
    }
    
    msgDiv.appendChild(bubble);
    chatHistory.appendChild(msgDiv);
    
    // Cuộn xuống tin nhắn mới nhất
    chatHistory.scrollTop = chatHistory.scrollHeight;
    
    return bubble; // Trả về thẻ bubble để update text khi stream
}

// Hàm gửi tin nhắn
async function handleSend() {
    const prompt = inputField.value.trim();
    if (!prompt) return;

    // 1. Chuyển đổi giao diện sang chế độ Chat
    chatHistory.classList.add('active');
    inputContainer.classList.add('bottom-mode');
    
    // 2. Hiển thị tin nhắn của User
    appendMessage('user', prompt);
    inputField.value = '';
    inputField.disabled = true;

    // 3. Tạo khung chờ cho AI
    const aiBubble = appendMessage('ai', 'Đang suy nghĩ...');

    try {
        // Gọi API Backend
        const response = await fetch('/Home/StreamChat', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: `prompt=${encodeURIComponent(prompt)}`
        });

        if (!response.ok) throw new Error('Lỗi mạng');

        // Xử lý Stream
        const reader = response.body.getReader();
        const decoder = new TextDecoder('utf-8');
        let isFirstChunk = true;

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            const chunk = decoder.decode(value, { stream: true });
            const lines = chunk.split('\n');

            for (const line of lines) {
                if (line.startsWith('data: ')) {
                    const data = line.substring(6);
                    if (data === '[DONE]') break;
                    
                    try {
                        const json = JSON.parse(data);
                        const content = json.choices[0]?.delta?.content || '';
                        
                        // Nếu Backend trả về 1 URL ảnh (cho tính năng tạo ảnh)
                        if (json.is_image_url) {
                            aiBubble.innerHTML = '';
                            const img = document.createElement('img');
                            img.src = json.is_image_url;
                            aiBubble.appendChild(img);
                        } 
                        // Nếu là text bình thường
                        else if (content) {
                            if (isFirstChunk) {
                                aiBubble.innerText = ''; // Xóa chữ "Đang suy nghĩ..."
                                isFirstChunk = false;
                            }
                            aiBubble.innerText += content;
                            chatHistory.scrollTop = chatHistory.scrollHeight;
                        }
                    } catch (e) { /* Bỏ qua lỗi parse chunk lẻ */ }
                }
            }
        }
    } catch (error) {
        aiBubble.innerText = 'Đã xảy ra lỗi khi kết nối với AI.';
        aiBubble.style.color = '#ef4444';
    } finally {
        inputField.disabled = false;
        inputField.focus();
    }
}

// Gắn sự kiện Enter và Click
btnSend.addEventListener('click', handleSend);
inputField.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') handleSend();
});
```

### 14.5. Cập nhật Backend (`HomeController.cs`) để hỗ trợ Chat & Ảnh
Backend cần phân tích xem người dùng muốn chat hay tạo ảnh để trả về đúng định dạng Stream.

```csharp
[HttpPost]
public async Task<IActionResult> StreamChat(string prompt)
{
    // (Tùy chọn) Logic phân loại ý định đơn giản dựa vào từ khóa
    if (prompt.Contains("tạo ảnh") || prompt.Contains("vẽ"))
    {
        var imageUrl = _pollinations.GetImageUrl(prompt);
        
        // Trả về định dạng SSE giả lập để Frontend dễ xử lý (1 lần là xong)
        Response.Headers.Append("Content-Type", "text/event-stream");
        var payload = JsonSerializer.Serialize(new { is_image_url = imageUrl });
        await Response.WriteAsync($"data: {payload}\n\n");
        await Response.WriteAsync("data: [DONE]\n\n");
        return new EmptyResult();
    }

    // Nếu là chat text bình thường -> Gọi OpenRouter
    var stream = await _openRouter.StreamChatAsync(prompt);
    
    Response.Headers.Append("Content-Type", "text/event-stream");
    Response.Headers.Append("Cache-Control", "no-cache");
    
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
```

### 14.6. Lưu ý cho AI (Prompt mẫu để fix giao diện)
> "Hãy sửa lại file `Home/Index.cshtml` và `pages.css` của tôi để thanh nhập liệu (Prompt Bar) hoạt động giống Duck.ai:
> 1. Ban đầu, thanh Prompt Bar nằm giữa màn hình theo cả 2 trục (dùng `position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%)`).
> 2. Khi người dùng nhập câu hỏi và nhấn Gửi, thanh này phải trượt mượt mà xuống đáy màn hình (dùng JS thêm class `bottom-mode` để chuyển sang `position: relative; margin-top: auto`).
> 3. Khu vực phía trên thanh nhập liệu phải trở thành khung chat (Chat History), hiển thị các bong bóng chat (User bên phải, AI bên trái). Khi AI trả lời text dài, khung chat tự động cuộn xuống dưới."
```