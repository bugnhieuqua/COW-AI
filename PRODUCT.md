# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users
Software engineers, technical architects, and developers who need instant, terminal-friendly AI code generation, algorithm optimization, syntax debugging, and multimodal assistance without risking proprietary code privacy.

## Product Purpose
Provide a high-performance, private, developer-centric AI workspace. COW.AI delivers real-time token streaming, multi-model auto-routing (e.g. DeepSeek R1 for reasoning/code, Gemini for broad context), studio-quality Vietnamese and multilingual voice synthesis (Edge TTS DRM v2), and code tooling with zero server-side conversation logging.

## Positioning
A zero-server-footprint developer AI workspace (100% client LocalStorage) with dynamic prompt separation ("Zero Hardcoded Prompts" via `Prompts/`), multi-model intent routing, and real-time SSE streaming running on modern ASP.NET Core 10 and a lightweight 60 FPS Three.js canvas. Unlike cloud-centric LLM platforms (ChatGPT, Claude), no user code, prompt text, or session data ever touches a persistent server database.

## Operating Context
Daily software development, code refactoring, algorithm debugging, architecture planning, document summarization, and prompt-driven image generation. Developers run it locally or as a self-hosted instance, interacting via web browser (keyboard-first, Markdown rendering with Prism.js code syntax highlighting, Web Speech STT, and Edge TTS playback).

## Capabilities and Constraints
- **Confirmed Capabilities**:
  - Real-time Server-Sent Events (SSE) token streaming via OpenRouter API.
  - Multi-model routing (DeepSeek R1 for code/math, Gemini for general queries, Llama 3 for rapid chat, Pollinations for image generation).
  - High-performance, zero-cost Edge TTS DRM v2 audio streaming (Vietnamese voices: NamMinh, HoaiMy) with player controls.
  - Client-side Web Speech API Speech-to-Text (STT) input.
  - Client-side file attachment reading via `FileReader` (`.cs`, `.py`, `.sql`, `.txt`).
  - 100% client-side session management in `LocalStorage` with Markdown session export.
  - Specialized workflows: Document Summary (`/Document/Summary`) and Code Generation (`/Document/CodeGen`).
- **Technical Constraints**:
  - ASP.NET Core 10 (.NET 10.0) backend with Razor Views.
  - Vanilla JavaScript client, Three.js 3D background canvas, CSS custom properties.
  - Zero server database for user chats or telemetry (stateless proxy).
  - External prompt templates decoupled in `Prompts/` directory.

## Brand Commitments
- **Name**: COW.AI
- **Tagline / Core Promise**: All chats are private; high-speed developer intelligence.
- **Voice**: Technical, precise, utilitarian, and uncluttered. Developer-friendly, crisp, and responsive.

## Evidence on Hand
- Architectural documentation and specs: `PROJECT.md`, `docs/BaoCaoTongThe_COW_AI.md`, `docs/COW_AI_Design_Philosophy.md`.
- ASP.NET Core 10 backend implementations: `Controllers/HomeController.cs`, `Services/OpenRouterService.cs`, `Services/EdgeTtsClient.cs`.
- Frontend views and styles: `Views/Home/Index.cshtml`, `Views/Shared/_Layout.cshtml`, `wwwroot/css/pages.css`, `wwwroot/css/components.css`, `wwwroot/js/chat-logic.js`.
- 10-Tier E2E automated testing suite: `tests/`.

## Product Principles
1. **Privacy-by-Design & Zero Server Footprint**: User data, prompts, and chat sessions belong solely to the client; the server remains a stateless gateway.
2. **Developer-First Ergonomics**: Prioritize rapid response times (<800ms TTFT), keyboard shortcuts, clean syntax highlighting, and terminal/code clarity over decorative fluff.
3. **Zero Hardcoded Prompts**: Keep prompts strictly decoupled in editable template files (`Prompts/`), ensuring maintainability without recompilation.
4. **Lightweight & High-FPS Response**: Keep the client fast (60 FPS rendering, lightweight Vanilla JS + Three.js) without heavyweight SPA overhead.

## Accessibility & Inclusion
- Full keyboard navigability (Enter to submit, Shift+Enter for multiline, Tab navigation, accessible input states).
- High-contrast code syntax highlighting (Prism.js) across dark and light themes.
- Studio-grade audio readback (Edge TTS) supporting both audio playback and visual transcripts.
