# SKILL: Video So Sánh / Đối Chiếu 3-Zone (compare-3zone)
> Kế thừa chuẩn mực từ `auto-compare-video/DESIGN.md` & `create-video/SKILL.md`.

## 1. Hợp đồng Bố cục 3-Zone (3-Zone Layout Contract)
- **Top — Comparison Zone (`zone-cards`)**: 2 thẻ đối chiếu song song (`Card A` bên trái vs `Card B` bên phải), huy hiệu tròn `VS` ở giữa mối nối. Tên khái niệm sử dụng màu `--accent-cyan` (`#37E6C4`).
- **Middle — Kinetic Caption Zone (`zone-captions`)**: Chỉ hiển thị **1 dòng phụ đề tại một thời điểm** (`<= 14 từ/dòng`), từ khóa tạo nên sự khác biệt được bọc trong `[kw]...[/kw]` (màu `--accent-pink` `#FF4FA3`).
- **Bottom — Avatar Host Zone (`zone-avatar`)**: Nhân vật 2D / Giảng viên AI đổi tư thế theo từng nhịp (`point-left`, `point-right`, `shrug`, `explain`, `neutral`).

## 2. Nhịp Beat Chuẩn (6-Beat Comparison Rhythm)
- **Beat 1 (`hook`)**: Giới thiệu trực diện Khái niệm A và Khái niệm B (2 nhịp).
- **Beat 2 (`question`)**: Đặt câu hỏi nút thắt kích thích tò mò ("Điểm khác biệt cốt lõi nằm ở đâu?").
- **Beat 3 (`reveal-a`)**: Giải mã Khái niệm A (Định nghĩa -> Đặc trưng kỹ thuật -> Ví dụ thực tế). Card A sáng rõ (`scale: 1.04`), Card B mờ (`opacity: 0.55`).
- **Beat 4 (`reveal-b`)**: Giải mã Khái niệm B (Định nghĩa -> Đặc trưng kỹ thuật -> Ví dụ thực tế). Card B sáng rõ (`scale: 1.04`), Card A mờ (`opacity: 0.55`).
- **Beat 5 (`compare`)**: Đối chiếu song song trực tiếp 2 vế ("Một bên X — một bên Y"). Cả 2 card sáng `100%`.
- **Beat 6 (`payoff`)**: Câu đúc kết ngắn gọn, sắc bén, dễ nhớ, giữ khung hình tới kết thúc (`hold`).

## 3. Công thức Timing & Quy tắc Bắt buộc
- `start[1] = 0.55s`; `gap` cùng beat = `0.32s`; `gap` chuyển beat = `0.42s`; `outro_hold = 1.3s`.
- **TUYỆT ĐỐI KHÔNG dùng chữ "Cảnh", "Cảnh 1", "Cảnh 2"** trong tiêu đề hoặc huy hiệu hiển thị. Dùng tên Beat / Chuyên mục cụ thể (ví dụ: `[Hook] Khái niệm A & B`, `[Giải mã A] Kiến trúc cốt lõi`, `[Đối chiếu] Hiệu năng thực tế`).
- Tách riêng **Lời hiển thị (Caption)** giữ nguyên chính tả thuật ngữ gốc và **Lời đọc TTS (ttsText)** đã phiên âm tiếng Việt cho các từ viết tắt tiếng Anh.
