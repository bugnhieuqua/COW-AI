---
name: phan-tich-yeu-cau
description: Dùng ở BƯỚC 1 khi người dùng đưa ý tưởng, đề bài hoặc yêu cầu dự án mới. Phân tích yêu cầu chức năng/phi chức năng, user story, phạm vi MVP và rủi ro, rồi lưu vào docs/01-yeu-cau.md.
---

# Bước 1 - Phân tích yêu cầu

## Khi nào dùng
Khi bắt đầu dự án/tính năng mới, hoặc khi yêu cầu còn mơ hồ.

## Quy trình
1. Đọc kỹ mô tả của người dùng và mọi tài liệu đính kèm (nếu có).
2. Liệt kê những điểm CHƯA RÕ và hỏi lại người dùng (gộp tối đa 5 câu).
3. Xác định:
   - Mục tiêu và vấn đề cần giải quyết
   - Đối tượng người dùng (persona)
   - Yêu cầu chức năng (FR-01, FR-02, ...)
   - Yêu cầu phi chức năng (hiệu năng, bảo mật, khả năng mở rộng, UX)
   - Ràng buộc (công nghệ, thời gian, ngân sách, thiết bị)
   - Phạm vi MVP và những thứ NGOÀI phạm vi
4. Viết user story dạng: "Là <vai trò>, tôi muốn <việc>, để <lợi ích>" kèm tiêu chí chấp nhận (Given/When/Then).
5. Đánh giá rủi ro và giả định.
6. Lưu kết quả vào `docs/01-yeu-cau.md` theo mẫu `.agent/templates/yeu-cau.md`.

## Đầu ra bắt buộc
- `docs/01-yeu-cau.md`
- Bảng ưu tiên (MoSCoW: Must / Should / Could / Won't)

## Kết thúc bước
Tóm tắt 5 dòng và hỏi: "Anh/chị duyệt yêu cầu để sang Bước 2 - Thiết kế kiến trúc?"
