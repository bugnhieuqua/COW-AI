---
description: Dọn sạch code - đổi tên camelCase, xóa code thừa/demo, đưa hard-code vào env
---

Áp dụng `.agent/rules/01-code-sach.md` cho toàn bộ mã nguồn hiện có:

1. Chạy các lệnh quét ở mục 4 của rule và liệt kê mọi vi phạm theo file:dòng.
2. Xóa code thừa: code bị comment, import/biến/hàm không dùng, console.log, code demo/mẫu, localStorage/sessionStorage không dùng.
3. Đưa mọi giá trị hard-code (API key, tên model, URL, port, chuỗi kết nối) vào lớp config đọc từ env; cập nhật `.env.example`; kiểm tra `.env` nằm trong `.gitignore`.
4. Đổi tên không đúng chuẩn sang camelCase (class/component dùng PascalCase), cập nhật mọi chỗ tham chiếu.
5. Chạy lại toàn bộ test, đảm bảo không hồi quy.
6. Báo cáo ngắn: số vi phạm đã sửa, vi phạm còn lại (kèm lý do). Nếu phát hiện key từng bị commit, yêu cầu thu hồi/đổi key.
