# KẾ HOẠCH NÂNG CẤP VÀ TÁI CẤU TRÚC GIAO DIỆN ADMIN & WORKSPACE (Plan/NangCapAdmin.md)

> **Mục tiêu**: Rà soát, chuẩn hóa và tái thiết kế toàn bộ hệ thống giao diện từ Bảng điều khiển (Admin Panel / Dock), Thanh điều hướng (Navbar / Drawer), đến các bố cục trang tính năng (`Home`, `Image`, `Document`). 
> **Triết lý**: Xóa bỏ hoàn toàn CSS cũ cồng kềnh, phân mảnh; không phát minh lại bánh xe xe từ số 0 mà kế thừa hệ thống Design Token chuẩn mở công nghiệp (kết hợp **Tailwind CSS**, **Shadcn UI**, **Radix**) và tuân thủ tuyệt đối quy chuẩn **`AGENTS.md`** (`ux-psych-design` + `impeccable`).

---

## 1. Đánh giá hiện trạng Giao diện (Current State Audit)

### 1.1. Các vấn đề cốt lõi đã xác định
- **Quá lộn xộn (Visual Clutter & Fragmentation)**:
  - Tệp `components.css` (~35KB) và `pages.css` (~32KB) đang chứa nhiều đoạn CSS viết đè lẫn nhau, sử dụng nhiều giá trị màu hardcoded thay vì đồng nhất theo biến CSS.
  - Quá nhiều hiệu ứng gradient text tím - hồng - xanh kiểu AI-slop gây mỏi mắt cho lập trình viên/người dùng chuyên nghiệp.
  - Hệ thống bóng đổ (box-shadow) và border phát sáng quá mức (over-saturated glow) vi phạm bộ quy tắc Anti-Slop trong `AGENTS.md`.
- **Chưa chuẩn SEO & Semantic HTML**:
  - Các trang con (`Views/Document/Summary.cshtml`, `Views/Document/CodeGen.cshtml`, `Views/Image/Index.cshtml`) còn lạm dụng thẻ `<div>` bọc lồng nhau, thiếu thẻ cấu trúc semantic chuẩn như `<section>`, `<header>`, `<article>`.
  - Phân cấp thẻ Heading (`<h1>`, `<h2>`, `<h3>`) ở một số khu vực chưa có trật tự logic chặt chẽ, thiếu thẻ `aria-label` và `aria-live` cho các vùng phản hồi động.
- **Chữ quá nhiều, thiếu khoảng thở và nhịp điệu (Typography & Information Density)**:
  - Nhiều khối text giải thích rườm rà, lặp đi lặp lại không cần thiết ở các bảng tính năng.
  - Khoảng cách padding/margin không đồng đều (nhảy cóc giữa 10px, 15px, 25px thay vì theo hệ thống nhịp 8px: 8, 16, 24, 32px).
  - Chưa tối ưu `line-height` và font monospace cho các khối code hoặc console output.

---

## 2. Định hướng Kiến trúc & Chuẩn mã nguồn mở kế thừa (Design System Foundation)

Để không phải xây dựng lại từ số 0, dự án sẽ áp dụng hệ thống token và layout patterns từ các mã nguồn mở tiêu chuẩn:

```
[ OPEN-SOURCE TOKENS ] ──▶ [ SURFACE MODE: OPERATE ] ──▶ [ COMPONENT CLEANUP ] ──▶ [ CLI AUDIT ]
(Tailwind / Radix / Shadcn)      (Tỷ lệ màu 60-30-10)          (8 States & Minimal CSS)      (impeccable detect)
```

### 2.1. Hệ thống Design Tokens chuẩn mở (Semantic Tokens)
Kế thừa bảng màu và thang đo công thái học từ **Tailwind Slate/Zinc/Cyan** và **Radix Primitives**:
- **Tỷ lệ phối màu 60 - 30 - 10**:
  - `60% Nền trung tính`: Dark mode nền sâu `#0b0f19` (Zinc 950), Light mode xám sáng trang nhã `#f8fafc` (Slate 50).
  - `30% Bề mặt thẻ & Text`: Card surface `#131b2e` (Zinc 900) với viền mỏng 1px sắc nét `rgba(255, 255, 255, 0.08)`; Text chính `#f1f5f9` (Slate 100), Text phụ `#94a3b8` (Slate 400).
  - `10% Điểm nhấn (Accent)`: Cyan/Sky Blue (`#38bdf8` / `#0ea5e9`) dành riêng cho nút hành động chính (Primary CTA), trạng thái active và focus rings.
- **Thang đo khoảng cách (8px Grid System)**:
  - Mọi padding, margin, gap đều dùng biến CSS: `--space-1: 4px; --space-2: 8px; --space-3: 16px; --space-4: 24px; --space-5: 32px;`.

### 2.2. Hiệu chỉnh 3 Taste Dials theo `AGENTS.md`
- `DESIGN_VARIANCE`: **5/10** — Cấu trúc Bento Box cân đối, trật tự kỹ thuật, loại bỏ các chi tiết thừa thãi.
- `MOTION_INTENSITY`: **4/10** — Chuyển động nhẹ nhàng (250–350ms cubic-bezier), không làm chậm thao tác người dùng.
- `VISUAL_DENSITY`: **7–8/10** — Tối ưu không gian cho dân kỹ thuật, mở rộng diện tích hiển thị nội dung và code, thu gọn padding rườm rà.

---

## 3. Kế hoạch Triển khai Chi tiết từng Khu vực (Action Items)

### Giai đoạn 1: Chuẩn hóa Design System & Biến CSS Toàn cục (`wwwroot/css/site.css`)
- [ ] Dọn dẹp `site.css`: Định nghĩa bộ biến CSS chuẩn (Design Tokens) cho cả 2 theme `light` và `dark`:
  - `--color-bg-app`, `--color-surface-card`, `--color-border-subtle`, `--color-text-primary`, `--color-text-secondary`, `--color-accent`.
  - Hệ thống font chữ: Sans-serif (`Inter`, `system-ui`) và Monospace (`JetBrains Mono`, `Fira Code`, `Consolas`).
- [ ] Thiết lập Reset CSS cơ bản và Typography chuẩn SEO (line-height 1.6, tiêu đề 1.25, letter-spacing chuẩn).

### Giai đoạn 2: Tái thiết kế Thanh điều hướng & Bảng điều khiển (Navbar & Admin Dock)
- [ ] **Thanh Navbar trên cùng (`_Layout.cshtml`)**:
  - Chuyển sang phong cách Glassmorphism tối giản (Minimal Border, backdrop-filter 12px, không viền phát sáng lòe loẹt).
  - Tối ưu cấu trúc SEO với thẻ `<header>`, `<nav role="navigation">`, Schema.org `SiteNavigationElement`.
  - Tối ưu Drawer Lịch sử chat: Đóng mở mượt mà, phân loại phiên chat theo thời gian (Hôm nay, Hôm qua, 7 ngày trước).
- [ ] **Bảng điều khiển / Dock phía dưới (`Views/Home/Index.cshtml`)**:
  - Thu gọn thanh Prompt Bar: Thiết kế phẳng tinh gọn, bo góc chuẩn mực `10px` (bỏ pill button tròn quá đà 9999px).
  - Khắc phục triệt để luồng chọn Model: Khi chọn model âm thanh (Edge TTS) thì hiển thị rõ badge trạng thái "Chế độ đọc giọng nói", tránh người dùng nhầm lẫn với chế độ Hỏi đáp AI thông thường.

### Giai đoạn 3: Tái cấu trúc các trang tính năng con (`Image`, `Document`, `Summary`, `CodeGen`)
- [ ] **Trang Tạo ảnh (`Views/Image/Index.cshtml`)**:
  - Áp dụng bố cục 2 cột cân đối (Split Layout): Cột trái là Bảng điều khiển tham số (Form, Kích thước, Phong cách dưới dạng Toggle Chips gọn gàng); Cột phải là Preview Canvas.
  - Cắt giảm văn bản giải thích thừa, thay bằng Tooltip hoặc Placeholder súc tích.
- [ ] **Trang Tóm tắt & Viết code (`Views/Document/Summary.cshtml`, `CodeGen.cshtml`)**:
  - Chuẩn hóa Semantic HTML: Thẻ `<article>` cho vùng nội dung, khối `<pre><code>` có thanh công cụ tiện ích (Copy code, chọn ngôn ngữ).
  - Đảm bảo đạt chuẩn WCAG 2.2 AA về độ tương phản text và keyboard focus-visible.

### Giai đoạn 4: Dọn dẹp CSS cũ & Quét lỗi Slop bằng CLI cơ học
- [ ] Loại bỏ hoàn toàn các class CSS chết, CSS trùng lặp trong `components.css` và `pages.css`.
- [ ] Chạy CLI kiểm tra cơ học theo quy định tại `AGENTS.md`:
  ```powershell
  & ".agent/skills/impeccable/scripts/impeccable.cmd" detect --json wwwroot/css/components.css wwwroot/css/pages.css
  ```
- [ ] Tinh chỉnh nước bóng cuối cùng (`/impeccable polish`, `/impeccable quieter`) để đảm bảo diện mạo chuyên nghiệp, tinh tế và thuần chất kỹ thuật.

---

## 4. Tiêu chí Nghiệm thu (Definition of Done)

1. **Hiệu năng & Kích thước**: Dung lượng CSS nén lại giảm tối thiểu 40% so với kích thước hiện tại (~70KB xuống còn ~35KB).
2. **Chuẩn SEO & Accessibility**: 100% trang đạt điểm Lighthouse Accessibility $\ge 95$, không còn lỗi thiếu thẻ semantic hoặc vi phạm contrast.
3. **Thẩm mỹ Pro Developer**: Giao diện gọn gàng, trật tự Bento Box, không còn hiệu ứng lòe loẹt, chữ không bị dính và không gây mỏi mắt.
4. **Trạng thái thành phần**: Mọi nút bấm, input, card đều có đủ 8 trạng thái UI chuẩn (`default`, `hover`, `active`, `focus-visible`, `loading`, `disabled`, `empty`, `error`).
