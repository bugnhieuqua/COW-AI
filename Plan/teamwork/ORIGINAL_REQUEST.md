# Original User Request

## 2026-10-27T01:54:50Z

Tái cấu trúc giao diện trang chủ COW.AI (`Home/Index.cshtml`) từ dạng "Form nhập liệu tĩnh" sang "Giao diện Chat tương tác chuẩn Duck.ai": thanh Prompt Bar ban đầu nằm chính giữa màn hình kèm các thẻ gợi ý, khi người dùng gửi tin nhắn sẽ trượt mượt mà xuống đáy, nhường không gian cho khung lịch sử hội thoại (Chat History) với bong bóng chat cuộn tự động và streaming SSE thời gian thực (hỗ trợ cả văn bản và ảnh).

Working directory: d:/COW-AI
Integrity mode: development

## Requirements

### R1. Cấu trúc Giao diện 2 trạng thái (Centered & Bottom-Docked Chat Layout)
- Thiết kế layout flexbox toàn màn hình trong `Home/Index.cshtml` và `pages.css`:
  - **Trạng thái ban đầu:** Thanh Prompt Bar nằm chính giữa màn hình (centered), bên dưới là các thẻ gợi ý tác vụ (suggestion cards) và mô tả.
  - **Trạng thái Chat:** Khi gửi tin nhắn đầu tiên, thanh Prompt Bar trượt mượt mà (smooth CSS cubic-bezier transition) xuống đáy màn hình (`bottom-mode`), ẩn các thẻ gợi ý, và hiển thị khung lịch sử hội thoại `chat-history`.
  - Giữ nguyên tích hợp Three.js background, Navbar và bộ chọn Model AI hiện có.

### R2. Trải nghiệm Hội thoại & Hiển thị Bong bóng Chat (Chat History & Bubbles)
- Khu vực `chat-history` hỗ trợ:
  - Bong bóng tin nhắn của User căn lề phải (nền nổi bật, bo góc).
  - Bong bóng tin nhắn của AI căn lề trái (nền glassmorphism, viền mờ).
  - Tự động cuộn xuống đáy (`chatHistory.scrollTop = chatHistory.scrollHeight`) khi có token mới được stream về.
  - Hỗ trợ hiển thị Markdown và thẻ `<img>` trong bong bóng AI khi nhận kết quả tạo ảnh từ Pollinations.

### R3. Luồng Streaming SSE Phía Backend & Điều phối Tác vụ
- Cập nhật `HomeController.cs` (Action `StreamChat`):
  - Nhận diện linh hoạt tác vụ Chat văn bản hoặc Tạo ảnh (dựa vào từ khóa hoặc model lựa chọn).
  - Trả về luồng SSE (`text/event-stream`), stream từng token text từ OpenRouter, hoặc đóng gói payload URL ảnh dưới dạng SSE payload và kết thúc với `data: [DONE]`.

### R4. Xử lý Logic Phía Client (JavaScript)
- Cập nhật `chat-logic.js`:
  - Lắng nghe sự kiện gửi tin nhắn (phím Enter hoặc nút Gửi).
  - Thêm tin nhắn User ngay lập tức, kích hoạt chuyển đổi layout sang `bottom-mode`.
  - Đọc luồng SSE bằng `ReadableStream` / `TextDecoder`, cập nhật trực tiếp nội dung vào bong bóng AI mà không gây giật lag giao diện.

## Acceptance Criteria

### Giao diện & Hiệu ứng chuyển động (UI/UX)
- [ ] Khi tải trang lần đầu, thanh Prompt Bar nằm chính giữa màn hình cùng các thẻ gợi ý.
- [ ] Khi gửi câu hỏi đầu tiên, thanh Prompt Bar chuyển động mượt mà xuống sát đáy màn hình trong ~0.5s và các thẻ gợi ý tự động ẩn đi.
- [ ] Khung Chat History xuất hiện ở phần trên, hiển thị tin nhắn User bên phải và tin nhắn phản hồi của AI bên trái.

### Xử lý Dữ liệu & Streaming
- [ ] Phản hồi dạng văn bản hiển thị theo thời gian thực (từng từ/cụm từ xuất hiện liên tục, không bị đơ trình duyệt).
- [ ] Khung hội thoại tự động cuộn xuống dưới cùng khi nội dung câu trả lời dài ra.
- [ ] Phản hồi tạo ảnh hiển thị trực tiếp ảnh đã tạo ngay trong bong bóng chat của AI.
- [ ] Gõ Enter hoặc bấm nút Gửi hoạt động nhất quán; trường nhập liệu bị khóa tạm thời trong lúc gửi và sẵn sàng nhập tiếp sau khi AI hoàn tất.

### Tính toàn vẹn của mã nguồn
- [ ] Dự án biên dịch thành công với `dotnet build` đạt 0 lỗi (0 errors).
- [ ] Không làm hỏng bộ chọn Model (`_ModelSelector`), hiệu ứng nền Three.js hay chức năng chuyển đổi Theme Dark/Light.


## 2026-10-27T04:17:42Z

This is a single self-contained fix; keep it small and focused.

Nâng cấp hệ thống COW.AI kết hợp 2 mục tiêu: (1) Chuẩn hóa cơ chế xử lý lỗi an toàn (Error Handling) từ Backend đến Frontend, ẩn toàn bộ thông báo kỹ thuật/Exception thô và thay bằng thông báo thân thiện người dùng; (2) Tối ưu hóa giao diện bộ chọn Model (`_ModelSelector`), loại bỏ dòng mô tả dài để chống tràn màn hình, bổ sung các tùy chọn Auto, OmniRoute và DuckDuckGo Search cùng cơ chế xử lý tương ứng ở backend.

Working directory: d:/COW-AI
Integrity mode: development

## Requirements

### R1. Xử lý Lỗi An toàn & Ẩn Thông tin Kỹ thuật (Error Handling & Security)
- **Backend:**
  - Không văng Exception thô, StackTrace hoặc thông báo cấu hình nội bộ (như thiếu Key, sai model ID) ra giao diện người dùng.
  - Sử dụng `ILogger` ghi log chi tiết lỗi cho lập trình viên/quản trị viên.
  - Cập nhật `OpenRouterService.cs` và `HomeController.cs`: Bọc try-catch chặt chẽ, khi có lỗi sẽ trả về HTTP status phù hợp hoặc định dạng JSON/SSE an toàn với thông báo thân thiện (VD: *"Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút."*).
- **Frontend (`chat-logic.js`):**
  - Kiểm tra `response.ok`, hiển thị thông báo lỗi màu đỏ trang nhã, không render chuỗi lỗi kỹ thuật C# hoặc thông báo cấu hình nhạy cảm.

### R2. Tinh gọn Giao diện Model Selector & Bổ sung OmniRoute/DuckDuckGo
- **Giao diện (`_ModelSelector.cshtml`):**
  - Xóa bỏ các thẻ mô tả dài `<span class="model-option__desc">` để menu ngắn gọn, thanh lịch.
  - Bổ sung tùy chọn `Auto (Tự động)`, `OmniRoute` và `DuckDuckGo Search` bên cạnh các model hiện có (Gemini, Llama 3, DeepSeek, Pollinations).
- **CSS (`components.css`):**
  - Thu hẹp kích thước `.model-menu` (chiều rộng ~240px, chiều cao tối đa `max-height: 320px`), thêm `overflow-y: auto` và thanh cuộn mỏng (6px) chống tràn màn hình.
  - Tối ưu padding và font-size của `.model-option` (~13px) để hiển thị sắc nét, vừa vặn.

### R3. Điều phối Xử lý Model Mới tại Backend (`HomeController.cs`)
- Mở rộng xử lý trong `StreamChat` khi nhận lựa chọn:
  - `auto`: Tự động định tuyến sang model tối ưu nhất theo nội dung câu hỏi.
  - `omniroute`: Định tuyến qua luồng tổng hợp OmniRoute.
  - `duckduckgo`: Giả lập/tra cứu thông tin web và trả về luồng dữ liệu text hiển thị trực tiếp trong bong bóng chat AI.

## Acceptance Criteria

### [Bảo mật & Trải nghiệm Người dùng (UX/Error Handling)]
- [ ] Khi xảy ra sự cố (chưa cấu hình API Key, lỗi mạng hoặc lỗi API OpenRouter), giao diện hiển thị thông báo thân thiện màu đỏ, tuyệt đối không xuất hiện từ khóa kỹ thuật C#, `InvalidOperationException`, `StackTrace` hay lộ API Key.
- [ ] Mọi lỗi hệ thống được ghi log đầy đủ qua `ILogger` ở phía backend.

### [Giao diện Bộ chọn Model (Model Selector UI)]
- [ ] Menu chọn model hiển thị gọn gàng, không bị tràn màn hình ở mọi độ phân giải; có thanh cuộn mượt khi số lượng model vượt quá chiều cao tối đa.
- [ ] Không còn dòng chữ mô tả phụ bên dưới tên model; danh sách bao gồm: Auto, Gemini 2.0 Flash, Llama 3 8B, DeepSeek R1, Pollinations AI, OmniRoute, DuckDuckGo Search.

### [Tính tương thích & Toàn vẹn Mã nguồn]
- [ ] Dự án biên dịch thành công với `dotnet build` đạt 0 lỗi (0 errors).
- [ ] Không làm gián đoạn hiệu ứng trượt 2 trạng thái Duck.ai (`bottom-mode`), Three.js background hay chức năng tạo ảnh Pollinations.
