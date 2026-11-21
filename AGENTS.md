# COW.AI — Agent UI/UX Engineering Operating System (AGENTS.md)

> **Mục tiêu**: Hướng dẫn toàn diện cho mọi Agent / AI Assistant khi tiếp nhận bất kỳ tác vụ nào liên quan đến Giao diện (UI), Trải nghiệm người dùng (UX), Luồng tương tác (Flows), Styling (CSS/Three.js) và Thành phần giao diện (Components) trong dự án **COW.AI**.
> 
> **Kiến trúc cốt lõi**: Kết hợp sức mạnh hiệp đồng của 2 UI/UX skills hàng đầu:
> 1. 🧠 **`ux-psych-design`** — *The Cognitive Architect & Systems Ergonomist* (Nền tảng tâm lý học nhận thức, 6-Stage Journey, FAANG benchmarks, 3 Aesthetic Dials, 8 Component States).
> 2. ✨ **`impeccable`** — *The Design Director & Craft Engine* (Tiêu chuẩn thẩm mỹ vượt trội, Surface Modes, Anti-Slop Detector CLI cơ học, hệ động từ tinh chỉnh: `polish`, `quieter`, `harden`, `typeset`, `audit`).

---

## 1. Phân vai và Ma trận trách nhiệm (Dual-Engine Synergy)

Hai skill không hoạt động rời rạc mà bổ trợ tuần tự theo mô hình **"Kiến trúc nhận thức trước, Chế tác sắc bén sau"**:

| Trụ cột | 🧠 `ux-psych-design` | ✨ `impeccable` |
| :--- | :--- | :--- |
| **Vai trò chính** | Tổng công trình sư trải nghiệm & Nhà tâm lý nhận thức | Giám đốc thiết kế mỹ thuật & Kỹ sư chế tác chi tiết |
| **Trách nhiệm** | - Khám phá JTBD & mô hình tư duy người dùng.<br>- 30 định luật tâm lý học nhận thức.<br>- Kiến trúc 6 giai đoạn người dùng (Entry → Peak-End).<br>- Định lượng 3 Taste Dials (Variance, Motion, Density).<br>- Đầy đủ 8 trạng thái thành phần giao diện. | - Định vị Surface Mode (`Operate`, `Read`, `Persuade`).<br>- Giữ chuẩn chất lượng tối cao (Craft Floor).<br>- Quét lỗi AI Slop bằng CLI cơ học (`impeccable detect`).<br>- Tinh chỉnh chi tiết (`quieter`, `harden`, `typeset`, `polish`).<br>- Quản lý `PRODUCT.md` & `DESIGN.md`. |
| **Khi nào kích hoạt** | Bắt đầu tính năng mới, tái cấu trúc layout, thiết kế luồng tương tác, tối ưu hóa tải nhận thức. | Khi định nghĩa design token, bắt đầu viết CSS/HTML, quét lỗi slop cơ học, tinh chỉnh chi tiết trước khi xuất xưởng. |

---

## 2. Quy trình 4 bước chuẩn mực khi làm việc với UI/UX (Standard 4-Step Pipeline)

Mỗi khi nhận yêu cầu sửa đổi hoặc xây dựng giao diện trong COW.AI, Agent **bắt buộc** thực hiện theo 4 bước sau:

```
[ BƯỚC 1: NHẬN THỨC & LUỒNG ] ──▶ [ BƯỚC 2: RÀNG BUỘC & TOKENS ] ──▶ [ BƯỚC 3: HIỆN THỰC MÃ NGUỒN ] ──▶ [ BƯỚC 4: QUÉT CLI & TINH CHỈNH ]
      (ux-psych-design)                  (impeccable)                         (Dual Execution)                 (impeccable detect & refine)
```

### Bước 1: Kiến trúc nhận thức & Luồng tương tác (`ux-psych-design`)
1. **Thiết lập Scope Lock & JTBD**:
   - Chỉ làm đúng màn hình, component và hành vi người dùng yêu cầu (không vẽ thêm trang/tính năng ngoài phạm vi).
   - Với COW.AI, người dùng cốt lõi là **Developer/Engineer** cần không gian chat thông minh, nhanh, bảo mật và thuần kỹ thuật.
2. **Hiệu chỉnh 3 Taste Dials cho COW.AI Workspace**:
   - `DESIGN_VARIANCE`: **5/10** (Cấu trúc Bento Box cân đối, trật tự kỹ thuật, không lộn xộn).
   - `MOTION_INTENSITY`: **4/10** (Chuyển động mượt mà ~300-400ms chuẩn CSS cubic-bezier, hỗ trợ nhận thức khi Prompt Bar thu xuống đáy, Three.js 60 FPS nhẹ nhàng).
   - `VISUAL_DENSITY`: **7–8/10** (Môi trường Pro Developer: khoảng cách chặt chẽ, tối ưu diện tích hiển thị code và hội thoại, giảm cuộn trang vô ích).
3. **Áp dụng các định luật tâm lý nhận thức cốt lõi**:
   - **Doherty Threshold (<400ms)**: Phản hồi SSE streaming token đầu tiên tức thì; nếu trễ hiển thị ngay skeleton/indicator.
   - **Fitts's Law**: Thanh prompt bar và nút Submit to, rõ ràng, dễ click/gõ phím ở vị trí công thái học.
   - **Hick's Law**: Bộ chọn mô hình (Model Selector) gọn gàng, có gợi ý rõ ràng (DeepSeek R1 cho Code/Toán, Gemini cho hội thoại chung), tránh gây ngợp.
   - **Jakob's Law**: Duy trì hành vi quen thuộc (Enter = Gửi, Shift+Enter = Xuống dòng, nút Copy code ở góc trên phải khối code).

### Bước 2: Xác định Surface Mode & Hệ thống Tokens (`impeccable`)
1. **Xác định Surface Mode**:
   - Màn hình chính (`Views/Home/Index.cshtml`): **`Operate`** (Môi trường tác vụ chuyên sâu: tính scanable cao, tôn trọng công năng, độ chính xác và kiểm soát của lập trình viên).
   - Trang tài liệu / Báo cáo (`docs/`): **`Read`** (Tối ưu khả năng đọc, cấu trúc rõ ràng, typography chuẩn mực).
   - Trang giới thiệu / Landing (nếu có): **`Persuade`** (Thuyết phục bằng sự tin cậy và cam kết bảo mật 100% Client-Side).
2. **Đồng bộ với `PRODUCT.md` & `DESIGN.md`**:
   - Tham chiếu [PRODUCT.md](PRODUCT.md) để bảo đảm đúng định vị sản phẩm (All chats are private, Zero Hardcoded Prompts, Developer-centric).
   - Định nghĩa/Kế thừa CSS Variables trong `wwwroot/css/`:
     - Tỷ lệ màu **60-30-10**: 60% nền trung tính (Dark `#0b0f19` / Light `#f8fafc`), 30% bề mặt thẻ & chữ, 10% màu nhấn (Accent Teal/Cyan hoặc Sky Blue cho developer).

### Bước 3: Lập trình giao diện & 8 Trạng thái thành phần (Build Phase)
1. **Đầy đủ 8 trạng thái UI (State Machine completeness)**:
   Mọi thành phần tương tác (nút bấm, input, chat bubble, dropdown) phải có đủ:
   `default` ➔ `hover` ➔ `active` ➔ `focus-visible` ➔ `loading` ➔ `disabled` ➔ `empty` ➔ `error`.
2. **Tuân thủ quy chuẩn Accessibility**:
   - Đạt chuẩn **WCAG 2.2 AA** (Độ tương phản chữ tối thiểu 4.5:1 với text thường, 3:1 với text lớn/UI components).
   - Keyboard Navigation hoàn hảo (`outline` khi focus bằng bàn phím qua `:focus-visible`, không làm mất focus ring).

### Bước 4: Kiểm tra cơ học CLI & Tinh chỉnh sắc bén (`impeccable`)
Trước khi bàn giao bất kỳ file HTML/CSS/JS nào:
1. **Chạy Detector quét lỗi Slop tự động**:
   ```powershell
   & ".agent/skills/impeccable/scripts/impeccable.cmd" detect --json wwwroot/css/components.css wwwroot/css/pages.css
   ```
2. **Kích hoạt các lệnh tinh chỉnh khi cần**:
   - `/impeccable quieter`: Triệt tiêu các hiệu ứng loè loẹt, gradient text vô nghĩa, viền dày kiểu AI-slop.
   - `/impeccable harden`: Rà soát trạng thái ngắt mạng SSE, token stream bị đứt, lỗi clipboard khi copy code, tràn văn bản dài.
   - `/impeccable typeset`: Rà soát font monospace cho code (Fira Code, JetBrains Mono, Cascadia Code), line-height và phân cấp heading.
   - `/impeccable polish`: Hoàn thiện nước bóng cuối cùng trước khi đóng PR/commit.

---

## 3. Bộ quy tắc cấm kỵ Anti-Slop (Zero-Tolerance Rules)

Tuyệt đối **KHÔNG** đưa các mẫu phản cảm và "tố cáo mã AI" vào dự án COW.AI:

| ❌ Bị cấm (AI Slop Anti-Patterns) | ✅ Chuẩn kỹ thuật (Craftsmanship Standard) |
| :--- | :--- |
| **Gradient Text trên tiêu đề/nội dung**: `background-clip: text` với gradient tím-hồng-xanh. | Dùng màu đơn sắc có độ tương phản cao, rõ ràng, dễ đọc cho lập trình viên. |
| **Thẻ viền màu bên cạnh (Side-tab accent border)**: `border-left: 3px solid #38bdf8` trên card. | Dùng màu nền bề mặt phân tầng (Tonal Layering) hoặc viền mỏng 1px đồng nhất `rgba(255,255,255,0.08)`. |
| **Card phình to, padding lãng phí không gian**: Padding `32px` hay `48px` cho các thẻ nhỏ. | Padding cân đối theo nhịp 8px (`12px`, `16px`, tối đa `24px` cho container lớn). |
| **Bóng đổ phát sáng neon (Over-saturated glow)**: `box-shadow: 0 0 30px #a855f7`. | Đổ bóng tự nhiên nhiều lớp mềm mịn hoặc viền phân cách tinh tế (Subtle Border). |
| **Nút bấm bo tròn quá đà không chủ đích**: Pill button `border-radius: 9999px` tràn lan. | Border-radius chuẩn mực `6px`–`10px` cho giao diện công cụ chuyên nghiệp. |
| **Mất dấu vết bàn phím**: `outline: none` mà không có `:focus-visible` thay thế. | Giữ nguyên hoặc tùy biến vòng focus sắc nét cho lập trình viên dùng bàn phím. |

---

## 4. Đặc thù kỹ thuật & Vị trí tệp trong COW.AI

Khi thao tác với giao diện của COW.AI, Agent cần nắm rõ cấu trúc thư mục thực tế:

```
D:\COW-AI/
├── Views/
│   ├── Home/
│   │   └── Index.cshtml            # Màn hình Chat chính (Centered Hero & Bottom-Docked Prompt)
│   └── Shared/
│       ├── _Layout.cshtml          # Shell chung, Navbar, Three.js Canvas container
│       └── _ModelSelector.cshtml   # Dropdown chọn mô hình AI (DeepSeek, Gemini, v.v.)
├── wwwroot/
│   ├── css/
│   │   ├── pages.css               # Layout toàn trang, hiệu ứng chuyển đổi Centered <-> Bottom
│   │   └── components.css          # Chat bubbles, input box, audio player, code blocks
│   └── js/
│       ├── chat-logic.js           # Xử lý SSE stream, markdown marked.js, Prism code highlight
│       └── bg-animation.js         # Three.js background canvas 60 FPS
├── Prompts/                        # Hệ thống Dynamic Prompts (Zero Hardcoded Prompts)
├── PRODUCT.md                      # Hồ sơ sản phẩm chuẩn mực do impeccable quản lý
└── .agent/skills/                  # Thư mục cài đặt các kỹ năng agent
    ├── ux-psych-design/            # Skill kiến trúc tâm lý học nhận thức
    └── impeccable/                 # Skill chế tác, kiểm tra chất lượng & CLI detector
```

---

## 5. Bảng tra cứu lệnh nhanh cho Agent

| Tình huống tác vụ | Lệnh / Hành động khuyến nghị |
| :--- | :--- |
| Đang viết kế hoạch (`/plan`) và có câu hỏi hoặc phương án đề xuất | **Bắt buộc** gọi công cụ `ask_question` để mở **Form đề xuất tương tác** cho người dùng kiểm duyệt trực tiếp (theo skill `plan-proposal-form`). |
| Thiết kế màn hình mới hoặc luồng tương tác mới | Mở `ux-psych-design` chạy **Phase 1 (DISCOVER)** & **Phase 2 (FLOW)**, xác định 3 Taste Dials. |
| Kiểm tra lại định hướng sản phẩm & bối cảnh người dùng | Đọc file [PRODUCT.md](PRODUCT.md) do `impeccable init` tạo. |
| Đồng bộ hệ thống token & trích xuất phong cách | Chạy `/impeccable document` để cập nhật `DESIGN.md`. |
| Vừa code xong 1 tệp CSS/HTML | Chạy `& ".agent/skills/impeccable/scripts/impeccable.cmd" detect --json <file>` để quét slop. |
| Giao diện bị chê loè loẹt, nhiều hiệu ứng thừa | Chạy `/impeccable quieter <target>` kết hợp `references/06-anti-slop-taste.md`. |
| Xử lý trạng thái lỗi, ngắt mạng, ranh giới dữ liệu | Chạy `/impeccable harden <target>` kết hợp `Phase 4: Resilient States` của `ux-psych-design`. |
| Cải thiện font chữ, khối code, khoảng cách đọc | Chạy `/impeccable typeset <target>`. |
| Chuẩn bị nghiệm thu bàn giao | Chạy `/impeccable polish <target>` và rà soát lại **WCAG 2.2 AA**. |
