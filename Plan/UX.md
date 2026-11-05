Dưới đây là **Mục 13** (và các mục con) để bạn bổ sung vào `KeHoach.md`. Mục này giải quyết triệt để lỗi logic hiển thị UI: **Không hiện trạng thái loading/placeholder khi chưa có tương tác**, đồng thời thêm hiệu ứng cuộn trang mượt mà cho Trang chủ.
---

```markdown
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
**Lỗi hiện tại (Ảnh 2):** Mới vào trang đã hiện spinner "AI đang kiến tạo...". 

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
```