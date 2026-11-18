# KẾ HOẠCH TRIỂN KHAI AI GATEWAY – MÁI NHÀ XANH

**Phiên bản 2.0 | Tháng 10/2026**

---

## 1. TỔNG QUAN KIẾN TRÚC

```
┌─────────────────────────────────────────────────────────────────────┐
│                      ỨNG DỤNG PHP (Mái Nhà Xanh)                   │
│                                                                     │
│   $ai->chat("Tìm phòng gần trường...")      →  Nhiệm vụ: Chat AI   │
│   $ai->vision("Phân tích ảnh này...", $img) →  Nhiệm vụ: Vision   │
│                                                                     │
│   Gọi duy nhất: http://localhost:20128/v1/chat/completions         │
│   Dùng duy nhất: OMNIROUTE_API_KEY (key của OmniRoute)             │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      OMNIROUTE GATEWAY                              │
│                   http://localhost:20128                            │
│                                                                     │
│  KHÔNG CÓ MODEL. LÀ BƯU ĐIỆN ĐỊNH TUYẾN.                          │
│  Nhận request → Đọc tên model → Chuyển đến provider → Trả kết quả  │
│                                                                     │
│  ┌───────────────────────────────────────────────────────────────┐ │
│  │  Free Tier Aggregation: ~1.62 tỷ token miễn phí/tháng        │ │
│  │  (489 free-tier entries, 357 providers)                      │ │
│  └───────────────────────────────────────────────────────────────┘ │
└──────┬──────────────────┬──────────────────┬────────────────────────┘
       │                  │                  │
       ▼                  ▼                  ▼
┌──────────────┐  ┌──────────────┐  ┌──────────────────┐
│    GROQ      │  │ OLLAMA CLOUD │  │   OPENROUTER     │
│  Chat AI     │  │   Vision     │  │   Fallback       │
│  (Free)      │  │   (Free)     │  │   (Free)         │
└──────────────┘  └──────────────┘  └──────────────────┘
```

**Nguyên tắc cốt lõi:**

- Ứng dụng PHP **không bao giờ** chạm trực tiếp vào key của Groq, Ollama hay OpenRouter.
- Tất cả API key của provider được nạp **một lần duy nhất** vào OmniRoute Dashboard.
- Ứng dụng PHP chỉ cần **một API key duy nhất** do OmniRoute sinh ra.
- OmniRoute tự động xử lý fallback, xoay vòng provider, và tối ưu chi phí.

---

## 2. BIẾN MÔI TRƯỜNG (.env)

### 2.1. File `.env` cho ứng dụng PHP

```env
# ============================================
# OMNIROUTE GATEWAY
# ============================================
OMNIROUTE_BASE_URL=http://localhost:20128/v1
OMNIROUTE_API_KEY=sk-your-omniroute-generated-key

# ============================================
# AI TASK ASSIGNMENTS (ĐÃ CẬP NHẬT GEMINI 3.8)
# ============================================
AI_CHAT_MODEL=auto/fast
AI_CHAT_FALLBACK=openrouter/free
AI_VISION_MODEL=google/gemini-3.8-flash
AI_VISION_FALLBACK=openrouter/google/gemini-3.8-flash

# ============================================
# TIMEOUTS & LIMITS
# ============================================
AI_CHAT_TIMEOUT=15
AI_VISION_TIMEOUT=30
AI_MAX_TOKENS=1024
AI_TEMPERATURE=0.7
```

**Giải thích Model ID:**

| Model ID                    | Ý nghĩa                                                                             |
| --------------------------- | ------------------------------------------------------------------------------------- |
| `auto/fast`               | OmniRoute tự chọn model miễn phí nhanh nhất từ tất cả provider đã kết nối |
| `openrouter/free`         | OpenRouter tự chọn model free khả dụng                                            |
| `google/gemini-3.8-flash` | Model Gemini 3.8 Flash mới nhất (phát hành 02/09/2026)                            |

### 2.2. File `.env` cho OmniRoute

```env
# ============================================
# OMNIROUTE CORE
# ============================================
PORT=20128
DATA_DIR=%APPDATA%\omniroute
INITIAL_PASSWORD=your-dashboard-password

# ============================================
# GROQ (Chat AI - Tốc độ cao, Miễn phí)
# ============================================
GROQ_API_KEY=gsk_your-groq-key-here

# ============================================
# OLLAMA CLOUD (Vision - Phân tích ảnh)
# ============================================
OLLAMA_CLOUD_API_KEY=your-ollama-cloud-key

# ============================================
# OPENROUTER (Fallback - Đa model)
# ============================================
OPENROUTER_API_KEY=sk-or-v1-your-openrouter-key

# ============================================
# GOOGLE AI (Gemini 3.8 Flash)
# ============================================
GOOGLE_AI_API_KEY=your-google-ai-key
```

### 2.3. Bảng tổng hợp biến môi trường

| Biến                    | Nơi lưu         | Lấy từ đâu                           | Bắt buộc |
| ------------------------ | ----------------- | ---------------------------------------- | ---------- |
| `OMNIROUTE_BASE_URL`   | App`.env`       | Cố định:`http://localhost:20128/v1` | ✅         |
| `OMNIROUTE_API_KEY`    | App`.env`       | Dashboard → Endpoints                   | ✅         |
| `GROQ_API_KEY`         | OmniRoute`.env` | `console.groq.com`                     | ✅         |
| `OLLAMA_CLOUD_API_KEY` | OmniRoute`.env` | `ollama.com/settings/keys`             | ✅         |
| `OPENROUTER_API_KEY`   | OmniRoute`.env` | `openrouter.ai/keys`                   | ✅         |
| `GOOGLE_AI_API_KEY`    | OmniRoute`.env` | `aistudio.google.com`                  | ✅         |

---

## 3. CÀI ĐẶT OMNIROUTE

### 3.1. Yêu cầu hệ thống

| Thành phần | Yêu cầu tối thiểu                   |
| ------------ | --------------------------------------- |
| Node.js      | >= 22.0.0                               |
| RAM          | 512MB (khuyến nghị 1GB)               |
| Disk         | 500MB                                   |
| OS           | Windows 10/11, Ubuntu 22.04+, macOS 13+ |

### 3.2. Cài đặt bằng npm (PowerShell)

```powershell
# 1. Cài đặt OmniRoute global
npm install -g omniroute

# 2. Khởi động server
omniroute

# Dashboard sẽ tự động mở tại http://localhost:20128
```

**Cách hoạt động:** OmniRoute khởi động ngay lập tức, **không cần API key nào cả**. Model `auto` đã hoạt động ngay sau khi cài đặt.

### 3.3. Cài đặt OmniRoute như Windows Service (khuyến nghị)

```powershell
# Chạy nền, tự động khởi động cùng Windows
omniroute install-service

# Kiểm tra trạng thái
omniroute status

# Khởi động service
omniroute start
```

Sau khi cài service, OmniRoute chạy nền 24/7. Không cần mở PowerShell mỗi lần.

### 3.4. Truy cập Dashboard

```
http://localhost:20128
```

Đăng nhập bằng `INITIAL_PASSWORD` đã set trong `.env`.

---

## 4. KẾT NỐI CÁC PROVIDER MIỄN PHÍ

### 4.1. Nguyên tắc: Key của Provider → OmniRoute, Key của OmniRoute → PHP

| Bước | Hành động                                                 | Key nào?               |
| ------ | ------------------------------------------------------------ | ----------------------- |
| 1      | Lấy API key từ Groq, Ollama Cloud, OpenRouter              | Key của từng provider |
| 2      | Nạp key vào Dashboard OmniRoute (mục Providers)           | Key của provider       |
| 3      | Tạo API key mới trong Dashboard OmniRoute (mục Endpoints) | Key của OmniRoute      |
| 4      | Dán key OmniRoute vào file`.env` của PHP                | Key của OmniRoute      |

**Ứng dụng PHP không bao giờ chạm trực tiếp vào key của Groq, Ollama hay OpenRouter.** Nó chỉ biết key của OmniRoute.

### 4.2. Thêm Provider vào OmniRoute

Trong Dashboard OmniRoute, vào **Providers** → **Add Provider**:

**Groq (Chat AI):**

- Chọn provider: **Groq**
- API Key: `gsk_...` (lấy từ `console.groq.com`)
- Base URL: `https://api.groq.com/openai/v1`
- Free tier: 30 req/phút, 1.000 req/ngày

**Ollama Cloud (Vision):**

- Chọn provider: **Ollama Cloud**
- API Key: lấy từ `ollama.com/settings/keys`
- Base URL: `https://ollama.com/v1`
- Free tier: session/weekly limits (không công bố)

**OpenRouter (Fallback):**

- Chọn provider: **OpenRouter**
- API Key: `sk-or-v1-...` (lấy từ `openrouter.ai/keys`)
- Base URL: `https://openrouter.ai/api/v1`
- Free tier: 20 req/phút, 50 req/ngày

**Google AI (Gemini 3.8 Flash):**

- Chọn provider: **Google AI**
- API Key: lấy từ `aistudio.google.com`
- Model: `gemini-3.8-flash`

### 4.3. Thêm Provider miễn phí không cần Key

OmniRoute hỗ trợ các provider **miễn phí hoàn toàn, không cần API key**:

| Provider                | Model miễn phí      | Cách thêm                           |
| ----------------------- | --------------------- | ------------------------------------- |
| **Kiro AI**       | Claude models         | Providers → Kiro AI → Connect       |
| **OpenCode Free** | GPT models            | Providers → OpenCode Free → Connect |
| **Pollinations**  | GPT-5, Claude, Gemini | Providers → Pollinations → Connect  |

Chỉ cần nhấn **Connect**, không cần key.

### 4.4. Tạo API Key cho ứng dụng PHP

1. Vào **Endpoints** → **Create Endpoint**
2. Đặt tên: `mai-nha-xanh-app`
3. Chọn providers: Groq, Ollama Cloud, OpenRouter, Google AI
4. Generate API Key → Copy key này vào `OMNIROUTE_API_KEY` trong app `.env`

---

## 5. PHÂN CÔNG NHIỆM VỤ

### 5.1. Bảng phân công chi tiết

| Nhiệm vụ                              | Model ID trong OmniRoute    | Provider chính      | Fallback 1        | Fallback 2   |
| --------------------------------------- | --------------------------- | -------------------- | ----------------- | ------------ |
| **Chat AI** (tư vấn, tìm trọ) | `auto/fast`               | Groq (Llama 3.1 8B)  | OpenRouter Free   | Ollama Cloud |
| **Chấm bài đăng** (text)      | `auto/cheap`              | Groq (Llama 3.3 70B) | OpenRouter Free   | Ollama Cloud |
| **Phân tích ảnh** (vision)     | `google/gemini-3.8-flash` | Google AI            | OpenRouter Gemini | Ollama Cloud |

### 5.2. Lý do chọn model

**Groq cho Chat AI:**

- Tốc độ cực nhanh: 394–1.000 token/giây.
- Miễn phí với rate limit hợp lý.
- Model `llama-3.1-8b-instant` đủ thông minh cho tư vấn cơ bản.
- Chi phí: **$0/tháng** (free tier).

**Gemini 3.8 Flash cho Vision:**

- Model mới nhất, phát hành 02/09/2026, thay thế 3.7 Flash.
- Hỗ trợ đa phương thức: text, image, audio, video, PDF.
- 1M-token input window, 64K-token output limit.
- Giá: $0.75/1M input, $3.75/1M output (giá khuyến mãi đến 31/12/2026).
- Hiệu suất vượt trội trên DeepSWE, Vals Finance Agent V2, Harvey's Legal Agent Benchmark.

**OpenRouter cho Fallback:**

- Cung cấp 17+ model miễn phí với suffix `:free`.
- Router `openrouter/free` tự động chọn model free khả dụng.
- Đảm bảo hệ thống không gián đoạn khi Groq/Gemini gặp sự cố.
- Chi phí: **$0/tháng** (free tier).

---

## 6. TÍCH HỢP VÀO ỨNG DỤNG PHP

### 6.1. Class `AIService.php`

```php
<?php
/**
 * AIService - Unified AI Client for Mái Nhà Xanh
 * Tất cả request đi qua OmniRoute Gateway.
 * Ứng dụng PHP không cần biết provider nào đang được gọi.
 */
class AIService
{
    private string $baseUrl;
    private string $apiKey;
    private int $timeout;

    public function __construct()
    {
        $this->baseUrl = rtrim(getenv('OMNIROUTE_BASE_URL'), '/');
        $this->apiKey  = getenv('OMNIROUTE_API_KEY');
        $this->timeout = (int) (getenv('AI_CHAT_TIMEOUT') ?: 15);
    }

    /**
     * Chat completion - dùng cho Chatbot AI
     */
    public function chat(string $message, array $history = []): array
    {
        $model = getenv('AI_CHAT_MODEL') ?: 'auto/fast';

        $messages = array_merge($history, [
            ['role' => 'user', 'content' => $message]
        ]);

        return $this->call('/chat/completions', [
            'model'       => $model,
            'messages'    => $messages,
            'max_tokens'  => (int) (getenv('AI_MAX_TOKENS') ?: 1024),
            'temperature' => (float) (getenv('AI_TEMPERATURE') ?: 0.7),
        ]);
    }

    /**
     * Vision completion - dùng cho phân tích ảnh khi đăng bài
     */
    public function vision(string $prompt, string $imageBase64): array
    {
        $model = getenv('AI_VISION_MODEL') ?: 'google/gemini-3.8-flash';

        return $this->call('/chat/completions', [
            'model'    => $model,
            'messages' => [
                [
                    'role'    => 'user',
                    'content' => [
                        ['type' => 'text', 'text' => $prompt],
                        [
                            'type'      => 'image_url',
                            'image_url' => [
                                'url' => 'data:image/jpeg;base64,' . $imageBase64
                            ]
                        ]
                    ]
                ]
            ],
            'max_tokens' => 2048,
        ], (int) (getenv('AI_VISION_TIMEOUT') ?: 30));
    }

    /**
     * Gọi API OmniRoute
     */
    private function call(string $endpoint, array $body, ?int $timeout = null): array
    {
        $ch = curl_init($this->baseUrl . $endpoint);

        curl_setopt_array($ch, [
            CURLOPT_POST           => true,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_TIMEOUT        => $timeout ?? $this->timeout,
            CURLOPT_HTTPHEADER     => [
                'Content-Type: application/json',
                'Authorization: Bearer ' . $this->apiKey,
            ],
            CURLOPT_POSTFIELDS     => json_encode($body, JSON_UNESCAPED_UNICODE),
        ]);

        $response = curl_exec($ch);
        $httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
        $error    = curl_error($ch);
        curl_close($ch);

        if ($error) {
            return ['success' => false, 'error' => 'cURL: ' . $error];
        }

        $data = json_decode($response, true);

        if ($httpCode !== 200) {
            return [
                'success' => false,
                'error'   => $data['error']['message'] ?? 'HTTP ' . $httpCode,
            ];
        }

        return [
            'success' => true,
            'content' => $data['choices'][0]['message']['content'] ?? '',
            'model'   => $data['model'] ?? 'unknown',
            'usage'   => $data['usage'] ?? [],
        ];
    }
}
```

### 6.2. Sử dụng cho Chatbot AI

```php
$ai = new AIService();

$result = $ai->chat(
    'Tìm phòng gần trường, dưới 1.5 triệu',
    $_SESSION['chat_history'] ?? []
);

if ($result['success']) {
    echo $result['content'];
    // Lưu model đã dùng để debug
    error_log('Model used: ' . $result['model']);
} else {
    echo 'Xin lỗi, hệ thống đang bận. Vui lòng thử lại sau.';
}
```

### 6.3. Sử dụng cho phân tích ảnh khi đăng bài

```php
$ai = new AIService();

// Đọc ảnh và encode base64
$imageBase64 = base64_encode(file_get_contents($uploadedFile));

$result = $ai->vision(
    'Phân tích ảnh phòng trọ này. Mô tả: diện tích ước lượng, '
    . 'tiện nghi có trong ảnh (điều hòa, nóng lạnh, giường, tủ, wifi), '
    . 'tình trạng phòng (sạch sẽ, cũ, mới). Trả về JSON.',
    $imageBase64
);

if ($result['success']) {
    $analysis = json_decode($result['content'], true);
    // Lưu vào DB để admin duyệt
}
```

---

## 7. CƠ CHẾ FALLBACK TỰ ĐỘNG

OmniRoute tự động xử lý fallback theo thứ tự:

```
Request từ PHP (model = "auto/fast")
    │
    ▼
OmniRoute Gateway
    │
    ├── 1. Thử Groq (Llama 3.1 8B) — nhanh nhất, miễn phí
    │      │
    │      ├── Thành công → Trả kết quả ✅
    │      │
    │      └── Lỗi (429, timeout, 5xx)
    │             │
    │             ▼
    ├── 2. Thử OpenRouter (openrouter/free)
    │      │
    │      ├── Thành công → Trả kết quả ✅
    │      │
    │      └── Lỗi
    │             │
    │             ▼
    └── 3. Thử Ollama Cloud
           │
           ├── Thành công → Trả kết quả ✅
           │
           └── Lỗi → Trả lỗi về PHP
```

**Ứng dụng PHP không cần biết fallback đã xảy ra.** OmniRoute xử lý tất cả.

---

## 8. CHECKLIST TRIỂN KHAI

### Ngày 1 – Setup OmniRoute

- [ ] Cài Node.js >= 22
- [ ] `npm install -g omniroute`
- [ ] `omniroute` để khởi động
- [ ] Truy cập `http://localhost:20128`
- [ ] Thêm provider **Groq** với API key
- [ ] Thêm provider **Ollama Cloud** với API key
- [ ] Thêm provider **OpenRouter** với API key
- [ ] Thêm provider **Google AI** với API key (Gemini 3.8 Flash)
- [ ] Thêm provider miễn phí không cần key: **Kiro AI**, **OpenCode Free**
- [ ] Tạo endpoint `mai-nha-xanh-app`
- [ ] Generate API key cho ứng dụng
- [ ] Test bằng curl:
  ```bash
  curl http://localhost:20128/v1/chat/completions \
    -H "Authorization: Bearer sk-your-omniroute-key" \
    -H "Content-Type: application/json" \
    -d '{"model":"auto/fast","messages":[{"role":"user","content":"Hello"}]}'
  ```

### Ngày 2 – Tích hợp PHP

- [ ] Tạo file `AIService.php`
- [ ] Cập nhật `.env` của ứng dụng
- [ ] Thay thế code gọi AI cũ bằng `AIService`
- [ ] Test chatbot AI
- [ ] Test phân tích ảnh
- [ ] Test fallback (tạm xóa Groq key, xem OpenRouter có hoạt động không)

### Ngày 3 – Cài đặt Service (tùy chọn)

- [ ] `omniroute install-service`
- [ ] `omniroute start`
- [ ] Kiểm tra `omniroute status`
- [ ] Restart máy, xác nhận OmniRoute tự động chạy

---

## 9. LƯU Ý QUAN TRỌNG

### 9.1. Bảo mật

- **Không commit** file `.env` vào Git.
- **Không hardcode** API key trong code PHP.
- **Giới hạn** port OmniRoute chỉ listen trên `127.0.0.1` (không public ra ngoài).
- **Đặt JWT_SECRET** mạnh, random.

### 9.2. Rate Limit

| Provider     | Free Tier Limit                          | Ghi chú                           |
| ------------ | ---------------------------------------- | ---------------------------------- |
| Groq         | 30 req/phút, 1.000 req/ngày            | Đủ cho giai đoạn MVP           |
| Ollama Cloud | Session/weekly limits (không công bố) | Chỉ áp dụng cho vision          |
| OpenRouter   | 20 req/phút, 50 req/ngày               | Dùng làm fallback                |
| Google AI    | Free tier (Gemini 3.8 Flash)             | Giá khuyến mãi đến 31/12/2026 |

### 9.3. Giám sát

OmniRoute dashboard cung cấp:

- Số lượng request theo ngày
- Chi phí theo provider
- Tỷ lệ lỗi
- Model nào được gọi nhiều nhất
- **Free tier tracking**: ~1.62 tỷ token miễn phí/tháng được tổng hợp từ 489 free-tier entries

### 9.4. Về Gemini 3.8 Flash

Google đã phát hành **Gemini 3.8 Flash** vào ngày 02/09/2026, thay thế cho 3.7 Flash và các phiên bản cũ hơn. Model này hỗ trợ:

- 1M-token input window, 64K-token output limit
- Đa phương thức: text, image, audio, video, PDF
- Hiệu suất vượt trội trên DeepSWE, Vals Finance Agent V2, Harvey's Legal Agent Benchmark

**Khi cấu hình, chỉ cần dùng model ID `google/gemini-3.8-flash`.**

---

## 10. KẾT LUẬN

Kiến trúc này mang lại:

| Lợi ích                                   | Mô tả                                                |
| ------------------------------------------- | ------------------------------------------------------ |
| **Một endpoint duy nhất**           | PHP chỉ cần gọi`http://localhost:20128/v1`        |
| **Một API key duy nhất**            | PHP chỉ cần key do OmniRoute sinh ra                 |
| **Fallback tự động**               | Không bao giờ gián đoạn dịch vụ                 |
| **Tối ưu chi phí**                 | Groq miễn phí cho chat, Gemini 3.8 Flash cho vision  |
| **~1.62 tỷ token miễn phí/tháng** | Tổng hợp từ 489 free-tier entries                   |
| **Dễ mở rộng**                     | Thêm provider mới chỉ cần thêm key vào OmniRoute |
| **Bảo mật**                         | API key tập trung, không lộ trong code              |
| **Giám sát**                        | Dashboard theo dõi mọi request                       |

**Sẵn sàng triển khai trong 2-3 ngày.**
