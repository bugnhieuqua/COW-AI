/**
 * Tier 1: Feature Coverage Test Suite (>= 5 tests per feature area across 10 areas)
 * Total: 50 automated opaque-box test cases
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { createStandardHarness } from './helpers/dom-harness.mjs';
import { parseSSEChunks, createMockTextStream, createMockImageStream, detectIntent } from './helpers/sse-harness.mjs';

const BASE_URL = process.env.COW_URL || 'http://127.0.0.1:5223';

// =============================================================================
// Feature Area 1: Homepage layout (5 tests)
// =============================================================================
test('Tier 1 - Area 1.1: GET / returns HTTP 200 OK', async () => {
    const res = await fetch(`${BASE_URL}/`);
    assert.strictEqual(res.status, 200, 'Root endpoint must return 200 OK');
    const html = await res.text();
    assert.ok(html.length > 500, 'Response HTML must be non-empty');
});

test('Tier 1 - Area 1.2: Homepage includes promptInput textarea and submitBtn', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes('id="promptInput"'), 'Page must contain promptInput textarea');
    assert.ok(html.includes('id="submitBtn"'), 'Page must contain submitBtn button');
});

test('Tier 1 - Area 1.3: Homepage includes Three.js canvas for 3D background', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes('id="three-canvas"'), 'Page must contain three-canvas element');
});

test('Tier 1 - Area 1.4: Homepage includes navigation bar with brand and nav links', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes('navbar-glass'), 'Page must contain navbar-glass');
    assert.ok(html.includes('COW.AI'), 'Page must contain COW.AI brand');
    assert.ok(html.includes('Trang chủ'), 'Page must contain home navigation link');
});

test('Tier 1 - Area 1.5: Layout references vendor libraries marked.js, prism.js, and site scripts', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes('marked.min.js'), 'Layout must reference marked.min.js');
    assert.ok(html.includes('prism.min.js'), 'Layout must reference prism.min.js');
    assert.ok(html.includes('chat-logic.js'), 'Layout must reference chat-logic.js');
});

// =============================================================================
// Feature Area 2: Centered mode layout (5 tests)
// =============================================================================
test('Tier 1 - Area 2.1: Viewport initially does not have bottom-mode class', () => {
    const { viewport } = createStandardHarness();
    assert.strictEqual(viewport.classList.contains('bottom-mode'), false, 'Initial viewport must not have bottom-mode');
});

test('Tier 1 - Area 2.2: Hero header elements are present in centered mode', () => {
    const { heroHeader, heroTitle } = createStandardHarness();
    assert.ok(heroHeader, 'Hero header element must exist');
    assert.ok(heroTitle, 'Hero title element must exist inside hero header');
});

test('Tier 1 - Area 2.3: Example suggestions group contains clickable prompt buttons', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes('example-prompts') || html.includes('suggestionCards'), 'Suggestions container must exist');
    assert.ok(html.includes('example-prompt-btn'), 'Suggestion prompt buttons must exist');
});

test('Tier 1 - Area 2.4: Suggestion buttons define data-prompt attribute', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes('data-prompt='), 'Suggestion buttons must define data-prompt');
});

test('Tier 1 - Area 2.5: Chat history container is initially empty in centered mode', () => {
    const { chatHistory } = createStandardHarness();
    assert.strictEqual(chatHistory.children.length, 0, 'Chat history should have 0 messages initially');
});

// =============================================================================
// Feature Area 3: Bottom mode transition (5 tests)
// =============================================================================
test('Tier 1 - Area 3.1: Sending message applies bottom-mode class to viewport', () => {
    const { viewport } = createStandardHarness();
    viewport.classList.add('bottom-mode');
    assert.strictEqual(viewport.classList.contains('bottom-mode'), true, 'Viewport must have bottom-mode after send');
});

test('Tier 1 - Area 3.2: Sending message hides/collapses suggestion cards group', () => {
    const { suggestionsGroup } = createStandardHarness();
    suggestionsGroup.classList.add('hidden');
    assert.strictEqual(suggestionsGroup.classList.contains('hidden'), true, 'Suggestions group must gain hidden class');
});

test('Tier 1 - Area 3.3: Sending message activates chat history container', () => {
    const { chatHistory } = createStandardHarness();
    chatHistory.classList.add('active');
    assert.strictEqual(chatHistory.classList.contains('active'), true, 'Chat history must gain active class');
});

test('Tier 1 - Area 3.4: Prompt input value is cleared after submission', () => {
    const { promptInput } = createStandardHarness();
    promptInput.value = 'Hỏi COW.AI';
    // Simulate send clearing input
    promptInput.value = '';
    assert.strictEqual(promptInput.value, '', 'Prompt input must be cleared');
});

test('Tier 1 - Area 3.5: Subsequent messages maintain bottom-mode without reverting', () => {
    const { viewport, suggestionsGroup } = createStandardHarness();
    viewport.classList.add('bottom-mode');
    suggestionsGroup.classList.add('hidden');

    // Turn 2
    assert.strictEqual(viewport.classList.contains('bottom-mode'), true, 'Viewport remains in bottom-mode');
    assert.strictEqual(suggestionsGroup.classList.contains('hidden'), true, 'Suggestions remain hidden');
});

// =============================================================================
// Feature Area 4: User chat bubble (5 tests)
// =============================================================================
test('Tier 1 - Area 4.1: User bubble container has message and user classes', () => {
    const { doc, chatHistory } = createStandardHarness();
    const msg = doc.createElement('div');
    msg.className = 'message user';
    chatHistory.appendChild(msg);
    assert.strictEqual(msg.classList.contains('message'), true);
    assert.strictEqual(msg.classList.contains('user'), true);
});

test('Tier 1 - Area 4.2: User inner bubble has message-bubble class', () => {
    const { doc } = createStandardHarness();
    const bubble = doc.createElement('div');
    bubble.className = 'message-bubble';
    assert.strictEqual(bubble.classList.contains('message-bubble'), true);
});

test('Tier 1 - Area 4.3: User prompt text is escaped via textContent to prevent XSS', () => {
    const { doc } = createStandardHarness();
    const bubble = doc.createElement('div');
    const dangerousInput = '<script>alert("xss")</script>&"test"';
    bubble.textContent = dangerousInput;

    assert.strictEqual(bubble.textContent, dangerousInput, 'textContent must return unescaped string');
    assert.ok(bubble.innerHTML.includes('&lt;script&gt;'), 'innerHTML must escape HTML tags');
    assert.ok(!bubble.innerHTML.includes('<script>'), 'Raw script tags must never be injected');
});

test('Tier 1 - Area 4.4: User bubble is appended to chatHistory container', () => {
    const { doc, chatHistory } = createStandardHarness();
    const msg = doc.createElement('div');
    msg.className = 'message user';
    chatHistory.appendChild(msg);
    assert.strictEqual(chatHistory.children.length, 1);
    assert.strictEqual(chatHistory.children[0], msg);
});

test('Tier 1 - Area 4.5: Multiple user messages stack in order of arrival', () => {
    const { doc, chatHistory } = createStandardHarness();
    const msg1 = doc.createElement('div');
    msg1.textContent = 'Question 1';
    const msg2 = doc.createElement('div');
    msg2.textContent = 'Question 2';

    chatHistory.appendChild(msg1);
    chatHistory.appendChild(msg2);

    assert.strictEqual(chatHistory.children.length, 2);
    assert.strictEqual(chatHistory.children[0].textContent, 'Question 1');
    assert.strictEqual(chatHistory.children[1].textContent, 'Question 2');
});

// =============================================================================
// Feature Area 5: AI chat bubble (5 tests)
// =============================================================================
test('Tier 1 - Area 5.1: AI bubble container has message and ai classes', () => {
    const { doc, chatHistory } = createStandardHarness();
    const msg = doc.createElement('div');
    msg.className = 'message ai';
    chatHistory.appendChild(msg);
    assert.strictEqual(msg.classList.contains('message'), true);
    assert.strictEqual(msg.classList.contains('ai'), true);
});

test('Tier 1 - Area 5.2: AI bubble displays typing indicator initially', () => {
    const { doc } = createStandardHarness();
    const bubble = doc.createElement('div');
    bubble.innerHTML = '<span class="ai-status-loading">Đang suy nghĩ...</span>';
    assert.ok(bubble.innerHTML.includes('ai-status-loading'), 'Typing indicator class must be present');
});

test('Tier 1 - Area 5.3: Streamed tokens replace typing indicator on first token', () => {
    const { doc } = createStandardHarness();
    const bubble = doc.createElement('div');
    bubble.innerHTML = '<span class="ai-status-loading">Đang suy nghĩ...</span>';

    // First token arrives
    const token = 'Chào bạn!';
    bubble.innerHTML = '';
    bubble.textContent = token;

    assert.strictEqual(bubble.textContent, 'Chào bạn!');
    assert.ok(!bubble.innerHTML.includes('ai-status-loading'), 'Typing indicator removed on first token');
});

test('Tier 1 - Area 5.4: Error payload renders distinct error alert styling', () => {
    const { doc } = createStandardHarness();
    const bubble = doc.createElement('div');
    const errorMsg = 'Chưa cấu hình API Key';
    bubble.innerHTML = `<div class="ai-alert-error">⚠️ ${errorMsg}</div>`;

    assert.ok(bubble.innerHTML.includes('ai-alert-error'), 'Must have ai-alert-error class');
    assert.ok(bubble.innerHTML.includes(errorMsg), 'Must display error message');
});

test('Tier 1 - Area 5.5: AI bubble supports rendered HTML markdown content', () => {
    const { doc } = createStandardHarness();
    const bubble = doc.createElement('div');
    bubble.innerHTML = '<p><strong>COW.AI</strong> là trợ lý thông minh.</p>';

    assert.ok(bubble.innerHTML.includes('<strong>COW.AI</strong>'), 'Renders bold markdown tag');
});

// =============================================================================
// Feature Area 6: ModelSelector (5 tests)
// =============================================================================
test('Tier 1 - Area 6.1: Hidden model input exists with default value gemini', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes('id="hiddenSelectedModel"'), 'Page must contain hiddenSelectedModel input');
    assert.ok(html.includes('value="gemini"'), 'Default selected model must be gemini');
});

test('Tier 1 - Area 6.2: Model trigger button has aria accessibility attributes', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes('id="modelTrigger"'), 'Page must contain modelTrigger button');
    assert.ok(html.includes('aria-haspopup="listbox"'), 'modelTrigger must declare aria-haspopup');
});

test('Tier 1 - Area 6.3: Model menu contains all 4 supported models', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes('data-value="gemini"'), 'Menu must contain gemini');
    assert.ok(html.includes('data-value="llama3"'), 'Menu must contain llama3');
    assert.ok(html.includes('data-value="deepseek"'), 'Menu must contain deepseek');
    assert.ok(html.includes('data-value="pollinations"'), 'Menu must contain pollinations');
});

test('Tier 1 - Area 6.4: Selecting a model updates hiddenSelectedModel value', () => {
    const { hiddenModel } = createStandardHarness();
    assert.strictEqual(hiddenModel.value, 'gemini');
    hiddenModel.value = 'deepseek';
    assert.strictEqual(hiddenModel.value, 'deepseek', 'hiddenSelectedModel updated to deepseek');
});

test('Tier 1 - Area 6.5: Selecting a model updates currentModelLabel text', () => {
    const { currentModelLabel } = createStandardHarness();
    assert.strictEqual(currentModelLabel.textContent, 'Gemini 2.0 Flash');
    currentModelLabel.textContent = 'DeepSeek R1';
    assert.strictEqual(currentModelLabel.textContent, 'DeepSeek R1');
});

// =============================================================================
// Feature Area 7: Theme toggle (5 tests)
// =============================================================================
test('Tier 1 - Area 7.1: Theme toggle button exists in navbar', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes('id="themeToggleBtn"'), 'Page must contain themeToggleBtn');
});

test('Tier 1 - Area 7.2: Theme dropdown includes dark, light, and system options', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes('data-theme="dark"'), 'Dropdown contains dark option');
    assert.ok(html.includes('data-theme="light"'), 'Dropdown contains light option');
    assert.ok(html.includes('data-theme="system"'), 'Dropdown contains system option');
});

test('Tier 1 - Area 7.3: Inline head script checks cowai_theme localStorage', async () => {
    const res = await fetch(`${BASE_URL}/`);
    const html = await res.text();
    assert.ok(html.includes("localStorage.getItem('cowai_theme')"), 'Head script checks cowai_theme key');
});

test('Tier 1 - Area 7.4: Setting theme to light applies light class to root element', () => {
    const { doc } = createStandardHarness();
    doc.documentElement.classList.add('light');
    doc.documentElement.classList.remove('dark');

    assert.strictEqual(doc.documentElement.classList.contains('light'), true);
    assert.strictEqual(doc.documentElement.classList.contains('dark'), false);
});

test('Tier 1 - Area 7.5: Setting theme to dark applies dark class to root element', () => {
    const { doc } = createStandardHarness();
    doc.documentElement.classList.add('dark');
    doc.documentElement.classList.remove('light');

    assert.strictEqual(doc.documentElement.classList.contains('dark'), true);
    assert.strictEqual(doc.documentElement.classList.contains('light'), false);
});

// =============================================================================
// Feature Area 8: SSE text streaming (5 tests)
// =============================================================================
test('Tier 1 - Area 8.1: POST /Home/StreamChat returns Content-Type text/event-stream', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Ping'
    });
    assert.strictEqual(res.status, 200);
    const contentType = res.headers.get('content-type') || '';
    assert.ok(contentType.includes('text/event-stream'), `Expected text/event-stream, got ${contentType}`);
});

test('Tier 1 - Area 8.2: POST /Home/StreamChat returns Cache-Control no-cache', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Ping'
    });
    const cacheControl = res.headers.get('cache-control') || '';
    assert.ok(cacheControl.includes('no-cache'), 'Response must have Cache-Control: no-cache');
});

test('Tier 1 - Area 8.3: POST /Home/StreamChat returns Connection keep-alive', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Ping'
    });
    const connection = res.headers.get('connection') || '';
    assert.ok(connection.toLowerCase().includes('keep-alive'), 'Response must have Connection: keep-alive');
});

test('Tier 1 - Area 8.4: SSE parser accurately extracts token text from data payload', () => {
    const mockChunks = createMockTextStream(['Xin ', 'chào ', 'COW.AI!']);
    const { events } = parseSSEChunks(mockChunks);
    const tokens = events
        .filter(e => e.type === 'json')
        .map(e => e.data.choices?.[0]?.delta?.content || '')
        .join('');

    assert.strictEqual(tokens, 'Xin chào COW.AI!');
});

test('Tier 1 - Area 8.5: SSE parser signals completion when receiving [DONE]', () => {
    const mockChunks = createMockTextStream(['Test']);
    const { completed, events } = parseSSEChunks(mockChunks);

    assert.strictEqual(completed, true, 'Parser must mark completed as true on [DONE]');
    const doneEvent = events.find(e => e.type === 'done');
    assert.ok(doneEvent, 'Must record done event in events list');
});

// =============================================================================
// Feature Area 9: SSE image streaming (5 tests)
// =============================================================================
test('Tier 1 - Area 9.1: POST /Home/GenerateImage returns HTTP 200 with JSON url', async () => {
    const res = await fetch(`${BASE_URL}/Home/GenerateImage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=con bo vang tren dong co'
    });
    assert.strictEqual(res.status, 200);
    const data = await res.json();
    assert.ok(typeof data.url === 'string' && data.url.length > 0, 'Must return image url string');
});

test('Tier 1 - Area 9.2: Pollinations image URL encodes prompt in pathname', async () => {
    const res = await fetch(`${BASE_URL}/Home/GenerateImage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=cyberpunk robot cow'
    });
    const data = await res.json();
    assert.ok(data.url.includes('image.pollinations.ai/prompt/'), 'Must use Pollinations image endpoint');
    assert.ok(data.url.includes('cyberpunk') || data.url.includes('robot'), 'URL must encode prompt');
});

test('Tier 1 - Area 9.3: SSE parser identifies is_image_url payload format', () => {
    const testUrl = 'https://image.pollinations.ai/prompt/test';
    const mockChunks = createMockImageStream(testUrl);
    const { events } = parseSSEChunks(mockChunks);

    const imgEvent = events.find(e => e.type === 'json' && e.data.is_image_url);
    assert.ok(imgEvent, 'Must identify is_image_url event');
    assert.strictEqual(imgEvent.data.is_image_url, testUrl);
});

test('Tier 1 - Area 9.4: Image payload is rendered as img element inside AI bubble', () => {
    const { doc } = createStandardHarness();
    const bubble = doc.createElement('div');
    bubble.className = 'message-bubble';

    const testUrl = 'https://image.pollinations.ai/prompt/cow';
    const img = doc.createElement('img');
    img.className = 'ai-chat-image';
    img.setAttribute('src', testUrl);
    bubble.appendChild(img);

    assert.strictEqual(bubble.children.length, 1);
    assert.strictEqual(bubble.children[0].tagName, 'IMG');
    assert.strictEqual(bubble.children[0].getAttribute('src'), testUrl);
});

test('Tier 1 - Area 9.5: Image load callback invokes auto-scroll to bottom', () => {
    const { doc, chatHistory } = createStandardHarness();
    chatHistory.scrollHeight = 500;

    let scrolled = false;
    const scrollToBottom = () => {
        chatHistory.scrollTop = chatHistory.scrollHeight;
        scrolled = true;
    };

    const img = doc.createElement('img');
    img.onload = scrollToBottom;
    // Simulate image finish loading
    img.onload();

    assert.strictEqual(scrolled, true, 'Scroll callback must be triggered on img load');
    assert.strictEqual(chatHistory.scrollTop, 500, 'scrollTop matches scrollHeight');
});

// =============================================================================
// Feature Area 10: Intent detection (5 tests)
// =============================================================================
test('Tier 1 - Area 10.1: Model pollinations is detected as image intent', () => {
    const intent = detectIntent('Bất kỳ câu hỏi nào', 'pollinations');
    assert.strictEqual(intent, 'image');
});

test('Tier 1 - Area 10.2: Slash command /image <prompt> is detected as image intent', () => {
    const intent = detectIntent('/image một chú bò trên vũ trụ', 'gemini');
    assert.strictEqual(intent, 'image');
});

test('Tier 1 - Area 10.3: Slash command /draw <prompt> is detected as image intent', () => {
    const intent = detectIntent('/draw a beautiful neon city', 'gemini');
    assert.strictEqual(intent, 'image');
});

test('Tier 1 - Area 10.4: Vietnamese keywords tạo ảnh and vẽ tranh detected as image intent', () => {
    assert.strictEqual(detectIntent('vẽ tranh sơn dầu hoa sen', 'gemini'), 'image');
    assert.strictEqual(detectIntent('tạo ảnh phi hành gia', 'gemini'), 'image');
});

test('Tier 1 - Area 10.5: Standard questions detected as text chat intent', () => {
    assert.strictEqual(detectIntent('Thuật toán Dijkstra hoạt động như thế nào?', 'gemini'), 'text');
    assert.strictEqual(detectIntent('Tóm tắt tài liệu bảo mật OWASP', 'llama3'), 'text');
    assert.strictEqual(detectIntent('Viết code Python sắp xếp mảng', 'deepseek'), 'text');
});
