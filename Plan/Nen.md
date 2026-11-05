1. Nhận xét của bạn là hoàn toàn chính xác. Mạng lưới đa giác (wireframe) hiện tại ở background trông khá rối mắt, thiếu tính "công nghệ" và phong cách hoạt hình (cartoon) của con bò. Nền màu đen tuyền (#000000) cũng thực sự gây mỏi mắt khi nhìn lâu.

Dưới đây là giải pháp **xây lại hoàn toàn background bằng Three.js** theo đúng yêu cầu của bạn:
1.  **Xóa bỏ mạng lưới rối mắt.**
2.  **Dùng logo con bò (dạng ảnh PNG trong suốt), thu nhỏ lại, làm mờ (giảm opacity) để đưa xuống làm nền.**
3.  **Thêm animation con bò đi lang thang khắp màn hình (có hiệu ứng nhấp nhô như đang bước đi).**
4.  **Giữ chuột (mousedown) để dừng con bò, nhả chuột (mouseup) để tiếp tục.**
5.  **Nền màu xanh dương nhẹ (Tech Blue) kết hợp hiệu ứng hạt sáng (particles) nhẹ nhàng để tăng chiều sâu, chống mỏi mắt.**

### Bước 1: Chuẩn bị Asset
Bạn cần một file ảnh **PNG nền trong suốt** của logo con bò (ví dụ: `logo.png`). Hãy đặt nó vào thư mục `public/` hoặc thư mục chứa ảnh của dự án (tùy framework bạn dùng như Next.js, Vite, React...).

### Bước 2: Code HTML & CSS (Lớp nền)
Đảm bảo thẻ `<canvas>` được ghim cố định dưới cùng và UI của bạn nằm trên nó.

```html
<!-- Đặt thẻ canvas này ở đầu body, ngay dưới thẻ mở body -->
<canvas id="bg-canvas"></canvas>

<!-- Toàn bộ UI hiện tại của bạn (header, input, text...) đặt trong div này -->
<div id="app-content" style="position: relative; z-index: 1;">
  <!-- UI COW.AI của bạn ở đây -->
</div>
```

```css
/* CSS cho canvas nền */
#bg-canvas {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  z-index: 0; /* Nằm dưới UI */
  pointer-events: none; /* Để chuột có thể click xuyên qua canvas xuống UI */
  background: linear-gradient(135deg, #0b132b 0%, #1c2541 100%); /* Nền xanh tech blue */
}

/* Đảm bảo body không có margin và màu nền tối */
body {
  margin: 0;
  background-color: #0b132b; 
  color: #ffffff;
  font-family: sans-serif;
  overflow-x: hidden;
}
```

### Bước 3: Code JavaScript (Three.js)

Đoạn code này sử dụng Three.js (thông qua CDN để dễ chạy thử, nếu bạn dùng npm thì dùng `import * as THREE from 'three'`).

```javascript
import * as THREE from 'https://cdn.skypack.dev/three@0.136.0';

// 1. Khởi tạo Scene, Camera, Renderer
const canvas = document.getElementById('bg-canvas');
const scene = new THREE.Scene();
// Nền màu xanh đen sâu (nếu không dùng CSS gradient thì dùng mã màu này)
scene.background = new THREE.Color(0x0b132b); 

const camera = new THREE.PerspectiveCamera(75, window.innerWidth / window.innerHeight, 0.1, 1000);
camera.position.z = 10;

const renderer = new THREE.WebGLRenderer({ canvas: canvas, alpha: true, antialias: true });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

// 2. Tạo hiệu ứng hạt sáng (Particles) cho sinh động
const particlesGeo = new THREE.BufferGeometry();
const particlesCount = 700;
const posArray = new Float32Array(particlesCount * 3);

for(let i = 0; i < particlesCount * 3; i++) {
    // Trải rộng hạt ra khắp không gian
    posArray[i] = (Math.random() - 0.5) * 30;
}
particlesGeo.setAttribute('position', new THREE.BufferAttribute(posArray, 3));
const particlesMat = new THREE.PointsMaterial({
    size: 0.05,
    color: 0x4facfe, // Màu xanh công nghệ
    transparent: true,
    opacity: 0.6,
    blending: THREE.AdditiveBlending
});
const particlesMesh = new THREE.Points(particlesGeo, particlesMat);
scene.add(particlesMesh);

// 3. Tải Logo Con Bò
const textureLoader = new THREE.TextureLoader();
// THAY ĐỔI ĐƯỜNG DẪN NÀY THÀNH ĐƯỜNG DẪN ẢNH CON BÒ CỦA BẠN
const cowTexture = textureLoader.load('/cow-logo.png'); 

// Tạo vật liệu cho con bò (làm mờ để không lấn át text)
const cowMaterial = new THREE.SpriteMaterial({ 
    map: cowTexture, 
    transparent: true, 
    opacity: 0.15, // Độ mờ (0.1 - 0.2 là đẹp)
    color: 0xffffff 
});

const cowSprite = new THREE.Sprite(cowMaterial);
// Kích thước con bò (thu nhỏ lại)
cowSprite.scale.set(3, 3, 1); 
scene.add(cowSprite);

// 4. Logic di chuyển (Wandering)
let targetPosition = new THREE.Vector3(0, 0, 0);
let isPaused = false;
const speed = 0.03; // Tốc độ đi của bò
let time = 0; // Biến thời gian cho hiệu ứng nhấp nhô

function getRandomPosition() {
    // Giới hạn vùng di chuyển dựa trên kích thước màn hình
    const aspect = window.innerWidth / window.innerHeight;
    const width = 15 * aspect; 
    const height = 15;
    
    return new THREE.Vector3(
        (Math.random() - 0.5) * width,
        (Math.random() - 0.5) * height,
        0
    );
}

targetPosition.copy(getRandomPosition());

// 5. Xử lý sự kiện chuột (Giữ để dừng)
window.addEventListener('mousedown', () => { isPaused = true; });
window.addEventListener('mouseup', () => { isPaused = false; });
// Hỗ trợ thiết bị cảm ứng
window.addEventListener('touchstart', () => { isPaused = true; });
window.addEventListener('touchend', () => { isPaused = false; });

// 6. Vòng lặp Animation
const clock = new THREE.Clock();

function animate() {
    requestAnimationFrame(animate);
    const delta = clock.getDelta();

    if (!isPaused) {
        time += delta * 5; // Tốc độ nhấp nhô

        // Di chuyển mượt mà về phía target
        cowSprite.position.lerp(targetPosition, speed);

        // Hiệu ứng nhấp nhô (bobbing) giả lập bước đi
        // Chỉ nhấp nhô trục Y một chút so với vị trí hiện tại
        cowSprite.position.y += Math.sin(time) * 0.01;

        // Lật hình con bò dựa vào hướng đi (trái/phải)
        if (targetPosition.x > cowSprite.position.x) {
            cowSprite.scale.x = -Math.abs(cowSprite.scale.x); // Hướng sang phải
        } else {
            cowSprite.scale.x = Math.abs(cowSprite.scale.x);  // Hướng sang trái
        }

        // Nếu đã đến gần đích, chọn đích mới
        if (cowSprite.position.distanceTo(targetPosition) < 0.5) {
            targetPosition.copy(getRandomPosition());
        }
    }

    // Xoay nhẹ các hạt particles để tạo cảm giác không gian
    particlesMesh.rotation.y += 0.0005;
    particlesMesh.rotation.x += 0.0002;

    renderer.render(scene, camera);
}

animate();

// 7. Xử lý khi resize cửa sổ
window.addEventListener('resize', () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
});
```

### 💡 Điểm nổi bật của đoạn code này:
*   **Màu nền (Tech Blue):** Đã thay bằng dải màu Gradient từ `#0b132b` sang `#1c2541`. Màu này rất hợp với các trang web công nghệ/AI, tạo cảm giác chuyên nghiệp, dịu mắt.
*   **Logo mờ (Opacity 0.15):** Con bò sẽ chìm vào nền, không gây cảm giác "đè" lên các chữ text phía trước.
*   **Hiệu ứng nhấp nhô (`Math.sin(time)`):** Giúp con bò di chuyển trông tự nhiên (như đang nhún nhảy/đi bộ) chứ không bị trượt cứng ngắc trên màn hình.
*   **Tự động lật hình:** Dựa vào tọa độ X, con bò sẽ tự lật đầu sang trái/phải tùy hướng đi.
*   **Tương tác:** Giữ chuột (hoặc chạm tay) ở bất kỳ đâu trên màn hình, con bò sẽ đứng yên. Nhả ra, nó tiếp tục đi. (Lưu ý: `pointer-events: none` trên thẻ canvas giúp bạn vẫn click được vào các nút bấm/ô input phía trên bình thường).

------------------
2.
Nhìn vào bức ảnh mới, tôi thấy rõ 3 vấn đề lớn khiến giao diện trông thiếu chuyên nghiệp (UX/UI chưa chuẩn):
1.  **Mạng lưới đa giác đen (Wireframe)** vẫn đang đè lên nền, tạo cảm giác cực kỳ lộn xộn và "rác" (cluttered).
2.  **Hộp Code (Code Block)** màu đen tuyền (#1e1e1e) đặt trên nền trắng tạo ra độ tương phản quá gắt, gây chói mắt và phá vỡ tổng thể thiết kế (như bạn nói: "nền trắng thì tránh dùng đen").
3.  **Thiếu cấu trúc (Hierarchy) và khoảng thở (Whitespace)**: Các khối chữ và form nhập liệu dính sát vào nhau, thiếu bóng đổ (shadow) để tách biệt các layer.

Dưới đây là bản code **xây lại toàn bộ giao diện trang CodeGen (Light Mode)**. Tôi đã:
- Xóa bỏ mạng lưới đen.
- Áp dụng phong cách thiết kế **"Clean Tech"** (hiện đại, phẳng, viền mảnh, bóng đổ mềm).
- Đổi hộp Code sang giao diện Sáng (Light Theme cho code) hoặc Xám dịu (Slate) để dịu mắt.
- Tích hợp lại Three.js với **nền sáng (Light theme)**, con bò mờ lang thang phía sau và các hạt sáng xanh nhẹ nhàng.

---

### 1. Cấu trúc HTML (Component CodeGen)

```html
<!-- Lớp nền Three.js -->
<canvas id="bg-canvas"></canvas>

<div class="page-container">
  <!-- Header (Giữ nguyên của bạn) -->
  <header class="main-header">
    <div class="logo-area">
      <img src="/cow-logo.png" alt="COW.AI" class="logo-small" />
      <span class="logo-text">COW.AI</span>
    </div>
    <nav class="nav-links">
      <a href="#">Trang chủ</a>
      <a href="#">Tạo ảnh</a>
      <a href="#">Tóm tắt</a>
      <a href="#" class="active">Viết code</a>
    </nav>
  </header>

  <!-- Nội dung chính -->
  <main class="codegen-layout">
    
    <!-- Cột Trái: Input -->
    <section class="card input-section">
      <div class="card-header">
        <div class="title-group">
          <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect><line x1="8" y1="21" x2="16" y2="21"></line><line x1="12" y1="17" x2="12" y2="21"></line></svg>
          <h2>Trợ lý viết code</h2>
        </div>
        <span class="badge blue-badge">Multi-Language</span>
      </div>
      <p class="subtitle">Sinh mã nguồn tối ưu, chuẩn cú pháp và có chú thích chi tiết</p>

      <div class="form-group">
        <label>Ngôn ngữ mục tiêu</label>
        <select class="form-control">
          <option>Python</option>
          <option>JavaScript / TypeScript</option>
          <option>Java</option>
          <option>C++</option>
        </select>
      </div>

      <div class="form-group">
        <label>Mô tả thuật toán hoặc chức năng cần viết</label>
        <textarea class="form-control" rows="5" placeholder="Ví dụ: Viết một class Python để kết nối với OpenRouter API, hỗ trợ streaming response và xử lý lỗi timeout tự động..."></textarea>
      </div>

      <div class="form-group">
        <label>Tiêu chuẩn sinh code</label>
        <div class="tags-container">
          <span class="tag active">Chú thích tiếng Việt</span>
          <span class="tag active">Kèm Type Hints / Static Typing</span>
          <span class="tag">Tối ưu hiệu năng</span>
        </div>
      </div>

      <button class="btn-primary">
        Sinh mã nguồn ngay
        <svg class="icon-right" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>
      </button>
    </section>

    <!-- Cột Phải: Output -->
    <section class="card output-section">
      <div class="card-header">
        <div class="title-group">
          <h2>Mã nguồn kết xuất</h2>
        </div>
        <span class="badge">Python</span>
      </div>
      <p class="subtitle">Tô màu cú pháp chuyên nghiệp & kiểm thử tĩnh</p>

      <!-- Code Block: Đã chuyển sang Light Theme (Không dùng nền đen) -->
      <div class="code-editor">
        <div class="editor-header">
          <div class="mac-dots">
            <span class="dot red"></span>
            <span class="dot yellow"></span>
            <span class="dot green"></span>
          </div>
          <span class="filename">solution.py</span>
          <button class="copy-btn">Copy</button>
        </div>
        <div class="editor-body">
          <!-- Nội dung code placeholder -->
          <pre><code><span class="comment"># Chưa có mã nguồn</span>
<span class="keyword">Hãy nhập</span> mô tả chức năng và bấm <span class="string">"Sinh mã nguồn ngay"</span>
          </code></pre>
        </div>
      </div>

      <!-- Giải thích thuật toán -->
      <div class="explanation-box">
        <div class="exp-header">
          <svg class="icon-sm" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 18h6"></path><path d="M10 22h4"></path><path d="M12 2a7 7 0 0 0-7 7c0 2.38 1.19 4.47 3 5.74V17a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1v-2.26c1.81-1.27 3-3.36 3-5.74a7 7 0 0 0-7-7z"></path></svg>
          <strong>Giải thích thuật toán:</strong>
        </div>
        <p>Hệ thống sẽ phân tích yêu cầu của bạn và tự động tạo ra phần giải thích chi tiết tại đây.</p>
      </div>
    </section>

  </main>
</div>
```

### 2. CSS (Giao diện chuẩn UX/UI)

```css
:root {
  --bg-color: #f4f7f9; /* Xám xanh rất nhạt, dịu mắt */
  --card-bg: #ffffff;
  --text-main: #1e293b; /* Xanh đen đậm */
  --text-muted: #64748b;
  --border-color: #e2e8f0;
  --primary-color: #3b82f6; /* Xanh dương tech */
  --primary-hover: #2563eb;
  --primary-light: #eff6ff;
  
  /* Màu cho Code Editor (Light theme) */
  --code-bg: #f8fafc;
  --code-text: #334155;
  --code-comment: #94a3b8;
  --code-keyword: #8b5cf6;
  --code-string: #059669;
}

body {
  margin: 0;
  font-family: 'Inter', -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  background-color: var(--bg-color);
  color: var(--text-main);
}

/* --- LỚP NỀN --- */
#bg-canvas {
  position: fixed;
  top: 0; left: 0; width: 100vw; height: 100vh;
  z-index: 0;
  pointer-events: none; /* Cho phép click xuyên qua */
  background: radial-gradient(circle at 50% 0%, #e0e7ff 0%, #f4f7f9 70%); /* Hiệu ứng ánh sáng nhẹ */
}

.page-container {
  position: relative;
  z-index: 1; /* Nằm trên canvas */
  max-width: 1400px;
  margin: 0 auto;
  padding: 0 24px;
}

/* --- HEADER --- */
.main-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 20px 0;
  margin-bottom: 20px;
  border-bottom: 1px solid var(--border-color);
}
.logo-area { display: flex; align-items: center; gap: 10px; }
.logo-small { width: 32px; height: 32px; border-radius: 8px;}
.logo-text { font-size: 20px; font-weight: 700; color: var(--primary-color); }
.nav-links a { text-decoration: none; color: var(--text-muted); margin-left: 24px; font-weight: 500; transition: color 0.2s;}
.nav-links a:hover, .nav-links a.active { color: var(--primary-color); }

/* --- LAYOUT --- */
.codegen-layout {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 24px;
  margin-bottom: 40px;
}

/* --- CARD CHUNG --- */
.card {
  background: var(--card-bg);
  border-radius: 16px;
  padding: 24px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.03), 0 1px 3px rgba(0,0,0,0.02);
  border: 1px solid var(--border-color);
  display: flex;
  flex-direction: column;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}
.title-group { display: flex; align-items: center; gap: 10px; }
.title-group h2 { font-size: 18px; margin: 0; font-weight: 600; color: var(--text-main); }
.icon { width: 20px; height: 20px; color: var(--text-muted); }
.subtitle { color: var(--text-muted); font-size: 14px; margin-top: 0; margin-bottom: 24px; }

.badge {
  padding: 4px 12px;
  border-radius: 20px;
  font-size: 12px;
  font-weight: 600;
  background: var(--primary-light);
  color: var(--primary-color);
}

/* --- FORM --- */
.form-group { margin-bottom: 20px; }
.form-group label { display: block; font-size: 14px; font-weight: 500; margin-bottom: 8px; color: var(--text-main);}
.form-control {
  width: 100%;
  padding: 12px 16px;
  border-radius: 8px;
  border: 1px solid var(--border-color);
  font-family: inherit;
  font-size: 14px;
  background: #f8fafc;
  transition: all 0.2s;
  box-sizing: border-box;
}
.form-control:focus {
  outline: none;
  border-color: var(--primary-color);
  background: #ffffff;
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}

.tags-container { display: flex; flex-wrap: wrap; gap: 8px; }
.tag {
  padding: 6px 12px;
  border-radius: 6px;
  font-size: 13px;
  border: 1px solid var(--border-color);
  cursor: pointer;
  background: #fff;
  color: var(--text-muted);
}
.tag.active {
  background: var(--primary-light);
  border-color: var(--primary-color);
  color: var(--primary-color);
  font-weight: 500;
}

.btn-primary {
  background: var(--primary-color);
  color: white;
  border: none;
  padding: 14px 24px;
  border-radius: 8px;
  font-size: 15px;
  font-weight: 600;
  cursor: pointer;
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 8px;
  transition: background 0.2s;
  margin-top: auto;
}
.btn-primary:hover { background: var(--primary-hover); }
.icon-right { width: 18px; height: 18px; }

/* --- CODE EDITOR (Sáng sủa, ko dùng nền đen) --- */
.code-editor {
  border-radius: 12px;
  border: 1px solid var(--border-color);
  overflow: hidden;
  background: var(--code-bg);
  flex-grow: 1;
  margin-bottom: 20px;
}
.editor-header {
  display: flex;
  align-items: center;
  padding: 12px 16px;
  background: #f1f5f9;
  border-bottom: 1px solid var(--border-color);
}
.mac-dots { display: flex; gap: 6px; margin-right: 16px; }
.dot { width: 12px; height: 12px; border-radius: 50%; }
.dot.red { background: #ef4444; }
.dot.yellow { background: #f59e0b; }
.dot.green { background: #10b981; }
.filename { font-size: 13px; color: var(--text-muted); font-family: monospace; flex-grow: 1; text-align: center;}
.copy-btn { background: none; border: none; color: var(--text-muted); font-size: 13px; cursor: pointer; }

.editor-body { padding: 20px; font-family: 'Fira Code', monospace; font-size: 14px; line-height: 1.6; color: var(--code-text);}
.comment { color: var(--code-comment); }
.keyword { color: var(--code-keyword); font-weight: 500; }
.string { color: var(--code-string); }

/* --- GIẢI THÍCH --- */
.explanation-box {
  background: var(--primary-light);
  border-radius: 12px;
  padding: 16px;
  border: 1px solid #dbeafe;
}
.exp-header { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; color: var(--primary-color); }
.icon-sm { width: 18px; height: 18px; }
.explanation-box p { margin: 0; font-size: 14px; color: #1e3a8a; line-height: 1.5; }

/* Responsive */
@media (max-width: 992px) {
  .codegen-layout { grid-template-columns: 1fr; }
}
```

### 3. JavaScript (Three.js Nền sáng - Con bò lang thang)

Bạn thay thế đoạn script Three.js cũ bằng đoạn này. Nó đã được tinh chỉnh để hợp với **Light Mode** (màu hạt sáng trong suốt, con bò mờ đi để không làm lu mờ text).

```javascript
import * as THREE from 'https://cdn.skypack.dev/three@0.136.0';

const canvas = document.getElementById('bg-canvas');
const scene = new THREE.Scene();
// Không set scene.background để nó trong suốt, lấy màu từ CSS
const camera = new THREE.PerspectiveCamera(75, window.innerWidth / window.innerHeight, 0.1, 1000);
camera.position.z = 10;

const renderer = new THREE.WebGLRenderer({ canvas: canvas, alpha: true, antialias: true });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

// 1. Hạt sáng (Particles) - Màu xanh nhạt hợp nền sáng
const particlesGeo = new THREE.BufferGeometry();
const particlesCount = 400; // Giảm số lượng cho nhẹ
const posArray = new Float32Array(particlesCount * 3);
for(let i = 0; i < particlesCount * 3; i++) {
    posArray[i] = (Math.random() - 0.5) * 30;
}
particlesGeo.setAttribute('position', new THREE.BufferAttribute(posArray, 3));
const particlesMat = new THREE.PointsMaterial({
    size: 0.05,
    color: 0x60a5fa, // Xanh dương nhạt
    transparent: true,
    opacity: 0.4,
    blending: THREE.NormalBlending // Đổi sang Normal để hợp nền sáng
});
const particlesMesh = new THREE.Points(particlesGeo, particlesMat);
scene.add(particlesMesh);

// 2. Con bò (Logo)
const textureLoader = new THREE.TextureLoader();
const cowTexture = textureLoader.load('/cow-logo.png'); // Đổi lại đường dẫn ảnh của bạn

const cowMaterial = new THREE.SpriteMaterial({ 
    map: cowTexture, 
    transparent: true, 
    opacity: 0.08, // Rất mờ, làm background pattern nhẹ nhàng
    color: 0x1e293b // Ám màu xanh đen để hợp nền sáng
});

const cowSprite = new THREE.Sprite(cowMaterial);
cowSprite.scale.set(2, 2, 1); // Thu nhỏ
scene.add(cowSprite);

// 3. Logic di chuyển
let targetPosition = new THREE.Vector3(0, 0, 0);
let isPaused = false;
const speed = 0.02; 
let time = 0;

function getRandomPosition() {
    const aspect = window.innerWidth / window.innerHeight;
    const width = 20 * aspect; 
    const height = 20;
    return new THREE.Vector3(
        (Math.random() - 0.5) * width,
        (Math.random() - 0.5) * height,
        0
    );
}
targetPosition.copy(getRandomPosition());

window.addEventListener('mousedown', () => { isPaused = true; });
window.addEventListener('mouseup', () => { isPaused = false; });
window.addEventListener('touchstart', () => { isPaused = true; });
window.addEventListener('touchend', () => { isPaused = false; });

// 4. Animation Loop
const clock = new THREE.Clock();

function animate() {
    requestAnimationFrame(animate);
    const delta = clock.getDelta();

    if (!isPaused) {
        time += delta * 3;
        // Di chuyển mượt
        cowSprite.position.lerp(targetPosition, speed);
        // Nhấp nhô
        cowSprite.position.y += Math.sin(time) * 0.005;

        // Hướng mặt
        if (targetPosition.x > cowSprite.position.x) {
            cowSprite.scale.x = -Math.abs(cowSprite.scale.x);
        } else {
            cowSprite.scale.x = Math.abs(cowSprite.scale.x);
        }

        if (cowSprite.position.distanceTo(targetPosition) < 0.5) {
            targetPosition.copy(getRandomPosition());
        }
    }

    particlesMesh.rotation.y += 0.0002;
    renderer.render(scene, camera);
}

animate();

window.addEventListener('resize', () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
});
```

### 🎯 Tại sao bản này lại "chuẩn UX/UI" hơn?
1.  **Loại bỏ mạng lưới (Wireframe):** Thay vào đó là nền chuyển màu (Gradient) cực nhẹ kết hợp hạt sáng (Particles), tạo chiều sâu mà không gây rối mắt.
2.  **Phân cấp thị giác (Visual Hierarchy):** Các khối được bao bọc bởi `border-radius: 16px` và đổ bóng mềm (`box-shadow`). Trông như những tấm kính nổi lên trên bề mặt, rất hiện đại (phong cách các trang như Vercel, Linear, Stripe).
3.  **Sửa lỗi Code Block:** Thay vì một cục đen sì đặt giữa nền trắng, Code Block giờ đây dùng nền xám cực nhạt (`#f8fafc`), viền nhạt, các "nút giao thông" (mac dots) và text được tô màu nhẹ nhàng. Điều này giúp mắt người dùng không bị "giật" khi nhìn từ vùng nhập liệu sang vùng kết quả.
4.  **Tương tác rõ ràng:** Các thẻ tag (tags-container) có trạng thái `.active` rõ ràng. Nút bấm (Button) có màu chủ đạo của thương hiệu (Xanh dương) nổi bật. Các input khi click vào sẽ có viền sáng xanh (focus ring) chuẩn xác.