## 5. Kế hoạch Triển khai Giao diện (Views) & Fix UX/UI

### 5.1. Quy chuẩn UX/UI Chung (Bắt buộc - Chống "dính" chữ)
Để giao diện không bị rối mắt và đạt chuẩn SEO/Accessibility, mọi file `.cshtml` phải tuân thủ:
- **Hệ thống khoảng cách (Spacing System):** Bắt buộc dùng biến CSS cho margin/padding (VD: `--space-1: 4px; --space-2: 8px; --space-3: 16px; --space-4: 24px; --space-5: 32px;`). Không dùng các giá trị lẻ tẻ như `margin: 13px`.
- **Typography (Giãn dòng):** Body text phải có `line-height: 1.6`, Tiêu đề `line-height: 1.2`. Thêm `letter-spacing: 0.5px` cho các đoạn text nhỏ, nút bấm.
- **Semantic HTML (Chuẩn SEO):**
  - Mỗi trang chỉ có duy nhất một thẻ `<h1>`.
  - Các khu vực dùng thẻ `<section>`, `<article>`, `<nav>`.
  - Mọi thẻ `<img>` phải có thuộc tính `alt` mô tả. Nút bấm dạng icon phải có `aria-label`.
- **Fix lỗi Dropdown (Theme Switcher):** Nút bấm chọn Theme phải nằm trong một thẻ `<div class="dropdown">` (position: relative). Menu xổ xuống dùng `position: absolute`, `z-index: 1000` và mặc định phải có class `display: none` (hoặc `opacity: 0; pointer-events: none`), chỉ hiển thị khi có class `active` được thêm vào bằng JavaScript.

### 5.2. `_Layout.cshtml` (Khung chính)
- Chứa thẻ `<canvas id="three-canvas"></canvas>` (position fixed, z-index -1).
- **Navbar:** Dùng thẻ `<header>` và `<nav>`. Logo COW.AI (có thẻ `<img alt="Logo COW.AI">`), các link điều hướng. 
- **Theme Switcher:** Khung chứa Dropdown phải có `z-index` cao nhất để không bị các thẻ khác đè lên.
- **Footer:** Đơn giản, dùng thẻ `<footer>`.

### 5.3. `Home/Index.cshtml` (Landing Page)
- **Hero Section:** Dùng `<section class="hero">`. Tiêu đề `<h1 class="hero-title">COW.AI - Trợ lý AI của bạn</h1>`. Phụ đề `<p class="hero-subtitle">`.
- **Prompt Bar:** Thẻ `<div class="prompt-bar">` (dùng glassmorphism). Bên trong gồm `<textarea class="prompt-input" aria-label="Nhập lệnh cho AI">` và `<button class="prompt-submit" aria-label="Gửi lệnh">`.
- **Tính năng:** Grid 3 thẻ `<article class="feature-card">`. Các thẻ phải có hiệu ứng hover (`transform: translateY(-5px)` và `box-shadow`).

### 5.4. `Image/Index.cshtml` (Trang Tạo ảnh)
- **Layout:** Dùng `<main class="image-generator-page">` bọc ngoài. Bên trong dùng CSS Grid chia 2 cột (`grid-template-columns: 1fr 1fr; gap: 32px;`).
- **Cột trái (Form):** 
  - `<label for="prompt-input">Mô tả bức ảnh bạn muốn tạo</label>`
  - `<textarea id="prompt-input" class="form-control"></textarea>` (có padding 16px, line-height 1.6).
  - Các nhóm nút chọn Tỉ lệ / Phong cách: Dùng `<div class="chip-group">` (flex, gap 8px). Nút bấm phải có padding 8px 16px, border-radius 20px.
  - Nút "Tạo ảnh ngay": `width: 100%; padding: 16px; font-size: 1.1rem;`.
- **Cột phải (Kết quả):** Dùng `<section class="result-panel">`. Khung hiển thị ảnh có `min-height: 400px`, viền nét đứt, `display: flex; align-items: center; justify-content: center;`.

### 5.5. `Document/Summary.cshtml` & `CodeGen.cshtml`
- **Layout:** Tương tự trang Tạo ảnh. Khoảng cách giữa các label và input phải có `margin-bottom: 8px`. Khoảng cách giữa các nhóm (Form nhập -> Nút bấm) là `margin-bottom: 24px`.
- **CodeGen:** Sử dụng `Prism.js` để tô màu code. Thẻ `<pre><code class="language-python">` phải có `padding: 16px; border-radius: 8px; background: #1e1e1e; font-family: monospace;`.

---

## 6. Prompt Mẫu cho AI (Dùng để Fix lỗi giao diện)

**Prompt 1: Fix lỗi Dropdown Theme bị đè (Ảnh 1)**
> "Giao diện của tôi đang bị lỗi Dropdown chọn giao diện (Dark/Light/System) bị hiển thị đè lên các thành phần khác. Hãy viết lại HTML/CSS và JS cho component Dropdown này. Yêu cầu: Bọc nó trong `<div class="theme-switcher" style="position: relative; z-index: 1000;">`. Menu xổ xuống phải dùng `position: absolute; right: 0; top: 100%;`. Mặc định ẩn (dùng `display: none`). Viết thêm một đoạn JS nhỏ để khi click vào nút, menu này toggle hiển thị và click ra ngoài thì tự động đóng."

**Prompt 2: Fix lỗi "dính" chữ và Chuẩn hóa Layout (Ảnh 2)**
> "Giao diện trang Tạo ảnh của tôi đang bị dính chữ, thiếu khoảng cách (padding/margin) và chưa đạt chuẩn SEO. Hãy viết lại toàn bộ HTML của trang này (dùng thẻ semantic) và CSS tương ứng. Yêu cầu cụ thể:
> - Dùng CSS Grid chia 2 cột, khoảng cách giữa 2 cột là `gap: 32px`.
> - Các thẻ label, textarea, button phải có khoảng cách rõ ràng (textarea có `padding: 16px`, label cách textarea `8px`).
> - Nút bấm (Tạo ảnh ngay) phải cao `48px`, màu gradient.
> - Các nút chọn Tỉ lệ / Phong cách (Chips) phải nằm trên 1 hàng, có `gap: 12px`, nút có `padding: 8px 16px`, bo tròn `border-radius: 20px`.
> - Chữ trong trang phải có `line-height: 1.6` để không bị dính vào nhau.
> - Đảm bảo có thẻ `<h1>` cho tiêu đề 'Tạo ảnh AI'."

**Prompt 3: Thêm hiệu ứng Loading UX/UI**
> "Khi người dùng bấm nút 'Tạo ảnh ngay', hãy viết JS để đổi text của nút thành 'Đang tạo...' kèm theo icon loading xoay tròn (spinner). Đồng thời vô hiệu hóa nút (thuộc tính `disabled`) cho đến khi có kết quả trả về để tránh người dùng spam click."