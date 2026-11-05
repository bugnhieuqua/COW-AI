# Project: COW.AI Homepage Duck.ai-Style Refactoring

## Architecture
The COW.AI web application is an ASP.NET Core (.NET 10) application serving Razor Views with client-side JavaScript, Three.js 3D background rendering, CSS styling (Dark/Light themes), OpenRouter AI text streaming, and Pollinations AI image generation.

### Module Boundaries & Data Flow:
1. **Frontend View & Styles (`Views/Home/Index.cshtml`, `wwwroot/css/pages.css`)**:
   - Manages the full-height viewport (`.chat-app-viewport`).
   - 2-state layout: Centered Mode (`#chatHeroHeader`, `#chatSuggestionsGroup` visible) vs Bottom-Docked Mode (`#chatHeroHeader`, `#chatSuggestionsGroup` collapsed to `max-height: 0; opacity: 0;`).
   - Hosts the Chat History container (`#chatHistory`) with auto-scroll and bubble containers.
   - Hosts the bottom-docked Prompt Bar (`#chatDockSection`) containing `#promptInput`, `#submitBtn`, and `#modelSelector`.
2. **Backend SSE Streaming (`Controllers/HomeController.cs`, `Services/OpenRouterService.cs`)**:
   - `[HttpPost] public async Task<IActionResult> StreamChat(string prompt, string? model, CancellationToken cancellationToken)`.
   - Dispatches intent: text chat vs image generation (`DetectImageIntent`).
   - Text chat: Streams tokens from OpenRouter API via SSE (`data: <token>\n\n`) with `Response.Body.FlushAsync()`.
   - Image generation: Obtains image URL from `PollinationsService.GetImageUrl(prompt)`, packages as dual-mode SSE payload, ends with `data: [DONE]\n\n`.
3. **Client Logic & SSE Integration (`wwwroot/js/chat-logic.js`)**:
   - Binds Enter key (Shift+Enter for newline) and Send button click.
   - On first message: toggles `bottom-mode` on viewport, adds `body.chat-active`, appends User bubble, appends AI bubble.
   - Disables input & submit button during active streaming; re-enables in `finally`.
   - Consumes SSE via `fetch(..., { signal })`, `ReadableStream`, `TextDecoder`, and robust line buffer `buffer = lines.pop()`.
   - Renders Markdown via `marked.parse()`, highlights code via `Prism.highlightAllUnder()`, renders responsive image elements, auto-scrolls `#chatHistory.scrollTop = #chatHistory.scrollHeight`.
   - Preserves `_ModelSelector` interaction, theme switcher, and auxiliary tools (`initImageGen`, `initDocSummary`, `initCodeGen`).
4. **E2E Testing Track (`tests/`)**:
   - Independent opaque-box test suite executing against HTTP server `http://127.0.0.1:5223`.
   - Covers 4 Tiers: Feature Coverage (Tier 1), Boundary & Edge Cases (Tier 2), Cross-Feature Combinations (Tier 3), Real-World Scenarios (Tier 4).
   - Publishes `TEST_READY.md`.

---

## Feature Inventory
Every requirement and acceptance criterion from `ORIGINAL_REQUEST.md` is mapped to an assigned milestone:

| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| F1 | Centered Mode Layout | Prompt bar centered vertically with hero header and suggestion cards on initial load | M1 | R1, AC-UI1 |
| F2 | Bottom-Docked Transition | Smooth CSS cubic-bezier (~0.5s) transition docking prompt bar to bottom and collapsing suggestions on first send | M1 | R1, AC-UI2 |
| F3 | Chat History Container | `#chatHistory` container emerges and expands dynamically to fill viewport height with auto-scroll | M1 | R1, R2, AC-UI3 |
| F4 | User Chat Bubble | Right-aligned, distinctive gradient background, asymmetric border-radius, word-wrap | M1 | R2, AC-UI3 |
| F5 | AI Chat Bubble | Left-aligned, glassmorphic blur background, subtle border, asymmetric border-radius | M1 | R2, AC-UI3 |
| F6 | Preservation of Global Features | Preserve Three.js background canvas, fixed Navbar, Theme switcher, and ModelSelector dropdown | M1 | R1, AC-Source2 |
| F7 | Backend StreamChat SSE Action | `HomeController.StreamChat` returning `text/event-stream`, `no-cache`, `keep-alive`, `X-Accel-Buffering: no` | M2 | R3, AC-Stream1 |
| F8 | Backend Intent Dispatching | `DetectImageIntent` classifying text vs image based on model ("pollinations"), slash commands, and prompt keywords | M2 | R3, AC-Stream3 |
| F9 | OpenRouter Token Streaming | Streaming text tokens via SSE `data: <token>\n\n` with immediate `FlushAsync()` and cancellation token handling | M2 | R3, AC-Stream1 |
| F10 | Pollinations SSE Packaging | Packaging image URL into SSE payload compatible with Markdown and ending with `data: [DONE]\n\n` | M2 | R3, AC-Stream3 |
| F11 | Dotnet Build Zero Errors | Project builds cleanly with `dotnet build` with 0 compilation errors | M2 | AC-Source1 |
| F12 | Client Event Handling & Input Lock | Enter key & click listeners, disabling input/button during streaming, re-enabling on completion | M3 | R4, AC-Stream4 |
| F13 | Client Layout Switching | Triggering `bottom-mode` transition on first message submission | M3 | R4, AC-UI2 |
| F14 | Client SSE Stream Reader | Consuming SSE using `fetch`, `ReadableStream`, `TextDecoder`, and robust line buffer `lines.pop()` | M3 | R4, AC-Stream1 |
| F15 | Markdown & Syntax Highlighting | Parsing markdown with `marked.js` and highlighting code blocks with `Prism.js` in AI bubbles | M3 | R2, R4 |
| F16 | Image Rendering in AI Bubble | Rendering responsive `<img>` tags directly inside AI bubbles upon receiving image payload | M3 | R2, R4, AC-Stream3 |
| F17 | Auto-Scroll on Stream | Incrementally scrolling `#chatHistory` to bottom on each incoming token and on image load | M3 | R2, R4, AC-Stream2 |
| F18 | E2E Test Suite Readiness | Comprehensive opaque-box test suite verifying Tiers 1-4 with 100% pass | M_E2E | Dual Track |
| F19 | Adversarial Coverage Hardening | White-box stress-testing and adversarial edge-case verification (Tier 5) | M4 | Final Milestone |

---

## Milestones

| # | Name | Scope | File Write Ownership | Dependencies | Status |
|---|------|-------|----------------------|-------------|--------|
| M_E2E | E2E Testing Suite Track | Opaque-box test suite (Tiers 1-4), test runner, and `TEST_READY.md` | `tests/**`, `TEST_INFRA.md`, `TEST_READY.md` | none (independent) | DONE |
| M1 | Frontend View & Styles (R1 & R2) | Razor structure in `Index.cshtml`, 2-state CSS layout, chat bubbles, suggestions, and glassmorphism in `pages.css` | `Views/Home/Index.cshtml`, `wwwroot/css/pages.css` | none | DONE |
| M2 | Backend SSE Streaming & Intent Dispatch (R3) | `StreamChat` action, `DetectImageIntent`, SSE headers, OpenRouter streaming, Pollinations packaging | `Controllers/HomeController.cs`, `Services/OpenRouterService.cs` | none | DONE |
| M3 | Client Logic & SSE Consumer (R4) | `chat-logic.js` event listeners, layout transition trigger, bubble creation, SSE reader, Markdown/Prism, auto-scroll | `wwwroot/js/chat-logic.js` | M1, M2 | DONE |
| M4 | Final Integration, 100% E2E Pass & Adversarial Hardening | Pass 100% E2E tests (Tiers 1-4), Tier 5 adversarial hardening, zero build errors, final verification | All modules (bug fixes only) | M1, M2, M3, M_E2E | DONE |

---

## Interface Contracts

### 1. DOM Elements & CSS Selectors (M1 ↔ M3):
- Viewport container: `.chat-app-viewport` (with state class `.bottom-mode` when chat is active).
- Body class when chat is active: `body.chat-active` (hides `.footer-glass`).
- Collapsible elements in centered mode: `#chatHeroHeader`, `#chatSuggestionsGroup` (collapsed via `.collapsed` or `.bottom-mode #chatHeroHeader`).
- Chat History container: `#chatHistory` (flex-column, `overflow-y: auto`, `scroll-behavior: smooth`).
- User bubble: `.chat-bubble.chat-bubble-user` (right-aligned, gradient background).
- AI bubble: `.chat-bubble.chat-bubble-ai` (left-aligned, glassmorphic background, containing `.chat-bubble-content`).
- Prompt bar container: `#chatDockSection` (docked at bottom in `.bottom-mode`).
- Form elements: `#chatPromptForm`, `#promptInput`, `#submitBtn`, `#hiddenSelectedModel`.
- Model Selector trigger & menu: `#modelTrigger`, `#currentModelLabel`, `#modelMenu`.

### 2. Backend SSE Stream Protocol (M2 ↔ M3 ↔ M_E2E):
- Endpoint: `POST /Home/StreamChat`
- Request Payload (form-encoded or multipart):
  - `prompt`: string (required)
  - `model`: string (optional, e.g. "gemini", "llama3", "deepseek", "pollinations")
- Response Headers:
  - `Content-Type: text/event-stream; charset=utf-8`
  - `Cache-Control: no-cache, no-transform`
  - `Connection: keep-alive`
  - `X-Accel-Buffering: no`
- SSE Chunks:
  - Text token: `data: <token_text>\n\n`
  - Image payload: `data: {"is_image_url": "https://image.pollinations.ai/prompt/...", "choices": [{"delta": {"content": "![image](https://image.pollinations.ai/prompt/...)\n\n"}}]}\n\n`
  - Stream Termination: `data: [DONE]\n\n`
  - Error: `data: [ERROR] <message>\n\n` followed by `data: [DONE]\n\n`

---

## Code Layout
- `Views/Home/Index.cshtml`: Main chat viewport, hero header, suggestions, chat history container, prompt dock.
- `Views/Shared/_ModelSelector.cshtml`: Reusable AI model selector popover.
- `Views/Shared/_Layout.cshtml`: Global wrapper, Three.js canvas, Navbar, marked.js, Prism.js.
- `wwwroot/css/pages.css`: Page-specific styles for homepage chat viewport, transitions, and chat bubbles.
- `wwwroot/css/site.css`: Global styles, Three.js canvas styling, CSS variables.
- `Controllers/HomeController.cs`: MVC controller handling `Index` and `StreamChat`.
- `Services/OpenRouterService.cs`: OpenRouter API client supporting streaming completions.
- `Services/PollinationsService.cs`: Pollinations image URL generator.
- `wwwroot/js/chat-logic.js`: Client-side chat logic, SSE streaming reader, bubble renderer, layout mode toggle.
- `tests/`: Opaque-box E2E test suites and runner.
