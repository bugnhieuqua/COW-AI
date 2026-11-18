---
name: BaoCao
description: Tiêu chuẩn soạn thảo báo cáo kỹ thuật, đồ án tốt nghiệp và luận văn tại Việt Nam. Tuân thủ các yếu tố pháp lý bắt buộc của Nghị định 30/2020/NĐ-CP (phông chữ, bộ mã, khổ giấy) và thực hành học thuật phổ biến tại các trường đại học kỹ thuật. Tích hợp toàn diện tiêu chuẩn UML 2.5 qua plantuml-skill (skinparam in ấn học thuật 300 DPI, chống bẫy cú pháp Mermaid Trap, mẫu 4 biểu đồ cốt lõi). Giãn dòng 1.3–1.5 lines, thụt đầu dòng 1.0–1.27 cm, tài liệu tham khảo theo thực hành học thuật chuẩn quốc tế.
---

# KỸ NĂNG: SOẠN THẢO BÁO CÁO KỸ THUẬT & LUẬN VĂN CHUẨN VIỆT NAM

## 0. NGUYÊN TẮC XÂY DỰNG QUY CHUẨN

Quy chuẩn này phân biệt rõ ba tầng căn cứ:

1. **Tầng pháp lý bắt buộc:** Nghị định 30/2020/NĐ-CP về công tác văn thư và TCVN 6909:2001 về bộ mã ký tự chữ Việt (Unicode). Các yếu tố thuộc tầng này **phải** tuân thủ tuyệt đối.
2. **Tầng thực hành học thuật:** Các thông số như giãn dòng, thụt đầu dòng, giãn đoạn, lề trang được quy định bởi **từng trường đại học kỹ thuật**, không có một chuẩn quốc gia duy nhất. Quy chuẩn này tổng hợp **khoảng giá trị phổ biến nhất** và ghi rõ dung sai.
3. **Tầng kỹ thuật chuyên ngành:** Hệ thống sơ đồ phần mềm UML 2.5 chuẩn hóa theo `/plantuml-skill`, định dạng công thức toán, bảng biểu nâng cao, khối mã nguồn và trích dẫn tài liệu tham khảo.

---

## 1. QUY CHUẨN ĐỊNH DẠNG HÌNH THỨC

### 1.1. Yếu tố pháp lý bắt buộc (Nghị định 30/2020/NĐ-CP, TCVN 6909:2001)

| Thuộc tính | Quy chuẩn bắt buộc | Căn cứ |
| :--- | :--- | :--- |
| **Phông chữ** | **Times New Roman** | Nghị định 30/2020/NĐ-CP |
| **Bộ mã ký tự** | **Unicode (TCVN 6909:2001)** | Nghị định 30; TCVN 6909:2001 |
| **Khổ giấy** | **A4 (210 × 297 mm)** | Nghị định 30/2020/NĐ-CP |
| **Căn đều** | **Justified (căn đều hai lề)** | Văn bản hành chính và báo cáo khoa học |

### 1.2. Thông số định dạng khuyến nghị cho báo cáo kỹ thuật / đồ án / luận văn

| Thuộc tính | Khoảng khuyến nghị | Ghi chú thực tiễn & Căn cứ |
| :--- | :--- | :--- |
| **Cỡ chữ nội dung** | **13–14 pt** | Nghị định 30 cho phép 13–14 pt; đa số các trường kỹ thuật quy định 13 pt |
| **Giãn dòng** | **1.3–1.5 lines** | HCMUT: 1.3 lines; CTUET: 1.2 hoặc 1.5 lines; QTU: 1.3–1.5 lines; HUST/VNU: 1.3–1.5 lines |
| **Thụt đầu dòng** | **1.0–1.27 cm** | CTUET: 1.0 cm; các trường kỹ thuật phổ biến 1.27 cm (tương đương 0.5 inch) |
| **Giãn đoạn (Before / After)** | **6 pt / 6 pt** | Chuẩn học thuật kỹ thuật; dùng Paragraph Spacing trong Word, cấm nhấn Enter liên tiếp |
| **Lề trang (Margins)** | **Trên: 2.0–2.5 cm<br>Dưới: 2.0–2.5 cm<br>Trái: 3.0–3.5 cm<br>Phải: 1.5–2.0 cm** | Nghị định 30 quy định dung sai lề. Các trường kỹ thuật thường chọn lề trái 3.5 cm và lề phải 2.0 cm để trừ hao gáy đóng quyển |

### 1.3. Phân cấp tiêu đề (Heading Hierarchy)

| Cấp | Cỡ chữ | Kiểu dáng | Quy tắc trang & Định dạng | Ví dụ |
| :--- | :---: | :--- | :--- | :--- |
| **Tiêu đề chương (Heading 1)** | 14–16 pt | In hoa, In đậm, Căn giữa | **Bắt buộc Page Break** (luôn bắt đầu trang mới), Before 12pt, After 12pt | `CHƯƠNG 2: THIẾT KẾ KIẾN TRÚC VÀ CƠ CHẾ HỆ THỐNG` |
| **Mục cấp 1 (Heading 2)** | 13–14 pt | In đậm, Căn trái | Bật `Keep with next` (chống mồ côi dòng), Before 12pt, After 6pt | `2.1. Kiến trúc tổng thể hệ thống` |
| **Mục cấp 2 (Heading 3)** | 13 pt | In đậm, Căn trái | Bật `Keep with next`, Before 6pt, After 6pt | `2.1.1. Tầng giao diện người dùng (Client Node)` |
| **Mục cấp 3 (Heading 4)** | 13 pt | In nghiêng hoặc Đậm nghiêng | Căn trái, Before 6pt, After 6pt | `2.1.1.1. Cơ chế kết nối thời gian thực qua SSE` |

> [!WARNING]
> **Quy tắc chống dòng mồ côi (Widow/Orphan & Keep with next):** Tuyệt đối không để tiêu đề mục nằm đơn độc ở dòng cuối cùng của trang. Mọi tiêu đề mục cấp 2, 3, 4 phải gắn cờ `keep_with_next = True` trong file `.docx` để tự động dời sang trang mới cùng đoạn văn tiếp theo.

---

## 2. QUY CHUẨN ĐÁNH SỐ TRANG & CẤU TRÚC PHẦN ĐẦU / THÂN BÁO CÁO

Báo cáo được chia thành 2 phân vùng ngắt trang (`Section Break - Next Page`):

1. **Phần sơ bộ (Front Matter):**
   - Bao gồm: Trang bìa chính, Bìa phụ, Lời cam đoan, Lời cảm ơn, Tóm tắt đề tài (tiếng Việt & tiếng Anh), Mục lục, Danh mục từ viết tắt, Danh mục bảng biểu, Danh mục hình ảnh.
   - **Đánh số trang:** Chữ số La Mã thường in nghiêng: `i, ii, iii, iv, v...`.
   - Trang bìa không hiển thị số trang.
2. **Phần nội dung chính (Main Body & Back Matter):**
   - Bắt đầu từ `MỞ ĐẦU` (hoặc `CHƯƠNG 1`) cho đến hết `KẾT LUẬN`, `TÀI LIỆU THAM KHẢO`, `PHỤ LỤC`.
   - **Đánh số trang:** Chữ số Ả Rập bắt đầu lại từ `1, 2, 3, 4...`.
   - **Vị trí số trang:** Căn giữa ở lề dưới trang giấy (hoặc góc trên bên phải tùy trường, mặc định giữa lề dưới theo Nghị định 30).

---

## 3. QUY CHUẨN PHÂN CẤP LIỆT KÊ (BULLET & NUMBERING)

Sử dụng hệ thống dấu phân cấp chuẩn mực văn phong kỹ thuật Việt Nam:

- **Cấp 1: Dấu gạch ngang (`-`)**
  - Thụt lề trái 1.27 cm, nhô đầu dòng -0.63 cm (hanging indent).
  - Đầu dòng viết hoa; cuối dòng kết thúc bằng dấu chấm phẩy (`;`) hoặc dấu chấm (`.`).
  - Dùng cho các luận điểm chính, nhóm chức năng, mục tiêu cấp 1.
- **Cấp 2: Dấu cộng (`+`)**
  - Thụt lề trái 2.2 cm, nhô đầu dòng -0.63 cm.
  - Triển khai cụ thể các ý con thuộc gạch đầu dòng cấp 1; kết thúc bằng dấu chấm phẩy (`;`).
- **Cấp 3: Dấu hoa thị (`*`) hoặc chữ cái (`a)`, `b)`）**
  - Thụt lề trái 3.0 cm, nhô đầu dòng -0.63 cm.
  - Dùng cho ví dụ minh họa, tham số kỹ thuật con, ngoại lệ dữ liệu.

> [!CAUTION]
> **Cấm lạm dụng:** Tuyệt đối không dùng các biểu tượng emoji, bàn tay chỉ trỏ, mũi tên trang trí, icon màu sắc trong văn bản báo cáo học thuật kỹ thuật.

---

## 4. QUY TẮC BẢNG BIỂU, CÔNG THỨC & KHỐI MÃ NGUỒN

### 4.1. Bảng biểu (Tables)
- **Vị trí tên bảng:** **PHÍA TRÊN** bảng, căn trái hoặc căn giữa, in đậm: `Bảng X.Y: Tên bảng` (X: số chương, Y: số thứ tự bảng trong chương).
- **Định dạng bảng:** 
  - Hàng tiêu đề: In đậm, nền xám nhạt (`#F1F5F9` hoặc `#E2E8F0`), căn giữa.
  - Bật thuộc tính **`tblHeader`** (Repeat header row on every page) để bảng dài tự động lặp lại tiêu đề ở trang sau.
  - Đặt thuộc tính **`cantSplit`** cho từng hàng để hàng dữ liệu không bị cắt đôi giữa 2 trang giấy.
  - Cột số liệu căn phải, cột tên/mô tả căn trái, cột STT/ký hiệu căn giữa.
- **Nguồn dữ liệu:** Nếu là dữ liệu trích dẫn, ghi chú nhỏ ngay bên dưới bảng: `Nguồn: [X] hoặc Tác giả tổng hợp (cỡ chữ 10–11 pt, in nghiêng)`.

### 4.2. Công thức toán học (Equations)
- Các công thức toán phải được soạn thảo bằng Math/Equation Editor chuẩn.
- Công thức căn giữa trang.
- Đánh số thứ tự công thức đặt trong dấu ngoặc đơn căn sát lề phải: `(X.Y)` (ví dụ: `(2.1)` là công thức 1 của Chương 2).

### 4.3. Khối mã nguồn (Code Snippets / Algorithms)
- Đặt trong khung viền mỏng 1px màu xám (`#CBD5E1`), nền xám nhạt (`#F8FAFC`).
- Phông chữ Monospace: **Consolas**, **Courier New** hoặc **JetBrains Mono**, cỡ chữ 10–11 pt (nhỏ hơn văn bản chính 2 pt), giãn dòng đơn (Single line spacing).
- Có đánh số thứ tự dòng hoặc chú thích code rõ ràng. Tuyệt đối không chụp ảnh màn hình IDE méo mó để dán vào báo cáo.

---

## 5. HỆ THỐNG BIỂU ĐỒ UML 2.5 CHUẨN HÓA THEO `/PLANTUML-SKILL`

Hệ thống biểu đồ phần mềm trong báo cáo bắt buộc phải sử dụng **PlantUML** theo tiêu chuẩn kỹ nghệ của `/plantuml-skill` để đảm bảo tính chính xác, nhất quán và thẩm mỹ in ấn cao cấp.

### 5.1. Triết lý Thiết kế UML In ấn Học thuật (Academic Print Skinparam)
Khác với chế độ Dark Mode của lập trình viên, sơ đồ nhúng vào luận văn/báo cáo in ấn phải tuân thủ nghiêm ngặt các quy tắc:
- **Nền trắng thuần túy (`#FFFFFF`)**: Không dùng nền trong suốt (`transparent`) hay nền tối để tránh lỗi hiển thị khi in hoặc chuyển đổi PDF.
- **Độ phân giải siêu nét (300 DPI)**: Đảm bảo hình in không bị vỡ hạt, răng cưa: `skinparam dpi 300`.
- **Phông chữ học thuật đồng nhất**: Sử dụng `Times New Roman` hoặc `Segoe UI` để tương thích hoàn hảo với văn bản Word: `skinparam defaultFontName "Times New Roman"`.
- **Bảng màu Academic Palette (Monochrome / Classic Blue)**:
  - Màu nhấn chính (Primary): `#1E3A8A` (Navy Blue) hoặc `#0284C7` (Sky Blue).
  - Màu nền khối: `#F8FAFC` hoặc `#F1F5F9` (bề mặt sáng, viền sắc nét `#334155`).
  - Đường nét liên kết: Nét đơn mảnh sắc nét, `skinparam shadowing false`, `skinparam roundcorner 6`.

### 5.2. Chống bẫy cú pháp PlantUML (PlantUML Syntax Guardrails)
Tuân thủ nghiêm ngặt tài liệu `docs/syntax-guide.md` của `plantuml-skill` để tránh lỗi "Mermaid Trap":
1. **Mũi tên trong Sequence Diagram:**
   - Thông điệp đồng bộ/gọi hàm: Dùng `->` (hoặc `->>`).
   - Phản hồi dữ liệu trả về: Dùng `-->>` (hoặc `-->`).
   - *Lưu ý:* Tuyệt đối không nhầm cú pháp Mermaid (`-->` là mũi tên thường trong Mermaid nhưng lại là nét đứt trong PlantUML!).
2. **Kích hoạt vòng đời (Lifeline):**
   - Luôn sử dụng cặp lệnh `activate <participant>` và `deactivate <participant>` tường minh.
3. **Mối quan hệ trong Class Diagram:**
   - Kế thừa (Generalization): `Con --|> Cha` hoặc `Con -up-|> Cha`.
   - Hiện thực hóa (Realization/Interface): `Lop ..|> GiaoDien`.
   - Hợp thành (Composition): `LopTongHop *-- LopThanhPhan`.
   - Kết tập (Aggregation): `LopTapHop o-- LopThanhVien`.
   - Phụ thuộc (Dependency): `LopA ..> LopB`.
4. **Cấu trúc C4 Model:**
   - Khi vẽ kiến trúc Container / Component, bắt buộc include thư viện chuẩn:
     ```plantuml
     !include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml
     ```

### 5.3. Tiêu chuẩn 4 Biểu đồ UML 2.5 Cốt lõi

```
[ 1. ACTIVITY DIAGRAM ] ──▶ [ 2. SEQUENCE BCE DIAGRAM ] ──▶ [ 3. CLASS DIAGRAM ] ──▶ [ 4. COMPONENT DIAGRAM ]
 (Quy trình luồng nghiệp vụ)   (Tương tác thời gian thực BCE)   (Mô hình đối tượng & Facade)    (Cấu trúc triển khai hệ thống)
```

1. **Biểu đồ hoạt động (Activity Diagram):**
   - Thể hiện trọn vẹn luồng từ điểm bắt đầu (`start`), các bước xử lý (action rounded box), rẽ nhánh điều kiện (`if ... then ... else ... endif`), cho đến điểm kết thúc (`stop`).
   - Phân làn trách nhiệm (Swimlanes) rõ ràng: `|Người dùng|`, `|Hệ thống Client|`, `|Máy chủ Backend|`, `|Dịch vụ AI Ngoài|`.
2. **Biểu đồ tuần tự BCE (Sequence Diagram - Boundary, Control, Entity):**
   - Phân vai rành mạch:
     + `actor`: Người dùng / Lập trình viên.
     + `boundary`: Giao diện (Razor View, Chat Input, Audio Player).
     + `control`: Tầng xử lý logic & điều phối (Controller, Facade Service, DRM Generator).
     + `entity`: Tầng dữ liệu & Dịch vụ ngoài (LocalStorage, MemoryStream, OpenRouter, Edge TTS).
   - Đánh số thông điệp tự động: `autonumber "<b>[00]</b>"`.
   - Bao bọc các nhánh xử lý bằng khung `alt / else` (thành công / lỗi), `opt` (tùy chọn) và `loop` (vòng lặp retry).
3. **Biểu đồ lớp (Class Diagram):**
   - Đầy đủ stereotype: `<<interface>>` `(I)`, `class` `(C)`, `enum` `(E)`.
   - Hiển thị rõ trường dữ liệu (`- private`), phương thức (`+ public`, `# protected`).
   - Thể hiện rõ các Design Pattern chủ đạo (ví dụ: Facade Pattern, Dependency Injection, Repository).
4. **Biểu đồ kiến trúc thành phần (Component / Deployment Diagram):**
   - Thể hiện các Node vật lý / môi trường thực thi: `node "Client Browser Node"`, `node "ASP.NET Core Server Node"`, `cloud "External Cloud AI Services"`.
   - Ghi rõ giao thức truyền thông trên các cổng kết nối: `HTTP/2 (Port 5223)`, `SSE (Server-Sent Events)`, `WSS (WebSocket Secure)`, `HTTPS REST API`.

### 5.4. Khung mã nguồn PlantUML chuẩn in ấn (Tham chiếu chuẩn)

#### Mẫu chuẩn in ấn học thuật (Skinparam Template):
```plantuml
@startuml
skinparam backgroundColor #FFFFFF
skinparam shadowing false
skinparam roundcorner 8
skinparam dpi 300
skinparam defaultFontName "Times New Roman"
skinparam defaultFontSize 12

!define ACADEMIC_PRIMARY #1E3A8A
!define ACADEMIC_SECONDARY #0284C7
!define ACADEMIC_BG #F8FAFC
!define ACADEMIC_BORDER #334155
!define ACADEMIC_TEXT #0F172A

skinparam sequence {
    ArrowColor ACADEMIC_PRIMARY
    LifeLineBorderColor ACADEMIC_SECONDARY
    LifeLineBackgroundColor #E0F2FE
    ParticipantBackgroundColor ACADEMIC_BG
    ParticipantBorderColor ACADEMIC_BORDER
    ParticipantFontColor ACADEMIC_TEXT
    ParticipantFontStyle bold
    ActorBackgroundColor #FEF3C7
    ActorBorderColor #D97706
    ActorFontColor ACADEMIC_TEXT
}

skinparam class {
    BackgroundColor ACADEMIC_BG
    BorderColor ACADEMIC_BORDER
    HeaderBackgroundColor #E2E8F0
    ArrowColor ACADEMIC_PRIMARY
    FontColor ACADEMIC_TEXT
}

skinparam activity {
    StartColor #16A34A
    EndColor #DC2626
    BackgroundColor ACADEMIC_BG
    BorderColor ACADEMIC_BORDER
    DiamondBackgroundColor #FEF08A
    DiamondBorderColor #CA8A04
}
@enduml
```

### 5.5. Quy trình Render Biểu đồ Tự động
1. **Phương pháp chính (Local Engine qua `plantuml-skill`):**
   ```powershell
   python .agent/skills/plantuml-skill/scripts/render.py --input docs/uml/<diagram>.puml --output docs/images/ --format png,svg
   ```
2. **Phương pháp dự phòng (Kroki API không cần Java/Graphviz cài trước):**
   - Mã hóa nội dung PlantUML qua thuật toán nén `zlib.compress` + `base64.urlsafe_b64encode`.
   - Gọi endpoint `https://kroki.io/plantuml/png/<encoded>` và lưu trực tiếp thành tệp PNG 300 DPI chất lượng cao.

### 5.6. Quy tắc Đặt tên Hình ảnh & Chú thích Sơ đồ
- **Vị trí tên hình:** Đặt ngay **PHÍA DƯỚI** hình ảnh, căn giữa, in đậm: `Hình X.Y: Tên sơ đồ/hình ảnh`.
- **Nguyên tắc cấm kỵ:**
  - **TUYỆT ĐỐI KHÔNG** chèn tên công cụ vẽ vào chú thích hình (Cấm: `- Chuẩn PlantUML`, `(Sequence Diagram - Chuẩn PlantUML)`, `(Draw.io)`).
  - **TUYỆT ĐỐI KHÔNG** gán mác năm tùy tiện vào tiêu đề hình (Cấm: `(CHUẨN QUỐC TẾ NĂM 2026)`, `(Năm 2026)`).
  - Tên hình phải phản ánh đúng ngữ nghĩa kỹ thuật, ví dụ:
    + Đúng: `Hình 2.1: Biểu đồ hoạt động tổng thể của hệ thống`
    + Đúng: `Hình 2.2: Biểu đồ tuần tự luồng tương tác thời gian thực`
    + Đúng: `Hình 2.3: Biểu đồ lớp chi tiết cấu trúc dịch vụ`
    + Đúng: `Hình 2.4: Biểu đồ kiến trúc thành phần và các luồng truyền thông`

---

## 6. QUY CHUẨN TÀI LIỆU THAM KHẢO

> [!IMPORTANT]
> **Đính chính khoa học:** Việt Nam **không có TCVN riêng** quy định định dạng trích dẫn tài liệu tham khảo cho báo cáo kỹ thuật. Tuyệt đối không viện dẫn sai "TCVN 5598" (tiêu chuẩn về cao su). Quy chuẩn dưới đây tuân thủ thực hành học thuật chuẩn quốc tế (chuẩn IEEE/Vancouver) phổ biến tại các trường kỹ thuật.

### 6.1. Quy tắc trình bày bắt buộc
1. **Đánh số trực tiếp trong ngoặc vuông:** `[1]`, `[2]`, `[3]`… **TUYỆT ĐỐI KHÔNG DÙNG** dấu gạch ngang đầu dòng như `- [1]`, `- [2]`.
2. **Thụt lề treo (Hanging Indent):**
   - `Left Indent = 1.27 cm` (0.5 inch).
   - `First Line Indent = -1.27 cm` (-0.5 inch).
3. **Thứ tự ưu tiên sắp xếp:**
   - 1. Văn bản quy phạm pháp luật Việt Nam (Nghị định, Thông tư) xếp lên đầu.
   - 2. Tiêu chuẩn quốc gia / quốc tế (TCVN, ISO, RFC, W3C).
   - 3. Giáo trình, sách chuyên khảo kỹ thuật.
   - 4. Bài báo khoa học trên các tạp chí/hội nghị chuyên ngành.
   - 5. Tài liệu kỹ thuật chính thức từ nhà sản xuất / tài liệu trực tuyến (Documentation).

### 6.2. Mẫu trích dẫn chuẩn
- **Văn bản pháp luật:**  
  `[1] Chính phủ, 2020. Nghị định số 30/2020/NĐ-CP ngày 05 tháng 3 năm 2020 về công tác văn thư. Hà Nội.`
- **Tiêu chuẩn kỹ thuật:**  
  `[2] Bộ Khoa học và Công nghệ, 2001. Tiêu chuẩn Việt Nam TCVN 6909:2001 về Công nghệ thông tin - Bộ mã ký tự tiếng Việt 16-bit. Hà Nội.`  
  `[3] Object Management Group (OMG), 2017. Unified Modeling Language (OMG UML) Specification Version 2.5.1.`
- **Giáo trình / Sách chuyên khảo:**  
  `[4] Nguyễn Văn A, 2022. Kỹ nghệ phần mềm và kiến trúc hướng dịch vụ. NXB Khoa học và Kỹ thuật, Hà Nội.`
- **Bài báo khoa học:**  
  `[5] Trần Thị B, Lê Văn C, 2023. "Ứng dụng kiến trúc bất đồng bộ thời gian thực trong tối ưu hóa giao tiếp Web". Tạp chí Tin học và Điều khiển học, 39(2), tr. 115–124.`
- **Tài liệu kỹ thuật / Trực tuyến:**  
  `[6] Microsoft Corporation, 2024. ASP.NET Core Architecture and Real-Time Communication Documentation. Truy cập từ: https://learn.microsoft.com/aspnet/core.`

---

## 7. CẤU TRÚC TOÀN DIỆN BÁO CÁO TỔNG THỂ

Một bản báo cáo kỹ thuật, đồ án tốt nghiệp chuẩn chỉnh bao gồm đầy đủ 11 phần:

```
[ DANH MỤC BAN ĐẦU ] ──▶ [ CHƯƠNG 1: TỔNG QUAN ] ──▶ [ CHƯƠNG 2: KIẾN TRÚC UML ] ──▶ [ CHƯƠNG 3: HIỆN THỰC ] ──▶ [ CHƯƠNG 4: TEST ] ──▶ [ CHƯƠNG 5 & PHỤ LỤC ]
```

1. **DANH MỤC TỪ VIẾT TẮT:** Bảng 4 cột: `STT | Từ viết tắt | Thuật ngữ tiếng Anh | Diễn giải tiếng Việt`.
2. **DANH MỤC BẢNG BIỂU:** Bảng thống kê: `Ký hiệu bảng | Tên bảng | Trang`.
3. **DANH MỤC HÌNH ẢNH VÀ SƠ ĐỒ:** Bảng thống kê: `Ký hiệu hình | Tên sơ đồ/hình | Trang` (tên thuần ngữ nghĩa, không chứa tên công cụ).
4. **LỜI MỞ ĐẦU:** Tính cấp thiết của đề tài, mục đích nghiên cứu, đối tượng & phạm vi, phương pháp nghiên cứu, ý nghĩa khoa học và thực tiễn.
5. **CHƯƠNG 1: TỔNG QUAN ĐỀ TÀI VÀ CƠ SỞ LÝ THUYẾT:**
   - Bối cảnh đề tài, khảo sát hiện trạng.
   - Bảng so sánh đối sánh các giải pháp hiện hành trên thị trường.
   - Danh mục yêu cầu chức năng (FR) và phi chức năng (NFR).
6. **CHƯƠNG 2: THIẾT KẾ KIẾN TRÚC VÀ CƠ CHẾ HỆ THỐNG:**
   - Kiến trúc tổng thể hệ thống (C4 Component / Package Diagram).
   - Biểu đồ hoạt động tổng thể (Activity Diagram).
   - Biểu đồ tuần tự luồng tương tác cốt lõi theo mô hình BCE (Sequence Diagram).
   - Biểu đồ lớp chi tiết và các Design Pattern áp dụng (Class Diagram).
   - Thiết kế luồng dữ liệu, bảo mật dữ liệu riêng tư (Privacy-by-Design), công thái học giao diện (UX).
7. **CHƯƠNG 3: HIỆN THỰC HÓA KỸ THUẬT VÀ CÔNG NGHỆ CỐT LÕI:**
   - Ngăn xếp công nghệ chi tiết (Backend, Frontend, Third-party APIs).
   - Hiện thực hóa các thuật toán xử lý dữ liệu và luồng truyền thông thời gian thực (SSE, WebSockets).
   - Kỹ thuật tối ưu hóa hiệu năng, bộ đệm (Caching) và quản lý tài nguyên.
8. **CHƯƠNG 4: KIỂM THỬ, ĐÁNH GIÁ CHẤT LƯỢNG VÀ AN TOÀN HỆ THỐNG:**
   - Khung kiểm thử tự động đa tầng (Unit Test, Integration Test, E2E Test).
   - Bảng kết quả thực nghiệm và độ đo kiểm thử.
   - Đánh giá an toàn thông tin theo chuẩn bảo mật OWASP Top 10.
9. **CHƯƠNG 5: KẾT LUẬN VÀ HƯỚNG PHÁT TRIỂN:**
   - Tổng kết các kết quả đạt được so với mục tiêu ban đầu.
   - Đánh giá hạn chế của đề tài.
   - Đề xuất hướng nghiên cứu và mở rộng trong tương lai.
10. **TÀI LIỆU THAM KHẢO:** Định dạng `[1]`, `[2]`, hanging indent 1.27 cm, sắp xếp theo thứ tự quy định.
11. **PHỤ LỤC:** Cấu trúc mã nguồn, hướng dẫn cài đặt môi trường và kịch bản chạy thử nghiệm.

---

## 8. BẢNG CHECKLIST KIỂM TRA CHẤT LƯỢNG (DEFINITION OF DONE)

Trước khi đóng gói xuất xưởng tài liệu `.docx` hoặc `.pdf`, Agent **bắt buộc** rà soát 10 tiêu chí:

- [ ] **1. Font chữ & Căn lề:** 100% Times New Roman, cỡ 13pt (hoặc 14pt), căn đều hai lề (Justified), thụt dòng 1.0–1.27 cm.
- [ ] **2. Giãn dòng & Giãn đoạn:** Giãn dòng đúng 1.3–1.5 lines, Before 6pt / After 6pt (không có dòng trống Enter liên tiếp).
- [ ] **3. Phân cấp Liệt kê:** Đúng chuẩn cấp 1 là dấu `- `, cấp 2 là dấu `+ `, cấp 3 là dấu `*`.
- [ ] **4. Tiêu đề Bảng:** Nằm **PHÍA TRÊN** bảng, định dạng `Bảng X.Y: Tên bảng`, bật `tblHeader` và `cantSplit`.
- [ ] **5. Tiêu đề Hình:** Nằm **PHÍA DƯỚI** hình, định dạng `Hình X.Y: Tên sơ đồ/hình ảnh`.
- [ ] **6. Sạch tên công cụ & Mác năm:** Không còn bất kỳ chữ `- Chuẩn PlantUML`, `(Draw.io)`, `(CHUẨN QUỐC TẾ NĂM 2026)` nào trong văn bản.
- [ ] **7. Biểu đồ UML chuẩn `/plantuml-skill`:** Đầy đủ 4 sơ đồ (Activity, Sequence BCE, Class, Component) với nền trắng, độ phân giải 300 DPI, syntax chuẩn không lỗi Mermaid Trap.
- [ ] **8. Đánh số trang 2 phân vùng:** Phần đầu đánh số La Mã (`i, ii, iii...`), phần nội dung chính đánh số Ả Rập (`1, 2, 3...`) căn giữa đáy trang.
- [ ] **9. Tài liệu tham khảo:** Đánh số `[1]`, `[2]`... không có dấu `- `, thụt lề treo 1.27 cm / -1.27 cm, văn bản pháp luật xếp đầu.
- [ ] **10. Chống dòng mồ côi:** Mọi Heading 1 ngắt trang mới (`Page Break`), mọi Heading 2, 3, 4 bật `keep_with_next`.
