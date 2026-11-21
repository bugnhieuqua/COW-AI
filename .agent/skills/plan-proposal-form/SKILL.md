---
name: plan-proposal-form
description: Bắt buộc sử dụng Form đề xuất tương tác (ask_question) để người dùng kiểm duyệt các phương án thiết kế hoặc câu hỏi mở mỗi khi đang lập kế hoạch (/plan).
user-invocable: true
---

# Plan Proposal Form Skill (`/plan` Interactive Approval Gate)

## Quy tắc cốt lõi (Mandatory Rule)
Mỗi khi người dùng kích hoạt chế độ lập kế hoạch (`/plan`) hoặc yêu cầu lên kế hoạch nâng cấp/chỉnh sửa tính năng:
1. Nếu có **bất kỳ câu hỏi làm rõ yêu cầu**, **phương án thiết kế giao diện (UI/UX)**, hoặc **lựa chọn kiến trúc/thư viện** cần đề xuất cho người dùng:
   - **BẮT BUỘC** gọi công cụ `ask_question` để mở **Form đề xuất tương tác (Multiple-Choice Proposal Form)** ngay trên giao diện chat để người dùng tích chọn và kiểm duyệt.
   - Luôn đặt phương án tối ưu nhất lên đầu kèm tiền tố `(Recommended)`.
2. Không chỉ viết câu hỏi dưới dạng văn bản thuần trong tài liệu plan mà bỏ qua Form đề xuất.
3. Sau khi người dùng chọn xong trên Form đề xuất, cập nhật ngay các quyết định đã chốt vào tài liệu Implementation Plan trước khi thi công.
