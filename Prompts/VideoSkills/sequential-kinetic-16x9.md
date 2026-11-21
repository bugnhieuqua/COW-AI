# SKILL: Video Bài Giảng Màn Hình Ngang 16:9 Kinetic Typography + B-Roll (sequential-kinetic-16x9)
> Kế thừa từ mẫu `DaiHoi13` & `CuongLinh2011` (`1920x1080` Widescreen Kinetic Documentary / Lecture).

## 1. Hợp đồng Bố cục 16:9 Widescreen 3-Zone (`1920x1080`)
- **Zone 1 — Top Header & Progress Rail (`#header-bar`)**:
  - Bên trái: Huy hiệu kênh / dự án (`#eyebrow`).
  - Ở giữa: Huy hiệu chuyên mục đang thuyết trình (`SectionBadge` — tuyệt đối không ghi `"Cảnh ..."`).
  - Bên phải & thanh đáy: Chỉ báo tiến độ các phần (`#progress-fill` & chấm tiến trình).
- **Zone 2 — Full-Bleed B-Roll & Kinetic Typography Stage (`scene-layer`)**:
  - Ảnh nền tư liệu / AI B-Roll phủ kín màn hình với chuyển động `ken-burns` mượt và lớp phủ vignette tối làm nổi bật chữ.
  - Khối chữ động Kinetic Typography trung tâm: tiêu đề lớn `Be Vietnam Pro 900` (`line-height >= 1.35`, `padding-top >= 18px`, màu trực tiếp + `text-shadow`, **không** dùng `background-clip: text`), kết hợp số liệu phóng đại hai bên (`stat-counter`), trượt ngang (`slide-in`), chạy chữ (`marquee`) hoặc gõ phím (`typewriter`).
- **Zone 3 — Bottom Synchronized Caption & Optional Corner Host (`zone-captions`)**:
  - Thanh phụ đề thuyết minh rõ nét ở phần dưới cùng kèm từ khóa `[kw]...[/kw]` nổi bật và MC góc nhỏ (nếu có avatar).

## 2. Quy tắc Bắt buộc
- Chia nội dung tài liệu thành các **Chuyên mục / Mốc nội dung** tuần tự mạch lạc, độ dài mỗi phân đoạn khớp với tổng số phút mục tiêu.
- **Tuyệt đối loại bỏ chữ "Cảnh 1", "Cảnh 2"** khỏi tiêu đề hiển thị.
- Luôn cung cấp cả **Lời hiển thị (Caption)** và **Lời đọc TTS (ttsText)** đã phiên âm chuẩn tiếng Việt.
