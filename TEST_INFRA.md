# COW.AI Test Infrastructure & Methodology (TEST_INFRA.md)

## 1. Overview & Architecture

This document defines the automated end-to-end (E2E) and integration test infrastructure for the COW.AI web application refactoring (Duck.ai-style interactive homepage).

The test suite is built on an **opaque-box testing philosophy**:
- Tests interact with the application through its external boundaries: HTTP/REST endpoints, Server-Sent Events (SSE) data streams, DOM interfaces, and client-side behavioral contracts.
- No internal private methods or implementation details are tightly coupled to the assertions.
- The test suite is completely self-contained, isolated, repeatable, and requires **zero external npm package installations**.

### Runtime Environment
- **Runtime:** Node.js v24+ (built-in `node:test`, `node:assert/strict`, native `fetch`, `ReadableStream`, `TextDecoder`)
- **Target Application:** ASP.NET Core 10 Kestrel server running at `http://127.0.0.1:5223`
- **Fallback / Standalone Simulation:** High-fidelity DOM & SSE simulation harnesses for fully offline or headless CI/CD execution.

---

## 2. The 4-Tier Testing Methodology

The test suite is structured into 4 complementary tiers mapping directly to the 19 features defined in `PROJECT.md`:

```
┌────────────────────────────────────────────────────────────────────────┐
│ TIER 1: Feature Coverage (>=5 test cases per feature area)             │
│ - 10 feature areas: Layout, Centered, Bottom, User bubble, AI bubble, │
│   ModelSelector, Theme toggle, SSE text, SSE image, Intent detection  │
│ - Total: >= 50 tests verifying functional requirements (F1 - F10)      │
├────────────────────────────────────────────────────────────────────────┤
│ TIER 2: Boundary & Corner Cases                                        │
│ - Empty/whitespace prompts, >4000 char inputs, XSS escaping (<script>),│
│   Unicode/emojis (🐮✨), slash commands (/image, /draw), debounce,     │
│   and AbortController disconnect handling                              │
├────────────────────────────────────────────────────────────────────────┤
│ TIER 3: Cross-Feature Combinations                                     │
│ - Pairwise interactions: ModelSelector + SSE chat, Pollinations +      │
│   image stream, Theme dark/light + chat history bubbles, Enter key +   │
│   layout transition + input lock, Markdown + Prism code highlight      │
├────────────────────────────────────────────────────────────────────────┤
│ TIER 4: Real-World Scenarios                                           │
│ - Multi-turn conversation roundtrips, coding assistance flows with     │
│   Prism syntax highlighting, creative image generation flows,          │
│   mid-session model switching, and error resilience recovery           │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Feature Mapping to 19 Inventory Features

| # | Feature | Scope | Primary Verification Tier |
|---|---|---|---|
| F1 | Centered Mode Layout | Layout on initial load | Tier 1 (Area 1, 2) |
| F2 | Bottom-Docked Transition | Cubic-bezier transition to bottom on send | Tier 1 (Area 3), Tier 3 |
| F3 | Chat History Container | `#chatHistory` container emergence & auto-scroll | Tier 1 (Area 3), Tier 4 |
| F4 | User Chat Bubble | Right-aligned, escaped text, gradient styling | Tier 1 (Area 4), Tier 2, 3 |
| F5 | AI Chat Bubble | Left-aligned, glassmorphism, typing indicator | Tier 1 (Area 5), Tier 3, 4 |
| F6 | Preservation of Global Features | Three.js canvas, Navbar, Theme, ModelSelector | Tier 1 (Area 1, 6, 7) |
| F7 | Backend StreamChat SSE Action | `POST /Home/StreamChat` headers & stream | Tier 1 (Area 8), Tier 2 |
| F8 | Backend Intent Dispatching | Text vs Image intent classification | Tier 1 (Area 10), Tier 3 |
| F9 | OpenRouter Token Streaming | Streaming text tokens via SSE | Tier 1 (Area 8), Tier 4 |
| F10 | Pollinations SSE Packaging | Image URL packaging into SSE | Tier 1 (Area 9), Tier 3, 4 |
| F11 | Dotnet Build Zero Errors | Clean build with `dotnet build` | Tier 1 (Area 1) |
| F12 | Client Event Handling & Input Lock | Enter key, input disabled during streaming | Tier 1 (Area 3), Tier 3 |
| F13 | Client Layout Switching | Layout switch triggered on first send | Tier 1 (Area 3), Tier 3 |
| F14 | Client SSE Stream Reader | Buffer-safe chunk parsing (`lines.pop()`) | Tier 1 (Area 8), Tier 2 |
| F15 | Markdown & Syntax Highlighting | marked.js parsing & Prism.js highlighting | Tier 3, Tier 4 |
| F16 | Image Rendering in AI Bubble | Responsive `<img>` rendering in AI bubble | Tier 1 (Area 9), Tier 4 |
| F17 | Auto-Scroll on Stream | Scroll to bottom on token stream & image load | Tier 1 (Area 3), Tier 4 |
| F18 | E2E Test Suite Readiness | Unified runner passes 100% | All Tiers & Runner |
| F19 | Adversarial Coverage Hardening | White-box stress-testing & fuzzing | Tier 2, Tier 3 |

---

## 4. Test Directory Layout

```
tests/
├── helpers/
│   ├── dom-harness.mjs       # Headless DOM simulation harness for client-side chat logic
│   └── sse-harness.mjs       # SSE parser, chunk splitter, and mock stream generator
├── tier1-feature-coverage.test.mjs    # Tier 1: >=50 feature coverage tests
├── tier2-boundary-corner.test.mjs     # Tier 2: Boundary, corner, and stress tests
├── tier3-cross-combinations.test.mjs  # Tier 3: Pairwise feature combination tests
├── tier4-real-world-scenarios.test.mjs# Tier 4: Multi-turn real-world scenario tests
└── run-all-tests.mjs                  # Unified test runner reporting aggregated results
```

---

## 5. Execution Instructions

### Run Entire Test Suite (All 4 Tiers)
```powershell
node tests/run-all-tests.mjs
```

### Run Individual Tiers with Node.js Built-in Runner
```powershell
node --test tests/tier1-feature-coverage.test.mjs
node --test tests/tier2-boundary-corner.test.mjs
node --test tests/tier3-cross-combinations.test.mjs
node --test tests/tier4-real-world-scenarios.test.mjs
```

### Target Custom Server URL
By default, tests connect to `http://127.0.0.1:5223`. To override:
```powershell
$env:COW_URL="http://localhost:5000"; node tests/run-all-tests.mjs
```

---

## 6. Authoritative Expected Output Derivation

1. **HTTP Endpoints:**
   - `GET /` -> HTTP status 200, HTML contains `#promptInput`, `#submitBtn`, `#modelSelector`, `#three-canvas`.
   - `POST /Home/StreamChat` with empty prompt -> HTTP status 400 Bad Request.
   - `POST /Home/StreamChat` with valid prompt -> HTTP status 200, Content-Type `text/event-stream`, Cache-Control `no-cache`, Connection `keep-alive`.
   - `POST /Home/GenerateImage` with valid prompt -> HTTP status 200, JSON containing `{ url: "https://image.pollinations.ai/prompt/..." }`.

2. **Client-Side Behavior:**
   - In centered mode, `#chatHistory` is collapsed/inactive and hero suggestions are visible.
   - On first prompt submission, `.chat-app-viewport` gains `.bottom-mode` (or container gains `.bottom-mode`), suggestions gain `.hidden`, `#chatHistory` gains `.active`.
   - User prompt is appended as `.message.user` with sanitized `textContent`.
   - AI response is appended as `.message.ai` with typing indicator replaced by parsed markdown HTML.
   - Input field and send button are disabled during stream transmission and restored in `finally`.
   - `#chatHistory.scrollTop` is updated to `#chatHistory.scrollHeight` upon each token append.

---

## 7. Pass / Fail Acceptance Criteria

- All tests across Tiers 1 through 4 must pass with **0 failures**.
- Test execution must finish within seconds (< 5 seconds for full suite).
- Any regression in interface contracts immediately triggers an assertion failure with an actionable failure message.
