---
name: lap-trinh-tung-module
description: Dùng ở BƯỚC 3 để lập trình từng module một theo kế hoạch trong docs/03-ke-hoach-module.md, có AI hỗ trợ viết code sạch, đúng kiến trúc, mỗi lần chỉ một module.
---

# Bước 3 - Lập trình từng module (có AI hỗ trợ)

## Điều kiện đầu vào
`docs/02-kien-truc.md` và `docs/03-ke-hoach-module.md` đã được duyệt.

## Quy trình cho MỖI module
1. Chọn module tiếp theo trong kế hoạch (chưa tick hoàn thành). Nói rõ đang làm module nào.
2. Nhắc lại phạm vi: đầu vào, đầu ra, phụ thuộc, tiêu chí hoàn thành.
3. Liệt kê các file sẽ tạo/sửa TRƯỚC khi viết code, chờ người dùng đồng ý nếu thay đổi lớn.
4. Viết code:
   - Tuân thủ cấu trúc thư mục và quy ước đặt tên trong tài liệu kiến trúc
   - Hàm ngắn, một trách nhiệm; tách logic nghiệp vụ khỏi I/O
   - Validate dữ liệu đầu vào, xử lý lỗi rõ ràng, log hợp lý
   - Có docstring/comment cho phần logic không hiển nhiên
   - Đặt tên camelCase (ưu tiên), PascalCase cho class/component, theo `.agent/rules/01-code-sach.md`
   - KHÔNG hard-code API key, tên model, URL, port, chuỗi kết nối...: đọc từ env qua lớp config; biến mới phải thêm vào `.env.example`
   - Không viết code demo/mẫu, không để code thừa (localStorage không dùng, console.log, code bị comment, import thừa)
5. Chạy thử module (chạy lệnh thật, đọc kết quả) và sửa lỗi cú pháp/chạy được.
6. Chạy các lệnh quét ở mục 4 của `.agent/rules/01-code-sach.md`, xóa mọi code thừa/hard-code còn sót.
7. Cập nhật tick `[x]` trong `docs/03-ke-hoach-module.md` và ghi chú ngắn những gì đã làm.
8. Chuyển sang Bước 4 (unit test) cho CHÍNH module này trước khi làm module kế tiếp.

## Cấm
- Không viết nhiều module trong một lượt.
- Không sửa module khác khi chưa báo và được đồng ý.
- Không thêm thư viện mới mà không giải thích lý do.
- Không để lại code demo/mẫu hoặc giá trị hard-code "tạm thời".

## Kết thúc mỗi module
Tóm tắt: file đã đổi, cách chạy thử, điểm cần chú ý. Hỏi: "Tiếp tục viết unit test cho module này?"
