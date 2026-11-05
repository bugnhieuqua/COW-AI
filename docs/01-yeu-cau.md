# Tài liệu yêu cầu - COW.AI (Hệ sinh thái Trợ lý AI Đa Tác Vụ)

## 1. Mục tiêu & bối cảnh
- **Bối cảnh:** Nhu cầu người dùng về một không gian làm việc AI riêng tư, tốc độ cao, đa chức năng (Chat LLM, Viết code, Tóm tắt tài liệu, Tạo ảnh nghệ thuật, Tìm kiếm dữ liệu trực tiếp, Chuyển văn bản thành giọng nói).
- **Mục tiêu:** Xây dựng COW.AI trở thành nền tảng trợ lý AI cá nhân mạnh mẽ trên nền ASP.NET Core (.NET 10.0), tuân thủ triệt để nguyên tắc **"All chats are private"** (không lưu lịch sử trên server, 100% dữ liệu chat nằm tại trình duyệt người dùng) và **"Zero Hardcoded Prompts/SQL"** (toàn bộ template prompt tách riêng trong thư mục `Prompts/`).

## 2. Đối tượng người dùng (Persona)
- **Lập trình viên / Kỹ sư phần mềm:** Cần sinh mã nguồn, phân tích tệp code (`.cs`, `.py`, `.ts`, `.sql`), tìm đường đi ngắn nhất hoặc giải thuật phức tạp.
- **Nhà sáng tạo nội dung / Designer:** Cần tạo ảnh từ mô tả tiếng Việt (tự động dịch sang tiếng Anh cho model sinh ảnh Pollinations AI), thử nghiệm các prompt hình ảnh nghệ thuật.
- **Nhân viên văn phòng / Nghiên cứu sinh:** Cần tóm tắt văn bản dài theo nhiều phong cách (ngắn gọn, chi tiết, gạch đầu dòng), chuyển đổi câu trả lời thành giọng đọc tiếng Việt tự nhiên (Microsoft Edge TTS).
- **Người dùng phổ thông:** Cần tra cứu kiến thức nhanh, tìm kiếm web thời gian thực (DuckDuckGo), nhập liệu bằng giọng nói (Voice STT).

## 3. Yêu cầu chức năng
| Mã | Mô tả | Ưu tiên (MoSCoW) |
|----|-------|------------------|
| FR-01 | Trò chuyện trực tuyến dạng Server-Sent Events (SSE) với đa model (Gemini 2.0 Flash, DeepSeek R1, Llama 3 8B, Auto Route). | Must |
| FR-02 | Tạo hình ảnh tự động từ mô tả bằng Pollinations AI; tự động dịch prompt tiếng Việt sang tiếng Anh trước khi gửi. | Must |
| FR-03 | Tách 100% prompt hệ thống và prompt sinh mã/tóm tắt ra thư mục ngoài `Prompts/` (Zero Hardcoded Prompts). | Must |
| FR-04 | Tìm kiếm web trực tiếp qua DuckDuckGo Service khi người dùng yêu cầu tra cứu thông tin mới. | Must |
| FR-05 | Phát giọng nói tự nhiên Text-to-Speech (Edge TTS tiếng Việt nam/nữ) trực tiếp trên giao diện chat. | Must |
| FR-06 | Lưu trữ và quản lý lịch sử hội thoại trên LocalStorage (Chat Drawer Sidebar), cho phép xuất Markdown (`.md`) và tạo đoạn chat mới. | Must |
| FR-07 | Nhập liệu bằng giọng nói (Voice Speech-to-Text) qua Web Speech API tiếng Việt. | Should |
| FR-08 | Đính kèm tệp văn bản / mã nguồn (`.txt`, `.md`, `.cs`, `.py`, `.sql`) đọc trực tiếp tại client và kèm vào câu lệnh. | Should |
| FR-09 | Tùy chỉnh tham số mô hình (`temperature`, `maxTokens`) trực tiếp từ dropdown chọn Model. | Should |
| FR-10 | Tóm tắt tài liệu văn bản (`/Document/Summary`) theo 3 phong cách (brief, detailed, bullets). | Must |
| FR-11 | Trợ lý sinh mã nguồn (`/Document/CodeGen`) hỗ trợ C#, Python, JavaScript, TypeScript, Go. | Must |

## 4. Yêu cầu phi chức năng
- **Bảo mật & Quyền riêng tư (Privacy-by-Design):** Toàn bộ tin nhắn chat của người dùng không được lưu vào bất kỳ cơ sở dữ liệu backend nào; chỉ nằm ở LocalStorage máy cá nhân người dùng.
- **Không Hardcoded SQL / Prompts:** Nghiêm cấm đưa câu lệnh prompt hoặc SQL trực tiếp vào file `.cs` hoặc `.js`.
- **Hiệu năng & Trải nghiệm (60 FPS UX):**
  - Luồng SSE stream mượt mà, áp dụng cơ chế throttle highlight Prism.js khi nhận token liên tục để tránh giật lag DOM.
  - Chuyển đổi trạng thái 2-state (Hero Header -> Dock Bottom Mode) mượt mà với CSS transitions.
- **Khả năng tương thích:** Chạy tốt trên Chrome, Edge, Safari, Firefox trên cả máy tính và thiết bị di động.

## 5. User story & tiêu chí chấp nhận
### US-01: Chat thời gian thực với LLM
- *Là* lập trình viên, *tôi muốn* gửi câu hỏi kỹ thuật và nhận phản hồi tức thời dạng chữ chạy, *để* tôi không phải chờ đợi phản hồi toàn bộ.
  - **Given** người dùng ở giao diện chat với model Gemini/DeepSeek.
  - **When** người dùng nhập câu hỏi và bấm "Gửi".
  - **Then** hệ thống mở kết nối SSE `/Home/StreamChat`, hiển thị typing indicator và cập nhật token phản hồi kèm highlight code.

### US-02: Nhập liệu giọng nói tiếng Việt
- *Là* người dùng đang bận tay, *tôi muốn* nói bằng tiếng Việt để hệ thống tự điền vào khung prompt, *để* tiết kiệm thời gian gõ phím.
  - **Given** micro được cấp quyền trên trình duyệt.
  - **When** người dùng bấm nút micro `#voiceInputBtn` và nói tiếng Việt.
  - **Then** nút micro nhấp nháy đỏ (`listening`), văn bản được hiển thị theo thời gian thực vào ô textarea.

### US-03: Đính kèm tệp phân tích
- *Là* kỹ sư, *tôi muốn* chọn tệp code (`.cs`, `.py`) đính kèm vào khung chat, *để* AI phân tích mà không cần copy paste thủ công.
  - **Given** tệp văn bản có kích thước nhỏ hơn 512 KB.
  - **When** người dùng chọn tệp từ nút đính kèm `#attachBtn`.
  - **Then** chip tệp hiển thị tên và dung lượng; khi gửi câu hỏi, mã nguồn trong tệp được đóng gói tự động vào ngữ cảnh gửi tới AI.

### US-04: Lưu trữ và xuất lịch sử hội thoại
- *Là* người dùng thường xuyên, *tôi muốn* xem lại các phiên chat cũ và xuất ra file Markdown, *để* lưu trữ tài liệu nghiên cứu cá nhân.
  - **Given** người dùng đã thực hiện nhiều cuộc hội thoại.
  - **When** người dùng mở thanh trượt Sidebar `#chatHistoryDrawer` và chọn "Xuất .md".
  - **Then** một tệp `.md` chứa đầy đủ nội dung phiên chat được tải xuống máy tính.

## 6. Phạm vi MVP / Ngoài phạm vi
- **Trong phạm vi (In Scope):**
  - Trò chuyện đa mô hình qua OpenRouter (Gemini, DeepSeek, Llama3).
  - Tự động điều hướng câu hỏi (Auto Routing).
  - Tạo ảnh Pollinations AI kèm dịch tiếng Việt -> tiếng Anh.
  - Tìm kiếm DuckDuckGo tích hợp vào chat.
  - Text-to-Speech (Edge TTS) và Speech-to-Text (Web Speech API).
  - Client-side history drawer & export Markdown.
  - Đính kèm tệp văn bản/code tại trình duyệt.
  - Quản lý template prompt trong `Prompts/`.
- **Ngoài phạm vi (Out of Scope):**
  - Hệ thống đăng nhập / tài khoản người dùng trên máy chủ (để đảm bảo tiêu chí zero data footprint).
  - Lưu cơ sở dữ liệu quan hệ (SQL Server/Postgres) cho tin nhắn chat.
  - Fine-tuning model cục bộ trên máy chủ.

## 7. Ràng buộc, giả định, rủi ro
- **Ràng buộc:** Nền tảng .NET 10.0 ASP.NET Core, chạy trên môi trường Windows/Linux, bảo đảm không lộ OpenRouter API Key ra client.
- **Giả định:** Người dùng có kết nối Internet để gọi OpenRouter, Pollinations và DuckDuckGo.
- **Rủi ro & Biện pháp giảm thiểu:**
  - *Rủi ro:* Rate limit hoặc quá tải từ nhà cung cấp API bên ngoài.
  - *Giảm thiểu:* Cơ chế thử lại (exponential backoff) và thông báo lỗi thân thiện chuẩn SEO, không bao giờ lộ Exception kỹ thuật ra UI.

## 8. Trạng thái phê duyệt
- **Bước SDLC:** Bước 1 - Phân tích yêu cầu (Hoàn tất).
- **Tiến trình:** Đã duyệt để chuyển tiếp sang Bước 2 - Thiết kế kiến trúc.
