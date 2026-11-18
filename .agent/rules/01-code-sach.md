# Chuẩn code sạch (BẮT BUỘC cho mọi đoạn code)

## 1. Đặt tên - ưu tiên camelCase
- Biến, hàm, phương thức, tham số, thuộc tính: camelCase (`userName`, `getUserById`, `isLoggedIn`).
- Class, interface, type, enum, component: PascalCase (`UserService`, `RoomCard`).
- Hằng số cấp module và tên biến môi trường: UPPER_SNAKE_CASE (`AI_API_KEY`, `MAX_RETRY`).
- Tên file: theo quy ước đã chốt ở docs/02-kien-truc.md; mặc định camelCase (`userService.js`), file component dùng PascalCase.
- Biến boolean bắt đầu bằng `is`/`has`/`can`/`should`. Hàm bắt đầu bằng động từ (`getUser`, `validateEmail`).
- Tên đầy đủ nghĩa, tiếng Anh không dấu; cấm tên mơ hồ như `tmp`, `data1`, `x`, `usr`.
- Ngoại lệ DUY NHẤT: tên bị ràng buộc từ bên ngoài (khóa JSON của API bên thứ ba, tên cột CSDL có sẵn, quy ước bắt buộc của framework). Khi đó chuyển sang camelCase ở lớp biên (mapper/adapter), không để kiểu tên khác lan vào logic nghiệp vụ.

## 2. Xóa sạch code thừa
Phải XÓA HẲN (không comment lại, vì đã có git):
- Code bị comment, hàm/biến/import/class không dùng, file không dùng
- `console.log`, `print`, `debugger` dùng để debug
- Code demo, ví dụ mẫu, hello world, dữ liệu mẫu, tài khoản mặc định, mock data không phục vụ test
- Lệnh lưu trữ không còn dùng (vd `localStorage`, `sessionStorage`, cookie cũ)
- Code trùng lặp (gộp thành một hàm), TODO/FIXME cũ
- Package khai báo trong package.json / requirements.txt nhưng không được dùng

## 3. Không hard-code - đưa hết vào env
Các giá trị sau KHÔNG được nằm trong code:
- API key, secret, token, mật khẩu, chuỗi kết nối CSDL
- Tên model AI (vd `AI_MODEL`), URL endpoint/base URL, port, ID dịch vụ, email gửi, tên bucket
- Cờ bật/tắt tính năng, timeout/giới hạn có thể thay đổi, đường dẫn tuyệt đối

Cách làm:
- Chỉ MỘT lớp cấu hình (config) đọc biến môi trường; phần còn lại import từ đó.
- Kiểm tra lúc khởi động: thiếu biến bắt buộc thì báo lỗi nêu rõ tên biến và dừng.
- `.env` nằm trong `.gitignore`; `.env.example` liệt kê tên biến (giá trị rỗng/giả) kèm chú thích. Thêm biến mới là phải cập nhật `.env.example`.
- Frontend chỉ được đọc biến public (tiền tố `VITE_`, `NEXT_PUBLIC_`...). Tuyệt đối không đặt API key bí mật ở frontend; gọi qua backend.
- Nếu key đã lỡ bị commit: báo người dùng THU HỒI/ĐỔI key, không chỉ xóa khỏi code.

## 4. Quét kiểm tra trước khi báo hoàn thành (thay `src` bằng thư mục mã nguồn)
```
grep -rnE "localStorage|sessionStorage" src
grep -rnE "console\.log|print\(|debugger" src
grep -rniE "(api_?key|secret|token|password)[a-z_]*\s*[:=]\s*[\"'][^\"']+[\"']" src
grep -rnE "gemini-|gpt-|claude-|https?://" src
grep -rniE "TODO|FIXME|demo|sample|lorem|test123" src
```
Kết quả phải rỗng, hoặc mỗi kết quả còn lại phải có lý do chính đáng được ghi rõ trong báo cáo.
