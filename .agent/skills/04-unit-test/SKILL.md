---
name: unit-test
description: Dùng ở BƯỚC 4 ngay sau khi hoàn thành một module. Viết và chạy unit test (trường hợp bình thường, biên, lỗi), đo độ phủ, ghi kết quả vào docs/04-bao-cao-test.md.
---

# Bước 4 - Unit test

## Quy trình cho module vừa viết
1. Xác định framework test phù hợp với công nghệ dự án (pytest, unittest, Jest, JUnit, xUnit...). Dùng framework đã có sẵn nếu dự án đã chọn.
2. Lập bảng test case trước khi viết code test:
   - Happy path (đầu vào hợp lệ)
   - Edge case (rỗng, null, giá trị biên, rất lớn, ký tự đặc biệt/Unicode)
   - Error case (đầu vào sai, mất kết nối, timeout, không đủ quyền)
3. Viết test theo mẫu Arrange - Act - Assert; mỗi test chỉ kiểm tra một hành vi; tên test mô tả hành vi.
4. Dùng mock/stub cho phụ thuộc bên ngoài (DB, API, file, thời gian). Test phải chạy độc lập, lặp lại được.
5. CHẠY test thật bằng terminal, đọc kết quả. Nếu fail:
   - Xác định lỗi nằm ở code hay ở test
   - Sửa nguyên nhân gốc, không sửa test cho "xanh" một cách giả tạo
6. Đo độ phủ (coverage). Mục tiêu tối thiểu: 80% dòng cho logic nghiệp vụ.
7. Ghi kết quả (số test, pass/fail, coverage, lỗi phát hiện) vào `docs/04-bao-cao-test.md` theo mẫu `.agent/templates/bao-cao-test.md`.

## Kết thúc bước
Nếu còn module chưa làm -> quay lại Bước 3 cho module kế tiếp.
Nếu mọi module đã có test pass -> hỏi duyệt để sang Bước 5.
