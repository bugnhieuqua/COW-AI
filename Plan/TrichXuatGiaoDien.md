# BÁO CÁO TRÍCH XUẤT CẤU TRÚC GIAO DIỆN TỪ REPO `ai-chat-ui`

> **Nguồn trích xuất:** `thakurcoderz/ai-chat-ui`  
> **Mục tiêu:** Cung cấp đầy đủ cấu trúc HTML ngữ nghĩa và CSS thuần (đã chuyển đổi từ Tailwind CSS) phục vụ cho dự án **COW.AI** chạy trên nền tảng **ASP.NET Core MVC**.

---

## 1. Hệ thống Biến Màu và Theme Tokens (CSS Variables)

Để hỗ trợ chuyển đổi giao diện Dark / Light Mode mượt mà trên ASP.NET MVC:

```css
:root {
  /* Bảng màu chung (Shared Accent & Gradients) */
  --cow-accent: #0EA5E9;
  --cow-gradient-start: #0EA5E9;
  --cow-gradient-end: #6366F1;

  /* Theme sáng (Light Mode mặc định hoặc khi gỡ class .dark) */
  --cow-bg: #ffffff;
  --cow-bg-secondary: #f5f5f5;
  --cow-border: #e5e5e5;
  --cow-text: #111827;
  --cow-text-muted: #6b7280;
  --cow-glass-bg: rgba(255, 255, 255, 0.7);
  --cow-glass-border: rgba(229, 231, 235, 0.5);
  --cow-input-bg: rgba(255, 255, 255, 0.8);
  --cow-msg-user-bg: rgba(14, 165, 233, 0.12);
  --cow-msg-ai-bg: #f9fafb;
}

/* Kích hoạt chế độ Dark Mode thông qua class .dark trên thẻ <html> hoặc <body> */
body.dark, html.dark body {
  --cow-bg: #09090b;
  --cow-bg-secondary: #121216;
  --cow-border: #27272a;
  --cow-text: #f4f4f5;
  --cow-text-muted: #9ca3af;
  --cow-glass-bg: rgba(15, 15, 20, 0.65);
  --cow-glass-border: rgba(255, 255, 255, 0.1);
  --cow-input-bg: rgba(18, 18, 24, 0.6);
  --cow-msg-user-bg: rgba(99, 102, 241, 0.22);
  --cow-msg-ai-bg: rgba(255, 255, 255, 0.04);
}
```

---

## 2. Component 1: Navbar & Theme Switcher

### 2.1. Cấu trúc HTML
```html
<!-- Navbar Glassmorphism cố định ở đầu trang -->
<header class="navbar-glass">
  <div class="navbar-container">
    <div class="navbar-brand">
      <a href="/" class="brand-logo gradient-text">COW.AI</a>
    </div>

    <!-- Menu điều hướng chính -->
    <nav class="navbar-nav">
      <a href="/Image" class="nav-link">Tạo ảnh</a>
      <a href="/Document/Summary" class="nav-link">Tóm tắt</a>
      <a href="/Document/CodeGen" class="nav-link">Viết code</a>
    </nav>

    <!-- Theme Switcher Dropdown -->
    <div class="theme-switcher-wrapper">
      <button id="themeToggleBtn" class="theme-btn" type="button" aria-label="Toggle Theme">
        <!-- Icon mặt trăng (Dark Mode) -->
        <svg class="icon-theme" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />
        </svg>
      </button>

      <div id="themeDropdownMenu" class="theme-dropdown hidden glass-panel">
        <button class="theme-option active" data-theme="dark">
          <svg class="icon-sm" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z"/></svg>
          Dark
        </button>
        <button class="theme-option" data-theme="light">
          <svg class="icon-sm" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z"/></svg>
          Light
        </button>
        <button class="theme-option" data-theme="system">
          <svg class="icon-sm" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z"/></svg>
          System
        </button>
      </div>
    </div>
  </div>
</header>
```

### 2.2. CSS thuần tương ứng
```css
.navbar-glass {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  z-index: 50;
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  background-color: var(--cow-glass-bg);
  border-bottom: 1px solid var(--cow-glass-border);
  transition: background-color 0.3s ease, border-color 0.3s ease;
}

.navbar-container {
  max-width: 80rem;
  margin: 0 auto;
  padding: 0 1.5rem;
  height: 4rem;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.brand-logo {
  font-size: 1.5rem;
  font-weight: 700;
  text-decoration: none;
  letter-spacing: -0.025em;
}

.gradient-text {
  background: linear-gradient(to right, var(--cow-gradient-start), var(--cow-gradient-end));
  -webkit-background-clip: text;
  background-clip: text;
  color: transparent;
  filter: drop-shadow(0 0 12px rgba(14, 165, 233, 0.4));
}

.navbar-nav {
  display: flex;
  align-items: center;
  gap: 1.5rem;
}

.nav-link {
  color: var(--cow-text-muted);
  text-decoration: none;
  font-size: 0.95rem;
  font-weight: 500;
  transition: color 0.2s ease;
}

.nav-link:hover {
  color: var(--cow-text);
}

.theme-switcher-wrapper {
  position: relative;
}

.theme-btn {
  background: transparent;
  border: 1px solid transparent;
  color: var(--cow-text);
  padding: 0.5rem;
  border-radius: 0.5rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s ease;
}

.theme-btn:hover {
  background-color: rgba(255, 255, 255, 0.08);
}

.icon-theme {
  width: 1.25rem;
  height: 1.25rem;
}

.theme-dropdown {
  position: absolute;
  right: 0;
  margin-top: 0.5rem;
  width: 11rem;
  border-radius: 0.75rem;
  padding: 0.35rem;
  box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.3);
  z-index: 60;
}

.glass-panel {
  backdrop-filter: blur(24px);
  -webkit-backdrop-filter: blur(24px);
  background-color: var(--cow-glass-bg);
  border: 1px solid var(--cow-glass-border);
}

.theme-option {
  display: flex;
  align-items: center;
  width: 100%;
  padding: 0.5rem 0.75rem;
  border: none;
  border-radius: 0.5rem;
  background: transparent;
  color: var(--cow-text);
  font-size: 0.875rem;
  cursor: pointer;
  gap: 0.75rem;
  transition: background 0.15s ease;
}

.theme-option:hover {
  background-color: rgba(255, 255, 255, 0.08);
}

.theme-option.active {
  color: var(--cow-accent);
  background-color: rgba(14, 165, 233, 0.12);
}

.hidden {
  display: none !important;
}
```

---

## 3. Component 2: Khung Chat Chính (Message Bubbles & Typing Indicator)

### 3.1. Cấu trúc HTML
```html
<div class="chat-thread-container">
  <div class="chat-messages-inner">
    
    <!-- Bong bóng chat của người dùng (Bên phải) -->
    <div class="message-row message-user-row">
      <div class="message-bubble message-user-bubble">
        <p class="message-text">Hãy viết một đoạn code Python để tóm tắt văn bản bằng AI.</p>
      </div>
    </div>

    <!-- Bong bóng chat AI đang suy nghĩ (Typing/Thinking indicator) -->
    <div class="message-row message-ai-row" id="thinkingIndicator">
      <div class="message-bubble message-ai-bubble">
        <div class="typing-indicator">
          <span class="dot dot-1"></span>
          <span class="dot dot-2"></span>
          <span class="dot dot-3"></span>
        </div>
      </div>
    </div>

    <!-- Bong bóng chat phản hồi của AI (Bên trái) -->
    <div class="message-row message-ai-row">
      <div class="message-bubble message-ai-bubble">
        <p class="message-text">Dưới đây là giải pháp được tạo tự động bởi COW.AI:</p>
        <ul class="message-list">
          <li>
            <span class="bullet-accent">•</span>
            <span>Khởi tạo model Gemini hoặc OpenAI API</span>
          </li>
          <li>
            <span class="bullet-accent">•</span>
            <span>Chia nhỏ đoạn văn bản nếu vượt quá context window</span>
          </li>
          <li>
            <span class="bullet-accent">•</span>
            <span>Tạo kết quả tóm tắt súc tích với đầy đủ ý chính</span>
          </li>
        </ul>
      </div>
    </div>

  </div>
</div>
```

### 3.2. CSS thuần tương ứng
```css
.chat-thread-container {
  flex: 1;
  overflow-y: auto;
  padding-top: 5rem;
  padding-bottom: 9rem;
}

.chat-messages-inner {
  max-width: 48rem;
  margin: 0 auto;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.message-row {
  display: flex;
  width: 100%;
}

.message-user-row {
  justify-content: flex-end;
}

.message-ai-row {
  justify-content: flex-start;
}

.message-bubble {
  position: relative;
  max-width: 80%;
  padding: 1.25rem 1.5rem;
  border-radius: 1.25rem;
  overflow: hidden;
  box-shadow: 0 4px 15px rgba(0, 0, 0, 0.05);
  transition: transform 0.2s ease, box-shadow 0.2s ease;
}

.message-bubble:hover {
  transform: translateY(-1px) scale(1.01);
}

.message-bubble::before {
  content: '';
  position: absolute;
  inset: 0;
  background: linear-gradient(90deg, rgba(255, 255, 255, 0.06), transparent);
  opacity: 0;
  transition: opacity 0.2s ease;
  pointer-events: none;
}

.message-bubble:hover::before {
  opacity: 1;
}

/* User Message styling */
.message-user-bubble {
  background-color: var(--cow-msg-user-bg);
  border: 1px solid rgba(14, 165, 233, 0.25);
  border-bottom-right-radius: 0.35rem;
}

/* AI Message styling */
.message-ai-bubble {
  background-color: var(--cow-msg-ai-bg);
  border: 1px solid var(--cow-glass-border);
  backdrop-filter: blur(16px);
  -webkit-backdrop-filter: blur(16px);
  border-bottom-left-radius: 0.35rem;
}

.message-text {
  font-size: 1.05rem;
  line-height: 1.6;
  white-space: pre-wrap;
  margin: 0;
  color: var(--cow-text);
}

.message-list {
  margin-top: 1rem;
  padding-left: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.message-list li {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
  font-size: 0.95rem;
  color: var(--cow-text);
}

.bullet-accent {
  color: var(--cow-accent);
  font-weight: bold;
}

/* Hiệu ứng 3 chấm nhảy khi AI đang suy nghĩ */
.typing-indicator {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.25rem 0.5rem;
}

.typing-indicator .dot {
  width: 0.55rem;
  height: 0.55rem;
  background-color: var(--cow-text-muted);
  border-radius: 50%;
  animation: typingBounce 1.4s infinite ease-in-out both;
}

.typing-indicator .dot-1 {
  animation-delay: -0.32s;
}

.typing-indicator .dot-2 {
  animation-delay: -0.16s;
}

.typing-indicator .dot-3 {
  animation-delay: 0s;
}

@keyframes typingBounce {
  0%, 80%, 100% {
    transform: scale(0);
    opacity: 0.4;
  }
  40% {
    transform: scale(1);
    opacity: 1;
  }
}
```

---

## 4. Component 3: Thanh Nhập Liệu (Prompt Bar & Auto-expanding Textarea)

### 4.1. Cấu trúc HTML
```html
<!-- Khung cố định dưới đáy màn hình (Fixed Bottom Bar) -->
<div class="prompt-bar-dock">
  <div class="prompt-bar-wrapper">
    <form id="chatPromptForm" class="prompt-form">
      <div class="gradient-border-box">
        <div class="input-card">
          
          <!-- Textarea tự động giãn nở chiều cao theo nội dung -->
          <textarea
            id="userPromptInput"
            rows="1"
            placeholder="Nhập yêu cầu cho COW.AI..."
            class="prompt-textarea"
          ></textarea>

          <div class="input-card-footer">
            <div class="input-hint">Nhấn <kbd>Enter</kbd> để gửi, <kbd>Shift + Enter</kbd> để xuống dòng</div>
            
            <button type="submit" id="submitBtn" class="btn-send shimmer-btn">
              <span>Gửi</span>
              <svg class="icon-send" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 12h14M12 5l7 7-7 7" />
              </svg>
            </button>
          </div>

        </div>
      </div>
    </form>
  </div>
</div>
```

### 4.2. CSS thuần tương ứng
```css
.prompt-bar-dock {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  z-index: 40;
  backdrop-filter: blur(24px);
  -webkit-backdrop-filter: blur(24px);
  background-color: var(--cow-glass-bg);
  border-top: 1px solid var(--cow-glass-border);
  padding: 1rem 0;
}

.prompt-bar-wrapper {
  max-width: 48rem;
  margin: 0 auto;
  padding: 0 1rem;
}

.prompt-form {
  position: relative;
  width: 100%;
}

/* Viền phát sáng Gradient (Gradient Border) */
.gradient-border-box {
  position: relative;
  border-radius: 1rem;
  padding: 1.5px;
  background: linear-gradient(135deg,
    rgba(14, 165, 233, 0.4),
    rgba(99, 102, 241, 0.4),
    rgba(14, 165, 233, 0.15),
    rgba(99, 102, 241, 0.15));
  transition: all 0.3s ease;
}

.gradient-border-box:hover,
.gradient-border-box:focus-within {
  background: linear-gradient(135deg,
    rgba(14, 165, 233, 0.8),
    rgba(99, 102, 241, 0.8),
    rgba(14, 165, 233, 0.4),
    rgba(99, 102, 241, 0.4));
  box-shadow: 0 0 24px -4px rgba(14, 165, 233, 0.3);
}

.input-card {
  position: relative;
  background-color: var(--cow-input-bg);
  border-radius: calc(1rem - 1.5px);
  padding: 0.75rem 1rem;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
  transition: background-color 0.2s ease;
}

.prompt-textarea {
  width: 100%;
  background: transparent;
  border: none;
  outline: none;
  color: var(--cow-text);
  font-family: inherit;
  font-size: 1.05rem;
  line-height: 1.5;
  resize: none;
  min-height: 2.75rem;
  max-height: 12.5rem;
  padding: 0.25rem 0;
}

.prompt-textarea::placeholder {
  color: var(--cow-text-muted);
}

.input-card-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 0.5rem;
  padding-top: 0.25rem;
}

.input-hint {
  font-size: 0.78rem;
  color: var(--cow-text-muted);
}

.input-hint kbd {
  background-color: rgba(255, 255, 255, 0.1);
  padding: 0.1rem 0.35rem;
  border-radius: 0.25rem;
  border: 1px solid var(--cow-glass-border);
}

/* Nút bấm hiệu ứng Shimmer ánh sáng */
.btn-send {
  position: relative;
  overflow: hidden;
  background: linear-gradient(to right, var(--cow-gradient-start), var(--cow-gradient-end));
  color: #ffffff;
  border: none;
  border-radius: 0.75rem;
  padding: 0.55rem 1.25rem;
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 0.45rem;
  transition: transform 0.2s ease, opacity 0.2s ease;
}

.btn-send:hover {
  opacity: 0.92;
  transform: translateY(-1px);
}

.btn-send:active {
  transform: translateY(0);
}

.shimmer-btn::after {
  content: '';
  position: absolute;
  inset: 0;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.25), transparent);
  transform: translateX(-100%);
  transition: transform 0.6s ease;
}

.shimmer-btn:hover::after {
  transform: translateX(100%);
}

.icon-send {
  width: 1rem;
  height: 1rem;
}
```

---

## 5. Background Animated Blobs (Hiệu ứng nền động)

### 5.1. Cấu trúc HTML
```html
<div class="ambient-background" aria-hidden="true">
  <div class="blob-wrapper">
    <div class="ambient-blob blob-1"></div>
    <div class="ambient-blob blob-2"></div>
    <div class="ambient-blob blob-3"></div>
  </div>
</div>
```

### 5.2. CSS thuần tương ứng
```css
.ambient-background {
  position: fixed;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
  z-index: 0;
}

.blob-wrapper {
  position: absolute;
  inset: -10px;
  opacity: 0.35;
}

.ambient-blob {
  position: absolute;
  width: 22rem;
  height: 22rem;
  border-radius: 50%;
  filter: blur(80px);
  mix-blend-mode: screen;
  animation: blobFloat 8s infinite alternate ease-in-out;
}

.blob-1 {
  top: 5%;
  left: 10%;
  background: rgba(14, 165, 233, 0.3);
}

.blob-2 {
  top: 15%;
  right: 10%;
  background: rgba(99, 102, 241, 0.3);
  animation-delay: 2.5s;
}

.blob-3 {
  bottom: 10%;
  left: 35%;
  background: rgba(168, 85, 247, 0.25);
  animation-delay: 5s;
}

@keyframes blobFloat {
  0% {
    transform: translate(0px, 0px) scale(1);
  }
  50% {
    transform: translate(30px, -40px) scale(1.1);
  }
  100% {
    transform: translate(-20px, 30px) scale(0.95);
  }
}
```

---

## 6. JavaScript Hỗ trợ Auto-expanding Textarea & Theme Switcher

Đoạn mã JavaScript trích xuất và tối ưu để đưa vào `wwwroot/js/site.js`:

```javascript
document.addEventListener('DOMContentLoaded', () => {
  // 1. Theme Switcher Logic
  const themeToggleBtn = document.getElementById('themeToggleBtn');
  const themeDropdown = document.getElementById('themeDropdownMenu');
  const themeOptions = document.querySelectorAll('.theme-option');

  if (themeToggleBtn && themeDropdown) {
    themeToggleBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      themeDropdown.classList.toggle('hidden');
    });

    document.addEventListener('click', () => {
      themeDropdown.classList.add('hidden');
    });

    themeOptions.forEach(opt => {
      opt.addEventListener('click', () => {
        const selected = opt.getAttribute('data-theme');
        themeOptions.forEach(o => o.classList.remove('active'));
        opt.classList.add('active');

        if (selected === 'dark') {
          document.body.classList.add('dark');
          localStorage.setItem('cow_theme', 'dark');
        } else if (selected === 'light') {
          document.body.classList.remove('dark');
          localStorage.setItem('cow_theme', 'light');
        } else {
          // System preference
          if (window.matchMedia('(prefers-color-scheme: dark)').matches) {
            document.body.classList.add('dark');
          } else {
            document.body.classList.remove('dark');
          }
          localStorage.setItem('cow_theme', 'system');
        }
        themeDropdown.classList.add('hidden');
      });
    });

    // Khởi tạo theme ban đầu
    const savedTheme = localStorage.getItem('cow_theme') || 'dark';
    if (savedTheme === 'dark' || (savedTheme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches)) {
      document.body.classList.add('dark');
    }
  }

  // 2. Auto-expanding Textarea
  const promptInput = document.getElementById('userPromptInput');
  if (promptInput) {
    promptInput.addEventListener('input', function() {
      this.style.height = 'auto';
      this.style.height = (this.scrollHeight) + 'px';
    });

    promptInput.addEventListener('keydown', function(e) {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        document.getElementById('chatPromptForm')?.requestSubmit();
      }
    });
  }
});
```

---

## 7. Đánh giá Mức độ Tương thích với Dự án COW.AI

1. **Phong cách:** Hoàn toàn trùng khớp với định hướng hiện đại, AI-native, hiệu ứng Glassmorphism và viền phát sáng Gradient.
2. **Khả năng tích hợp ASP.NET Core MVC:** 
   - Các đoạn mã HTML/CSS trên đã được gỡ bỏ phụ thuộc vào build tool của Next.js hay Node.js.
   - Có thể trực tiếp đưa vào `Views/Shared/_Layout.cshtml`, `Views/Home/Index.cshtml`, `wwwroot/css/components.css` và `wwwroot/css/pages.css`.
