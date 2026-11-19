Dưới đây là bản kế hoạch Markdown chi tiết để tích hợp **HyperFrames Studio** vào Dashboard COW.AI, tận dụng gói `@hyperframes/studio` để nhúng trình biên tập video trực quan vào ứng dụng ASP.NET Core của bạn.

---

```markdown
# KẾ HOẠCH TÍCH HỢP HYPERFRAMES STUDIO VÀO COW.AI DASHBOARD

## 1. Tổng quan & Mục tiêu

HyperFrames cung cấp gói `@hyperframes/studio` – một bộ React components và hooks để xây dựng trình biên tập video chạy trên trình duyệt. Gói này cho phép bạn nhúng **visual timeline, code editor, live preview** và **player** vào bất kỳ ứng dụng web nào.

**Mục tiêu:** Thay thế khu vực "Review kịch bản" tĩnh bằng một **Studio thực thụ**, nơi người dùng có thể:
- Xem trước video (preview) với timeline và player.
- Chỉnh sửa trực tiếp HTML/GSAP của từng cảnh.
- Chọn element trên preview để sửa thuộc tính (text, background, animation).
- Theo dõi tiến trình render và tải video MP4 sau khi hoàn tất.

## 2. Kiến trúc Tích hợp

```
┌─────────────────────────────────────────────────────────────┐
│                    ASP.NET Core (Host)                      │
│  ┌─────────────────────────────────────────────────────┐   │
│  │   Dashboard (Razor Pages / MVC)                     │   │
│  │   ┌─────────────────────────────────────────────┐  │   │
│  │   │   <div id="studio-root"></div>              │  │   │
│  │   │   (React app mount point)                   │  │   │
│  │   └─────────────────────────────────────────────┘  │   │
│  └─────────────────────────────────────────────────────┘   │
│                            │                               │
│               postMessage / WebSocket                      │
│                            ▼                               │
│  ┌─────────────────────────────────────────────────────┐   │
│  │   HyperFrames Studio (React App)                    │   │
│  │   - Timeline, Player, Code Editor                   │   │
│  │   - Loads composition in iframe                     │   │
│  └─────────────────────────────────────────────────────┘   │
│                            │                               │
│                     HTTP / SSE                             │
│                            ▼                               │
│  ┌─────────────────────────────────────────────────────┐   │
│  │   Backend Services                                  │   │
│  │   - Ollama (sinh kịch bản JSON)                     │   │
│  │   - VieNeu TTS (tạo audio)                          │   │
│  │   - FFmpeg / HyperFrames CLI (render)               │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

**Luồng hoạt động:**
1. Người dùng upload file → Backend xử lý → Sinh kịch bản JSON.
2. Studio được nhúng vào dashboard, tự động tải composition (từ file HTML/JSON) và hiển thị preview.
3. Người dùng tương tác với Studio (kéo thả timeline, sửa code, chọn element) → các thay đổi được lưu vào state của Studio.
4. Khi bấm "Render", frontend gửi yêu cầu đến backend → backend gọi HyperFrames CLI (hoặc producer) để render MP4 từ composition hiện tại.
5. Backend trả về URL video → Studio hiển thị video trong player.

## 3. Các bước Triển khai

### 3.1. Cài đặt Dependencies

Trong thư mục `wwwroot` (hoặc một thư mục Node.js riêng cho frontend), chạy:

```bash
npm install @hyperframes/studio react react-dom zustand
```

**Lưu ý:** `@hyperframes/studio` yêu cầu **React 18 hoặc 19**, và **Zustand 4 hoặc 5** làm peer dependencies.

Nếu bạn muốn dùng **Tailwind CSS** cho Studio (khuyến nghị), thêm preset:

```bash
npm install -D tailwindcss @hyperframes/studio/tailwind-preset
```

### 3.2. Tạo React App để mount Studio

Tạo file `wwwroot/js/studio/main.jsx` (hoặc `.tsx`):

```jsx
import React from 'react';
import { createRoot } from 'react-dom/client';
import { StudioApp } from '@hyperframes/studio';
import '@hyperframes/studio/tailwind-preset'; // nếu dùng Tailwind

// Giả sử composition đã được tải từ backend qua API
async function loadComposition(projectId) {
    const res = await fetch(`/api/projects/${projectId}/composition`);
    const { html, assets } = await res.json();
    return { html, assets };
}

const root = createRoot(document.getElementById('studio-root'));

// Lấy projectId từ URL hoặc data attribute
const projectId = document.getElementById('studio-root').dataset.projectId;

loadComposition(projectId).then((composition) => {
    root.render(
        <StudioApp
            initialComposition={composition}
            // Các props khác tùy theo API của StudioApp
            onRenderRequest={(payload) => {
                // Gửi yêu cầu render đến backend
                fetch(`/api/projects/${projectId}/render`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload),
                });
            }}
        />
    );
});
```

**Lưu ý:** `StudioApp` là component đầy đủ của Studio. Nếu bạn cần tùy biến giao diện, có thể dùng các component con như `EditorShell`, `NLEPreview`, `Timeline`, `Player`, `PropertyPanel`. Tuy nhiên, các component này yêu cầu project state và callbacks phức tạp hơn.

### 3.3. Tích hợp vào Razor Page

Trong file `Views/Studio/Index.cshtml` (hoặc tương tự):

```html
@{
    ViewData["Title"] = "COW.AI Studio";
}

<div class="studio-layout" style="height: 100vh; display: flex;">
    <!-- Sidebar (giữ nguyên từ dashboard cũ) -->
    <aside class="w-72 bg-[#161b22] border-r border-white/10 p-6">
        <!-- ... -->
    </aside>

    <!-- Main Studio Area -->
    <main class="flex-1">
        <div id="studio-root" data-project-id="@Model.ProjectId" style="height: 100%;"></div>
    </main>
</div>

@section Scripts {
    <script type="module" src="~/js/studio/main.jsx"></script>
}
```

**Lưu ý:** Vì React app cần được bundle (ví dụ bằng Vite hoặc esbuild), bạn cần cấu hình build step. Đơn giản nhất là dùng **Vite**:

```bash
npm create vite@latest studio-app -- --template react
# Copy code vào, sau đó build:
npm run build
# Output sẽ nằm trong dist/, copy vào wwwroot/js/studio/
```

### 3.4. Xử lý Render từ Backend

Backend ASP.NET Core cần một endpoint để nhận yêu cầu render từ Studio và gọi HyperFrames CLI (hoặc producer) để tạo MP4.

**Ví dụ `StudioController.cs`:**

```csharp
[ApiController]
[Route("api/projects/{projectId}/render")]
public class StudioController : ControllerBase
{
    private readonly IHyperFramesService _hyperFrames;

    public StudioController(IHyperFramesService hyperFrames)
    {
        _hyperFrames = hyperFrames;
    }

    [HttpPost]
    public async Task<IActionResult> Render(Guid projectId, [FromBody] RenderRequest request)
    {
        // Lưu composition (HTML/JSON) vào thư mục tạm
        var compositionPath = await _hyperFrames.SaveCompositionAsync(projectId, request.Html);

        // Gọi HyperFrames CLI để render (chạy bất đồng bộ qua Hangfire)
        var jobId = BackgroundJob.Enqueue(() => _hyperFrames.RenderAsync(compositionPath, projectId));

        return Ok(new { jobId });
    }
}
```

**`HyperFramesService`** có thể gọi CLI:

```bash
npx hyperframes render <composition-path> --output <output-path>
```

Hoặc dùng **producer** (HTTP server) nếu muốn kiểm soát tốt hơn.

### 3.5. Tùy chỉnh Giao diện Studio (Tailwind)

`@hyperframes/studio` xuất một Tailwind preset để đồng bộ styling. Trong file `tailwind.config.js`:

```js
const studioPreset = require('@hyperframes/studio/tailwind-preset');

module.exports = {
    presets: [studioPreset],
    content: [
        './wwwroot/js/studio/**/*.{js,jsx,ts,tsx}',
        './node_modules/@hyperframes/studio/**/*.{js,jsx,ts,tsx}',
    ],
    // ...
};
```

## 4. Các tính năng của Studio sẽ có sẵn

Khi nhúng `StudioApp`, bạn sẽ có ngay:

| Tính năng | Mô tả |
| :--- | :--- |
| **Live Preview** | Xem trước composition trong iframe, cập nhật real-time khi sửa code. |
| **Visual Timeline** | Kéo, thả, thay đổi kích thước các element trên track. |
| **Code Editor** | Chỉnh sửa HTML và GSAP scripts với CodeMirror (syntax highlighting, autocomplete). |
| **Composition Inspector** | Chọn element trên preview để xem/sửa thuộc tính. |
| **Player Controls** | Play, pause, seek, fullscreen. |
| **File Tree** | Duyệt các file trong project. |
| **Undo/Redo** | Lịch sử thay đổi. |

## 5. Lưu ý Quan trọng

- **`StudioApp` là "all-in-one"**: Nếu bạn chỉ cần nhúng toàn bộ editor, dùng `StudioApp` là đủ. Nếu muốn tùy biến sâu, hãy dùng các component con (`EditorShell`, `Timeline`, `Player`, v.v.) – nhưng sẽ phức tạp hơn nhiều.
- **Build step là bắt buộc**: React app cần được bundle (Vite, esbuild, hoặc Webpack) trước khi đưa vào `wwwroot`. Đừng cố gắng nhúng JSX trực tiếp vào Razor view.
- **Hiệu năng**: Studio chạy trong iframe với HyperFrames runtime. Việc render preview có thể ngốn CPU, đặc biệt trên máy yếu. Hãy giới hạn số Studio instance mở đồng thời.
- **Bảo mật**: Khi render, đảm bảo backend chỉ cho phép render từ composition do người dùng sở hữu (kiểm tra quyền). Không để người dùng tùy ý chạy CLI với đường dẫn tùy ý.

## 6. Lịch trình Tích hợp (3 ngày)

| Ngày | Công việc | Kết quả |
| :--- | :--- | :--- |
| **Ngày 1** | Cài đặt `@hyperframes/studio`, tạo React app cơ bản, mount `StudioApp` vào một trang test. Kiểm tra preview với một composition mẫu. | Studio hiển thị được timeline + preview. |
| **Ngày 2** | Tích hợp vào dashboard COW.AI: thay thế khu vực "Review" bằng Studio. Viết API endpoint để tải composition từ backend. | Dashboard có Studio hoạt động, dữ liệu được nạp từ DB. |
| **Ngày 3** | Nối nút "Render" từ Studio đến backend (gọi HyperFrames CLI). Hiển thị tiến trình render và video kết quả. Xử lý lỗi cơ bản. | Luồng end-to-end: upload → Studio → render → video. |

## 7. Tài nguyên Tham khảo

- **HyperFrames Studio docs**: https://hyperframes.heygen.com/packages/studio
- **Package exports & components**: https://hyperframes.app/docs/5-packages/studio
- **npm package**: `@hyperframes/studio`
- **Repo chính**: https://github.com/heygen-com/hyperframes
```

---

Bản kế hoạch này tập trung vào việc **nhúng Studio có sẵn** thay vì tự xây dựng trình biên tập từ đầu, giúp bạn tiết kiệm thời gian và có ngay một giao diện chuyên nghiệp. Nếu bạn cần điều chỉnh chi tiết nào (ví dụ: dùng component con thay vì `StudioApp`, hoặc cấu hình Vite cụ thể), hãy cho tôi biết.