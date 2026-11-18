---
name: thiet-ke-kien-truc
description: Dùng ở BƯỚC 2 sau khi yêu cầu đã được duyệt. Thiết kế kiến trúc hệ thống, chia module, mô hình dữ liệu, API, chọn công nghệ và vẽ sơ đồ Mermaid, lưu vào docs/02-kien-truc.md.
---

# Bước 2 - Thiết kế kiến trúc

## Điều kiện đầu vào
Phải có `docs/01-yeu-cau.md` đã được người dùng duyệt. Nếu chưa có, quay lại Bước 1.

## Quy trình
1. Chọn kiểu kiến trúc phù hợp (monolith, modular monolith, client-server, microservice, hybrid...) và GIẢI THÍCH lý do, nêu phương án thay thế đã cân nhắc.
2. Chọn công nghệ (ngôn ngữ, framework, CSDL, hosting) kèm ưu/nhược điểm.
3. Chia hệ thống thành các MODULE độc lập. Với mỗi module ghi rõ:
   - Trách nhiệm, đầu vào/đầu ra
   - Phụ thuộc vào module nào
   - Thứ tự làm (module nền tảng làm trước)
4. Thiết kế dữ liệu: bảng/collection, khóa, quan hệ -> sơ đồ ER (Mermaid `erDiagram`).
5. Thiết kế API/giao diện giữa các thành phần (endpoint, request, response, mã lỗi).
6. Vẽ sơ đồ bằng Mermaid: kiến trúc tổng thể, luồng chính (sequence).
7. Cấu trúc thư mục dự án đề xuất.
8. Các quyết định kiến trúc quan trọng (ADR ngắn gọn).
9. Lưu `docs/02-kien-truc.md` theo mẫu `.agent/templates/kien-truc.md` và tạo `docs/03-ke-hoach-module.md` (danh sách module + thứ tự + tiêu chí hoàn thành) theo mẫu `.agent/templates/ke-hoach-module.md`.

## Nguyên tắc
- Đơn giản trước (KISS), tránh over-engineering cho MVP.
- Module có giao diện rõ ràng, ít phụ thuộc chéo.
- Bảo mật và khả năng test phải được tính ngay từ thiết kế.

## Kết thúc bước
Hỏi: "Anh/chị duyệt kiến trúc và kế hoạch module để sang Bước 3?"
