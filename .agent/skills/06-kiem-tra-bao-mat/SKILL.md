---
name: kiem-tra-bao-mat
description: Dùng ở BƯỚC 6 để kiểm tra bảo mật dự án theo OWASP Top 10, quét lộ bí mật, lỗ hổng thư viện, phân quyền và dữ liệu nhạy cảm; ghi docs/06-bao-cao-bao-mat.md.
---

# Bước 6 - Kiểm tra bảo mật

## Checklist (dựa trên OWASP Top 10)
1. **Kiểm soát truy cập**: phân quyền theo vai trò, chặn truy cập trái phép (IDOR), kiểm tra quyền ở phía server.
2. **Xác thực & phiên**: băm mật khẩu (bcrypt/argon2), chính sách mật khẩu, hạn chế đăng nhập sai, token có hạn dùng, cờ cookie HttpOnly/Secure/SameSite.
3. **Injection**: SQL/NoSQL/Command injection - dùng truy vấn tham số hóa, không nối chuỗi.
4. **XSS/CSRF**: escape đầu ra, Content-Security-Policy, token CSRF.
5. **Validate đầu vào**: kiểm tra kiểu, độ dài, định dạng; giới hạn kích thước và loại file upload.
6. **Bí mật & cấu hình**: tìm API key/mật khẩu/token/tên model/URL bị hard-code trong code và lịch sử git (dùng lệnh quét ở `.agent/rules/01-code-sach.md`); `.env` nằm trong `.gitignore`; không có key bí mật ở frontend; tắt debug ở production. Nếu key từng bị commit: yêu cầu thu hồi/đổi key.
7. **Thư viện phụ thuộc**: chạy công cụ audit (pip-audit, npm audit, dotnet list package --vulnerable...), liệt kê gói có CVE.
8. **Mã hóa**: HTTPS, mã hóa dữ liệu nhạy cảm khi lưu trữ, không tự chế thuật toán.
9. **Log & lỗi**: không ghi dữ liệu nhạy cảm vào log; thông báo lỗi không lộ stack trace/chi tiết nội bộ.
10. **Dữ liệu cá nhân**: chỉ thu thập tối thiểu, có cơ chế xóa/ẩn danh.
11. **Tính năng AI/chatbot (nếu có)**: chống prompt injection, không để model truy cập dữ liệu ngoài quyền người dùng, giới hạn tần suất gọi.

## Quy trình
1. Chạy các công cụ quét có sẵn trong môi trường; nếu thiếu, đề xuất lệnh cài đặt và chờ đồng ý.
2. Rà soát thủ công các điểm vào (API, form, upload, đăng nhập).
3. Phân loại mức độ: Critical / High / Medium / Low, kèm cách khắc phục cụ thể.
4. Sửa các mục Critical/High, thêm test chống tái phát, chạy lại toàn bộ test.
5. Lưu `docs/06-bao-cao-bao-mat.md` theo mẫu `.agent/templates/bao-cao-bao-mat.md`.

## Kết thúc bước
Hỏi duyệt để sang Bước 7 - Đóng gói & bàn giao.
