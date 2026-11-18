---
name: review-kiem-thu
description: Dùng ở BƯỚC 5 khi mọi module đã xong. Review code (chất lượng, hiệu năng, khả năng bảo trì), kiểm thử tích hợp và nghiệm thu theo tiêu chí chấp nhận, ghi docs/05-bao-cao-review.md.
---

# Bước 5 - Review & kiểm thử

## Phần A - Code review
Rà soát toàn bộ mã nguồn theo checklist:
- [ ] Đúng kiến trúc và yêu cầu (đối chiếu docs/01 và docs/02)
- [ ] Dễ đọc: tên rõ nghĩa, hàm ngắn, không lặp code (DRY)
- [ ] Xử lý lỗi và ngoại lệ đầy đủ, không nuốt lỗi âm thầm
- [ ] Đặt tên đúng chuẩn camelCase/PascalCase/UPPER_SNAKE_CASE (xem `.agent/rules/01-code-sach.md`)
- [ ] Không có code chết, code bị comment, import/biến thừa, TODO bỏ quên, log debug thừa
- [ ] Không còn code demo/mẫu, dữ liệu mẫu, lệnh lưu trữ không dùng (localStorage...)
- [ ] Không hard-code API key, tên model, URL, port: tất cả đọc từ env qua lớp config
- [ ] `.env.example` khớp với các biến mà code đang dùng
- [ ] Hiệu năng: truy vấn N+1, vòng lặp lồng nhau, tải dữ liệu thừa
- [ ] Quản lý tài nguyên: đóng file/kết nối, tránh rò rỉ bộ nhớ
- [ ] Tài liệu: README, hướng dẫn cài đặt/chạy, docstring chính

Phân loại phát hiện: Blocker / Major / Minor / Gợi ý. Với mỗi mục ghi: file:dòng, vấn đề, cách sửa đề xuất.

## Phần B - Kiểm thử tích hợp & nghiệm thu
1. Viết/chạy integration test cho các luồng chính giữa các module.
2. Chạy toàn bộ test suite, đảm bảo không hồi quy (regression).
3. Đối chiếu từng tiêu chí chấp nhận (Given/When/Then) ở Bước 1: Đạt / Không đạt.
4. Kiểm thử thủ công các luồng chính và các tình huống lỗi thường gặp.

## Phần C - Sửa lỗi
Sửa các mục Blocker và Major; chạy lại test. Mục Minor ghi vào danh sách nợ kỹ thuật.

## Đầu ra
`docs/05-bao-cao-review.md` theo mẫu `.agent/templates/bao-cao-review.md`.

## Kết thúc bước
Hỏi duyệt để sang Bước 6 - Kiểm tra bảo mật.
