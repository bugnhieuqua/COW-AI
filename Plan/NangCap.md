Dưới đây là bản kế hoạch Markdown chi tiết để xây dựng lại Dashboard cho COW.AI, kết hợp các nguyên tắc thiết kế từ **UI UX Pro Max (uupm.cc)** và các tiêu chuẩn **SEO kỹ thuật** cho ASP.NET Core. Kế hoạch này được thiết kế để có thể triển khai trong 3 ngày, tập trung vào giao diện "promax" và trải nghiệm người dùng mượt mà.

---

```markdown
# KẾ HOẠCH DỰNG LẠI DASHBOARD COW.AI (UX/UI PROMAX + SEO)

## 1. Triết lý Thiết kế & Nguồn cảm hứng
- **UI UX Pro Max (uupm.cc):** Đây là một AI skill cung cấp "trí tuệ thiết kế" với hàng trăm UI style, bảng màu, cặp font chữ và hướng dẫn UX. Chúng ta sẽ áp dụng tư duy **Design System Generator** của nó: định nghĩa rõ ràng token (màu, chữ, khoảng cách) trước khi code, thay vì chỉnh sửa tùy hứng.
- **Phong cách chủ đạo:** Dark Mode (near-black neutrals, không dùng đen tuyệt đối), kết hợp Glassmorphism nhẹ và điểm nhấn Neon (xanh dương/tím) để tạo cảm giác công nghệ cao.
- **Mục tiêu UX:** Giảm tải nhận thức (cognitive load) bằng cách chia workspace thành các vùng rõ ràng, sử dụng phân cấp thị giác (visual hierarchy) qua typography và khoảng trắng, thay vì lạm dụng khung viền.

## 2. Cấu trúc Layout (3 Vùng)

Dashboard sẽ là một **Workspace Studio** thay vì giao diện chat.

### 2.1. Sidebar (Cột trái – 280px)
- **Brand:** Logo COW.AI + tên "Studio".
- **Nút "Dự án mới":** Nổi bật với gradient và hiệu ứng hover (Neon glow).
- **Lịch sử dự án:** Danh sách các file đã upload. Mỗi item hiển thị tên file, trạng thái (`Đang xử lý` màu vàng, `Hoàn thành` màu xanh lá) và có thể click để mở lại workspace.
- **Cài đặt nhanh:** Dropdown chọn giọng đọc mặc định (Nam/Nữ) và model AI (Ollama).

### 2.2. Header (Thanh trên cùng)
- Hiển thị tên dự án hiện tại (ví dụ: "Bài giảng OWASP").
- Các nút hành động: **"Dọn dẹp file tạm"**, **"Xuất Video"** (chỉ active khi video đã render xong).
- Breadcrumb đơn giản (nếu cần) để điều hướng.

### 2.3. Main Workspace (Vùng làm việc chính)
Đây là khu vực thay đổi linh hoạt theo trạng thái của dự án:

| Trạng thái | Mô tả giao diện |
| :--- | :--- |
| **Empty (Ban đầu)** | Khung viền nét đứt lớn ở giữa: *"Kéo thả file PDF/PPTX vào đây để bắt đầu"*. |
| **Reviewing Script** | Chia 2 cột: **(1)** Trình soạn thảo kịch bản (textarea lớn hoặc Monaco Editor) chứa JSON/Markdown do AI sinh ra. **(2)** Panel cấu hình render: chọn giọng đọc, upload ảnh nền (tùy chọn), nút **"Bắt đầu Render"**. |
| **Processing** | Thanh tiến trình (Progress Bar) chạy từ 0% → 100% với các mốc: *"Đang tạo âm thanh..." → "Đang render video..."*. Có nút "Hủy" nếu muốn dừng. |
| **Completed** | Trình phát video (Video Player) tùy chỉnh. Nút **"Tải xuống MP4"** và **"Chỉnh sửa lại"** (quay về trạng thái Reviewing). |
### 2.4. Khu vực "Folder" (Quản lý file)
- Một tab nhỏ (hoặc panel bên phải) hiển thị danh sách các file AI đã tạo: `audio.mp3`, `images/`, `script.json`. 
- Nút **"Dọn dẹp"** để xóa các file trung gian (audio, ảnh tạm) sau khi render thành công, giữ lại `output.mp4`.

## 3. Hướng dẫn SEO cho ASP.NET Core

Vì dashboard là ứng dụng sau đăng nhập, SEO chủ yếu áp dụng cho các trang **public** (landing page, login). Tuy nhiên, vẫn cần đảm bảo các nguyên tắc sau để crawler không gặp lỗi và trang public hiển thị tốt.

### 3.1. Server-Side Rendering (SSR) & URL sạch
- Sử dụng **Razor Pages/MVC** (không dùng SPA thuần) để nội dung được render từ server, giúp bot đọc được ngay mà không cần chạy JavaScript.
- Sử dụng **Attribute Routing** để tạo URL thân thiện: `/projects/{id}/edit` thay vì `/Project/Edit?id=123`.

### 3.2. Meta Tags & Structured Data
- Mỗi trang public cần có:
  - `<title>` (dưới 60 ký tự).
  - `<meta name="description">` (dưới 160 ký tự).
  - `<link rel="canonical">` trỏ đến URL gốc (HTTPS).
- Thêm **JSON-LD** (schema.org) cho các trang giới thiệu tính năng (ví dụ: `SoftwareApplication`).
- Đối với các trang **private** (dashboard, workspace), thêm `<meta name="robots" content="noindex, nofollow">` để tránh index nhầm.

### 3.3. Semantic HTML & Accessibility
- Sử dụng thẻ semantic: `<header>`, `<nav>`, `<main>`, `<aside>`, `<section>`, `<footer>`.
- Mỗi `<img>` phải có `alt` mô tả. Các nút icon phải có `aria-label`.
- Đảm bảo cấu trúc heading hợp lý: mỗi trang chỉ có một `<h1>`, các heading con theo thứ tự `<h2>`, `<h3>`.

### 3.4. Tối ưu tốc độ & tài nguyên
- Nén (minify) CSS/JS khi deploy.
- Sử dụng `loading="lazy"` cho hình ảnh không quan trọng.
- Đảm bảo HTTPS (bắt buộc với ASP.NET Core qua `app.UseHttpsRedirection()`).

## 4. Code mẫu HTML/CSS (Sử dụng Tailwind CSS qua CDN)

Để nhanh chóng đạt được giao diện "promax" mà không cần viết nhiều CSS, hãy thêm Tailwind CSS vào layout. Dưới đây là khung HTML cho Dashboard.

### 4.1. Thêm Tailwind vào `_Layout.cshtml`
```html
<head>
    <!-- Các thẻ meta khác -->
    <script src="https://cdn.tailwindcss.com"></script>
    <style>
        /* Tùy chỉnh token màu (Design System) */
        :root {
            --bg-primary: #0d1117;    /* Near-black */
            --bg-surface: #161b22;
            --text-primary: #c9d1d9;
            --neon-blue: #3b82f6;
            --neon-purple: #8b5cf6;
        }
    </style>
</head>
```

### 4.2. Cấu trúc Dashboard (`Views/Home/Index.cshtml`)
```html
@{
    ViewData["Title"] = "COW.AI Studio";
}

<div class="flex h-screen bg-[#0d1117] text-[#c9d1d9] font-sans">
    <!-- Sidebar -->
    <aside class="w-72 bg-[#161b22] border-r border-white/10 p-6 flex flex-col gap-6 overflow-y-auto">
        <div class="flex items-center gap-3">
            <img src="~/images/logo.svg" alt="COW.AI" width="32" />
            <h1 class="text-xl font-bold text-white">COW.AI Studio</h1>
        </div>
        
        <button class="w-full py-3 bg-gradient-to-r from-blue-500 to-purple-600 text-white font-semibold rounded-lg hover:opacity-90 transition shadow-lg shadow-blue-500/30">
            + Dự án mới
        </button>

        <div class="mt-4">
            <h2 class="text-sm font-semibold text-gray-400 uppercase tracking-wider mb-3">Gần đây</h2>
            <ul class="space-y-2">
                <li class="p-3 bg-white/5 rounded-lg border border-white/10 cursor-pointer hover:bg-white/10 flex justify-between items-center">
                    <span class="truncate">Bài giảng OWASP</span>
                    <span class="text-xs text-green-400 bg-green-400/10 px-2 py-1 rounded-full">Xong</span>
                </li>
                <li class="p-3 bg-white/5 rounded-lg border border-white/10 cursor-pointer hover:bg-white/10 flex justify-between items-center">
                    <span class="truncate">Báo cáo Q3</span>
                    <span class="text-xs text-yellow-400 bg-yellow-400/10 px-2 py-1 rounded-full">Đang chạy</span>
                </li>
            </ul>
        </div>
    </aside>

    <!-- Main Workspace -->
    <main class="flex-1 flex flex-col overflow-hidden">
        <!-- Header -->
        <header class="p-6 border-b border-white/10 flex justify-between items-center bg-[#0d1117]/80 backdrop-blur-sm">
            <div>
                <h2 class="text-2xl font-bold text-white">Bài giảng OWASP</h2>
                <p class="text-sm text-gray-400">Kịch bản đã sẵn sàng để review</p>
            </div>
            <div class="flex gap-3">
                <button class="px-4 py-2 bg-transparent border border-white/20 rounded-lg hover:bg-white/5 transition text-sm">Dọn dẹp file</button>
                <button class="px-4 py-2 bg-blue-600 hover:bg-blue-700 rounded-lg transition text-sm font-medium">Xuất Video</button>
            </div>
        </header>

        <!-- Workspace Content (Trạng thái Reviewing) -->
        <div class="flex-1 p-6 overflow-y-auto">
            <div class="grid grid-cols-1 lg:grid-cols-3 gap-6 h-full">
                <!-- Script Editor (2 cột) -->
                <div class="lg:col-span-2 bg-[#161b22] border border-white/10 rounded-xl p-5 flex flex-col">
                    <h3 class="text-lg font-semibold text-white mb-3">Kịch bản AI (Markdown)</h3>
                    <textarea class="flex-1 w-full bg-[#0d1117] border border-white/10 rounded-lg p-4 text-sm font-mono text-gray-300 resize-none focus:outline-none focus:border-blue-500/50" spellcheck="false">Cảnh 1: OWASP là gì?
                        OWASP là một tổ chức phi lợi nhuận...</textarea>
                </div>
                <!-- Render Settings (1 cột) -->
                <div class="bg-[#161b22] border border-white/10 rounded-xl p-5 flex flex-col gap-4">
                    <h3 class="text-lg font-semibold text-white">Cấu hình Video</h3>
                    <div>
                        <label class="block text-sm text-gray-400 mb-1">Giọng đọc</label>
                        <select class="w-full bg-[#0d1117] border border-white/10 rounded-lg p-2 text-sm focus:outline-none focus:border-blue-500/50">
                            <option>Giọng Nữ (Hoài My - VieNeu)</option>
                            <option>Giọng Nam (Nam Minh - VieNeu)</option>
                        </select>
                    </div>
                    <div>
                        <label class="block text-sm text-gray-400 mb-1">Ảnh nền (Tùy chọn)</label>
                        <div class="border-2 border-dashed border-white/20 rounded-lg p-6 text-center text-sm text-gray-400 cursor-pointer hover:border-blue-500/50 transition">
                            + Tải ảnh lên
                        </div>
                    </div>
                    <button class="mt-auto w-full py-3 bg-gradient-to-r from-green-500 to-emerald-600 text-white font-bold rounded-lg hover:opacity-90 transition shadow-lg shadow-green-500/30">
                        Bắt đầu Render Video
                    </button>
                </div>
            </div>
        </div>
    </main>
</div>
```

### 4.3. CSS bổ sung (nếu cần)
- Tailwind đã xử lý hầu hết layout. Bạn có thể thêm các hiệu ứng `backdrop-blur`, `shadow` (đã có trong class) và tùy chỉnh thanh cuộn (scrollbar) bằng CSS nhỏ.

## 5. Lịch trình Triển khai 3 Ngày (Tập trung vào UI/UX)

| Ngày | Công việc chính | Kết quả |
| :--- | :--- | :--- |
| **Ngày 1** | Dựng layout với Tailwind (Sidebar, Header, Workspace). Tạo các trạng thái (Empty, Reviewing, Processing, Completed) bằng cách ẩn/hiện các `div`. | Giao diện tĩnh hiển thị đúng, có thể chuyển đổi giữa các trạng thái bằng nút bấm (dùng JS đơn giản). |
| **Ngày 2** | Nối dữ liệu giả lập (Mock Data): Viết JS để khi bấm "Render", chạy thanh tiến trình giả (setInterval). Hiển thị video mẫu (dùng thẻ `<video>` với link MP4 có sẵn). | Luồng UX mượt mà, không cần backend thật. |
| **Ngày 3** | Tích hợp backend thật (ASP.NET Core): Gọi API Ollama để lấy kịch bản, gọi API FFmpeg (hoặc VieNeu) để render. Cập nhật trạng thái từ server. Thêm meta tags SEO cho trang public. | Sản phẩm chạy end-to-end, sẵn sàng demo. |

## 6. Checklist SEO trước khi Demo
- [ ] Trang landing page (`/`) có thẻ `<title>`, `<meta name="description">`, và canonical.
- [ ] Trang dashboard (`/studio`) có `<meta name="robots" content="noindex">`.
- [ ] Tất cả hình ảnh có `alt`.
- [ ] Sử dụng `app.UseHttpsRedirection()` và `app.UseHsts()`.
- [ ] URL thân thiện (ví dụ: `/projects/1` thay vì `/Project/Details?id=1`).
- [ ] Đảm bảo nội dung chính được server-render (không phụ thuộc hoàn toàn vào JS).

## 7. Tài nguyên tham khảo
- **UI UX Pro Max:** [uupm.cc](https://uupm.cc) – nơi cung cấp design system, color palette và UX guidelines.
- **Tailwind CSS:** Sử dụng CDN để nhanh chóng prototype.
- **ASP.NET Core SEO:** Tham khảo các best practices về routing, metadata và server-rendering.
```

---

**Lưu ý:** Bản kế hoạch này ưu tiên tốc độ và tính thẩm mỹ. Trong 3 ngày, bạn không cần hoàn thiện mọi tính năng backend phức tạp; hãy tập trung vào việc tạo ra một **trải nghiệm người dùng mượt mà** với dữ liệu giả lập (mock data) để demo, sau đó mới nối các API thật.