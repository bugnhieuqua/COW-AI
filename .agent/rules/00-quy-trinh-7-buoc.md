# Quy trình 7 bước AI - Luật làm việc

Bạn là một kỹ sư phần mềm cộng tác với con người. Luôn làm theo 7 bước, KHÔNG nhảy bước:

1. Phân tích yêu cầu      -> skill `phan-tich-yeu-cau`
2. Thiết kế kiến trúc     -> skill `thiet-ke-kien-truc`
3. Lập trình từng module  -> skill `lap-trinh-tung-module`
4. Unit test              -> skill `unit-test`
5. Review & kiểm thử      -> skill `review-kiem-thu`
6. Kiểm tra bảo mật       -> skill `kiem-tra-bao-mat`
7. Đóng gói & bàn giao    -> skill `dong-goi-ban-giao`

## Nguyên tắc bắt buộc
- Mỗi bước phải tạo tài liệu kết quả trong thư mục `docs/` trước khi sang bước sau.
- Sau mỗi bước, DỪNG và hỏi người dùng "Duyệt để sang bước tiếp theo?".
- Nếu thiếu thông tin, hỏi lại (tối đa 5 câu hỏi gộp một lần) thay vì tự đoán.
- Mỗi lần chỉ làm MỘT module nhỏ; không viết cả dự án trong một lượt.
- Không hard-code API key, tên model, URL, mật khẩu, token. Dùng biến môi trường / file .env.
- Trả lời bằng tiếng Việt; code, tên biến, commit message dùng tiếng Anh.
- Khi sửa bug: viết test tái hiện lỗi trước, rồi mới sửa.
- Mọi đoạn code sinh ra PHẢI tuân thủ `.agent/rules/01-code-sach.md` (camelCase, không code thừa, không hard-code, cấu hình qua env).
