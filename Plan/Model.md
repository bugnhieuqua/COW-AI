
```markdown
## 10. Giao diện Lựa chọn Model (AI Model Selector Dropdown)

> 🖼️ **Hình ảnh tham chiếu:** *[Người dùng chèn ảnh chụp màn hình giao diện Sakana.ai tại đây để làm mẫu thiết kế]*

### 10.1. Phân tích Thiết kế (Dựa trên Sakana.ai)
- **Vị trí:** Đặt ở góc dưới bên trái của khung chat/khung nhập lệnh.
- **Trạng thái đóng (Trigger):** Nút bấm dạng "viên thuốc" (pill) hiển thị tên model hiện tại (VD: `Gemini 2.0 Flash`) kèm icon mũi tên.
- **Trạng thái mở (Popover):** Menu xổ lên trên. Nền tối bán trong suốt (glassmorphism), viền mờ.
- **Cấu trúc Menu:**
  - **Tiêu đề nhóm:** Chữ "Model" in hoa, nhỏ, màu xám.
  - **Các tùy chọn (Options):** Hiển thị đúng các model dự án đang dùng (Gemini, Llama 3, DeepSeek). Gồm Tên Model (in đậm) và Mô tả ngắn (màu xám nhạt). Model đang chọn sẽ có dấu `✓`.
  - **Cài đặt phụ (Sub-settings):** Các dòng như "Temperature" (Độ sáng tạo) và "Max Tokens" (Độ dài tối đa) kèm mũi tên `>`.

### 10.2. Cấu trúc HTML/CSS/JS (Chuẩn hóa cho ASP.NET)

**HTML (Chèn trong `_Layout.cshtml` hoặc `Views/Shared/_ModelSelector.cshtml`):**
```html
<div class="model-selector" id="modelSelector">
    <!-- Trigger (Nút bấm) -->
    <button class="model-trigger" id="modelTrigger" aria-haspopup="listbox" aria-expanded="false">
        <span class="model-trigger__label" id="currentModelLabel">Gemini 2.0 Flash</span>
        <svg class="model-trigger__chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 9l6 6 6-6"/></svg>
    </button>

    <!-- Menu (Popover) -->
    <div class="model-menu" id="modelMenu" role="listbox" aria-labelledby="modelTrigger">
        <div class="model-menu__header">Chọn Model AI</div>
        
        <!-- Danh sách Model Dự án COW.AI -->
        <div class="model-option selected" role="option" aria-selected="true" data-value="gemini">
            <div class="model-option__info">
                <span class="model-option__name">Gemini 2.0 Flash</span>
                <span class="model-option__desc">Tốc độ siêu nhanh, đa năng </span>
            </div>
            <svg class="model-option__check" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 6L9 17l-5-5"/></svg>
        </div>
        
        <div class="model-option" role="option" aria-selected="false" data-value="llama3">
            <div class="model-option__info">
                <span class="model-option__name">Llama 3 8B Instruct</span>
                <span class="model-option__desc">Mạnh mẽ về tóm tắt văn bản </span>
            </div>
            <svg class="model-option__check" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 6L9 17l-5-5"/></svg>
        </div>

        <div class="model-option" role="option" aria-selected="false" data-value="deepseek">
            <div class="model-option__info">
                <span class="model-option__name">DeepSeek R1</span>
                <span class="model-option__desc">Chuyên sâu về lập trình và tư duy logic </span>
            </div>
            <svg class="model-option__check" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 6L9 17l-5-5"/></svg>
        </div>

        <div class="model-option" role="option" aria-selected="false" data-value="pollinations">
            <div class="model-option__info">
                <span class="model-option__name">Pollinations AI</span>
                <span class="model-option__desc">Mô hình tạo ảnh 3D/Realistic (Miễn phí)</span>
            </div>
            <svg class="model-option__check" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 6L9 17l-5-5"/></svg>
        </div>

        <div class="model-menu__divider"></div>

        <!-- Cài đặt phụ -->
        <button class="model-sub-setting">
            <span class="model-sub-setting__label">Temperature</span>
            <span class="model-sub-setting__value">0.7 <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 18l6-6-6-6"/></svg></span>
        </button>
        <button class="model-sub-setting">
            <span class="model-sub-setting__label">Max Tokens</span>
            <span class="model-sub-setting__value">2048 <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 18l6-6-6-6"/></svg></span>
        </button>
    </div>
</div>
```

*(Phần CSS giữ nguyên như cũ, bạn có thể lấy lại từ đoạn code trước)*

**JS (Cập nhật logic cập nhật giá trị khi click):**
```javascript
document.addEventListener('DOMContentLoaded', () => {
    const trigger = document.getElementById('modelTrigger');
    const menu = document.getElementById('modelMenu');
    const currentModelLabel = document.getElementById('currentModelLabel');
    const options = menu.querySelectorAll('.model-option');

    // Toggle menu
    trigger.addEventListener('click', (e) => {
        e.stopPropagation();
        const isExpanded = trigger.getAttribute('aria-expanded') === 'true';
        trigger.setAttribute('aria-expanded', !isExpanded);
        menu.classList.toggle('active');
    });

    // Close when clicking outside
    document.addEventListener('click', (e) => {
        if (!menu.contains(e.target) && !trigger.contains(e.target)) {
            trigger.setAttribute('aria-expanded', 'false');
            menu.classList.remove('active');
        }
    });

    // Handle selection
    options.forEach(option => {
        option.addEventListener('click', () => {
            // Remove selected state from all
            options.forEach(opt => {
                opt.classList.remove('selected');
                opt.setAttribute('aria-selected', 'false');
            });
            // Add to clicked
            option.classList.add('selected');
            option.setAttribute('aria-selected', 'true');
            
            // Cập nhật text trên nút trigger thành tên Model vừa chọn
            const selectedModelName = option.querySelector('.model-option__name').textContent;
            currentModelLabel.textContent = selectedModelName;
            
            // (Tùy chọn) Cập nhật thẻ input hidden để gửi lên Backend ASP.NET
            // document.getElementById('hiddenSelectedModel').value = option.getAttribute('data-value');
            
            // Close menu
            trigger.setAttribute('aria-expanded', 'false');
            menu.classList.remove('active');
        });
    });
});
```

### 10.3. Prompt cho AI (Copy & Paste)
> "Hãy tạo một component 'AI Model Selector Dropdown' bằng HTML, CSS và JS thuần. Yêu cầu:
> 1. Nút trigger (dạng pill) hiển thị chữ 'Gemini 2.0 Flash'. Khi click, nó mở một menu xổ LÊN TRÊN.
> 2. Menu có tiêu đề 'Chọn Model AI', danh sách các model: Gemini 2.0 Flash, Llama 3 8B Instruct, DeepSeek R1, và Pollinations AI. Mỗi model có tên, mô tả ngắn và dấu tích ✓.
> 3. Bên dưới có đường kẻ ngang, dưới cùng là 2 nút cài đặt: 'Temperature 0.7' và 'Max Tokens 2048'.
> 4. Dùng CSS variables để style, không dùng inline CSS.
> 5. Viết JS xử lý click mở/đóng, click ra ngoài đóng. Khi chọn model khác, cập nhật lại text trên nút trigger."
```
Bo góc 3px