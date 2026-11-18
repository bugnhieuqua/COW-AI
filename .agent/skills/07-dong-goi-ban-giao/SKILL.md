---
name: dong-goi-ban-giao
description: Dùng ở BƯỚC 7 khi dự án đã qua test, review và bảo mật. Hoàn thiện README, hướng dẫn cài đặt/triển khai, changelog, checklist phát hành và tổng kết dự án.
---

# Bước 7 - Đóng gói & bàn giao

## Quy trình
1. Kiểm tra toàn bộ docs/01 -> docs/06 đã đầy đủ và nhất quán với code.
2. Hoàn thiện `README.md`: giới thiệu, tính năng, công nghệ, yêu cầu hệ thống, cài đặt, cấu hình (.env mẫu `.env.example`), cách chạy, cách test.
3. Chuẩn bị triển khai: Dockerfile / script chạy / hướng dẫn deploy (nếu phù hợp), tách cấu hình dev - production.
4. Tạo `CHANGELOG.md` và đề xuất số phiên bản (SemVer).
5. Checklist phát hành:
   - [ ] Toàn bộ test pass
   - [ ] Không còn lỗi Blocker/Critical
   - [ ] Không có bí mật, tên model, URL hard-code trong repo; mọi cấu hình nằm trong env
   - [ ] `.env.example` đầy đủ, `.env` đã được `.gitignore`
   - [ ] Không còn code thừa/demo; tên theo chuẩn camelCase
   - [ ] README chạy được từ máy sạch
   - [ ] Có kế hoạch sao lưu / theo dõi lỗi
6. Viết `docs/07-tong-ket.md`: những gì đã làm, hạn chế còn lại, nợ kỹ thuật, hướng phát triển tiếp theo, bài học rút ra.
7. (Tùy chọn) Soạn nội dung báo cáo/thuyết trình/demo dựa trên các tài liệu đã có.

## Kết thúc
Thông báo hoàn tất và liệt kê toàn bộ sản phẩm bàn giao.
