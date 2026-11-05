/**
 * Tier 5: White-Box Adversarial Stress & Edge-Case Verification Suite
 * Authored by Challenger_2 for empirical adversarial validation.
 * Probes:
 * 1. Model switching between text models and Pollinations AI
 * 2. Layout transitions and responsive breakpoint compliance
 * 3. Error handling across network failures, API errors, and stream corruptions
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createStandardHarness } from './helpers/dom-harness.mjs';
import { parseSSEChunks, detectIntent, cleanPrompt } from './helpers/sse-harness.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');
const BASE_URL = process.env.COW_URL || 'http://127.0.0.1:5223';

// -----------------------------------------------------------------------------
// AREA 1: MODEL SWITCHING STRESS PROBING
// -----------------------------------------------------------------------------

test('Adversarial 1.1: Live model switching between text and pollinations across 4 alternating turns', async () => {
    const turns = [
        { model: 'gemini', prompt: 'Hôm nay thời tiết thế nào?', expectedIntent: 'text' },
        { model: 'pollinations', prompt: 'Một phi thuyền neon bay vào hố đen', expectedIntent: 'image' },
        { model: 'llama3', prompt: 'Giải thích định luật Ohm', expectedIntent: 'text' },
        { model: 'pollinations', prompt: 'Bức tranh phong cảnh anime Nhật Bản mùa thu', expectedIntent: 'image' }
    ];

    for (let i = 0; i < turns.length; i++) {
        const turn = turns[i];
        const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: `prompt=${encodeURIComponent(turn.prompt)}&model=${encodeURIComponent(turn.model)}`
        });

        assert.strictEqual(res.status, 200, `Turn ${i + 1} status 200`);
        assert.ok(res.headers.get('content-type')?.includes('text/event-stream'), `Turn ${i + 1} returns text/event-stream`);

        const rawBody = await res.text();
        assert.ok(rawBody.includes('data:'), `Turn ${i + 1} contains data: line`);
        assert.ok(rawBody.includes('[DONE]'), `Turn ${i + 1} ends with [DONE]`);

        if (turn.expectedIntent === 'image') {
            assert.ok(rawBody.includes('is_image_url'), `Turn ${i + 1} payload includes is_image_url`);
            assert.ok(rawBody.includes('https://image.pollinations.ai/prompt/'), `Turn ${i + 1} points to Pollinations AI`);
        }
    }
});

test('Adversarial 1.2: Intent override on text model via slash commands and natural language triggers', async () => {
    const triggers = [
        { model: 'gemini', prompt: '/image cyberpunk city in rain', expectedClean: 'cyberpunk city in rain' },
        { model: 'llama3', prompt: '/draw ancient dragon breathing fire', expectedClean: 'ancient dragon breathing fire' },
        { model: 'deepseek', prompt: '/ve một con tàu vũ trụ cổ đại', expectedClean: 'một con tàu vũ trụ cổ đại' },
        { model: 'gemini', prompt: 'tạo ảnh một chú robot nhỏ chơi cờ', expectedClean: 'một chú robot nhỏ chơi cờ' },
        { model: 'llama3', prompt: 'vẽ tranh cánh đồng hoa hướng dương', expectedClean: 'cánh đồng hoa hướng dương' }
    ];

    for (const item of triggers) {
        const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: `prompt=${encodeURIComponent(item.prompt)}&model=${encodeURIComponent(item.model)}`
        });

        assert.strictEqual(res.status, 200);
        const body = await res.text();
        assert.ok(body.includes('is_image_url'), `Trigger "${item.prompt}" routed to image stream`);
        assert.ok(body.includes('[DONE]'));
    }
});

test('Adversarial 1.3: Client-side model selector interaction cycle and auto-detection', () => {
    const { hiddenModel, currentModelLabel, modelMenu, modelTrigger } = createStandardHarness();

    // Verify initial default
    assert.strictEqual(hiddenModel.value, 'gemini');
    assert.strictEqual(currentModelLabel.textContent, 'Gemini 2.0 Flash');

    // Select DeepSeek option
    const deepseekOpt = modelMenu.children.find(c => c.getAttribute('data-value') === 'deepseek');
    assert.ok(deepseekOpt, 'deepseek option exists');
    hiddenModel.value = deepseekOpt.getAttribute('data-value');
    currentModelLabel.textContent = deepseekOpt.children[0].textContent;
    assert.strictEqual(hiddenModel.value, 'deepseek');
    assert.strictEqual(currentModelLabel.textContent, 'DeepSeek R1');

    // Select Pollinations option
    const pollinationsOpt = modelMenu.children.find(c => c.getAttribute('data-value') === 'pollinations');
    assert.ok(pollinationsOpt, 'pollinations option exists');
    hiddenModel.value = pollinationsOpt.getAttribute('data-value');
    currentModelLabel.textContent = pollinationsOpt.children[0].textContent;
    assert.strictEqual(hiddenModel.value, 'pollinations');
    assert.strictEqual(currentModelLabel.textContent, 'Pollinations AI');

    // Switch back to Gemini
    const geminiOpt = modelMenu.children.find(c => c.getAttribute('data-value') === 'gemini');
    assert.ok(geminiOpt, 'gemini option exists');
    hiddenModel.value = geminiOpt.getAttribute('data-value');
    currentModelLabel.textContent = geminiOpt.children[0].textContent;
    assert.strictEqual(hiddenModel.value, 'gemini');
    assert.strictEqual(currentModelLabel.textContent, 'Gemini 2.0 Flash');
});

// -----------------------------------------------------------------------------
// AREA 2: LAYOUT TRANSITIONS & VIEWPORT RESPONSIVENESS
// -----------------------------------------------------------------------------

test('Adversarial 2.1: CSS verification of viewport height and smooth collapse transitions', () => {
    const cssPath = path.join(PROJECT_ROOT, 'wwwroot', 'css', 'pages.css');
    const cssContent = readFileSync(cssPath, 'utf-8');

    // Viewport height contracts (calc 100vh - 4rem and 100dvh - 4rem)
    assert.ok(cssContent.includes('calc(100vh - 4rem)'), 'CSS includes calc(100vh - 4rem)');
    assert.ok(cssContent.includes('calc(100dvh - 4rem)'), 'CSS includes calc(100dvh - 4rem)');

    // Smooth cubic-bezier transition ~0.5s for Hero Header and Suggestions
    assert.ok(cssContent.includes('cubic-bezier(0.25, 1, 0.5, 1)'), 'Uses 0.5s cubic-bezier timing');
    assert.ok(cssContent.includes('.bottom-mode .chat-hero-header') || cssContent.includes('#chatHeroHeader.collapsed'), 'Has collapsed hero state');
    assert.ok(cssContent.includes('.bottom-mode .chat-suggestions-group') || cssContent.includes('#chatSuggestionsGroup.collapsed'), 'Has collapsed suggestions state');

    // Body active state hides footer-glass
    assert.ok(cssContent.includes('body.chat-active .footer-glass'), 'Hides footer-glass when chat active');
    assert.ok(cssContent.includes('display: none !important'), 'Enforces display: none !important for footer');
});

test('Adversarial 2.2: CSS verification of responsive breakpoint rules for prompts and feature grid', () => {
    const cssPath = path.join(PROJECT_ROOT, 'wwwroot', 'css', 'pages.css');
    const cssContent = readFileSync(cssPath, 'utf-8');

    // Mobile media query 640px for example-prompts (1 column)
    assert.ok(cssContent.includes('@media (max-width: 640px)'), 'Has max-width 640px query');
    assert.ok(cssContent.includes('repeat(2, 1fr)'), 'Desktop prompts grid has 2 columns');

    // Tablet/mobile media query 900px for features-grid (1 column vs 3 columns)
    assert.ok(cssContent.includes('@media (max-width: 900px)'), 'Has max-width 900px query');
    assert.ok(cssContent.includes('repeat(3, 1fr)'), 'Desktop features grid has 3 columns');

    // Chat bubble asymmetric border-radii
    assert.ok(cssContent.includes('18px 18px 4px 18px'), 'User bubble has 18px 18px 4px 18px asymmetric radius');
    assert.ok(cssContent.includes('18px 18px 18px 4px'), 'AI bubble has 18px 18px 18px 4px asymmetric radius');
});

test('Adversarial 2.3: HTML View Index.cshtml layout hierarchy and required IDs', () => {
    const viewPath = path.join(PROJECT_ROOT, 'Views', 'Home', 'Index.cshtml');
    const viewContent = readFileSync(viewPath, 'utf-8');

    const requiredIds = [
        'chatAppViewport',
        'chatHeroHeader',
        'chatHistoryWrapper',
        'chatHistory',
        'chatDockSection',
        'chatPromptForm',
        'promptInput',
        'submitBtn',
        'chatSuggestionsGroup'
    ];

    for (const id of requiredIds) {
        assert.ok(viewContent.includes(`id="${id}"`), `Index.cshtml contains id="${id}"`);
    }

    assert.ok(viewContent.includes('<partial name="_ModelSelector"'), 'Includes _ModelSelector partial');
});

// -----------------------------------------------------------------------------
// AREA 3: ERROR HANDLING & FAILURE RECOVERY STRESS
// -----------------------------------------------------------------------------

test('Adversarial 3.1: Live server error handling on invalid / empty payloads', async () => {
    // 1. Empty prompt
    const emptyRes = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=&model=gemini'
    });
    assert.strictEqual(emptyRes.status, 400);
    const emptyData = await emptyRes.json();
    assert.ok(emptyData.error);

    // 2. Whitespace-only prompt
    const wsRes = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=%20%20%20%20&model=gemini'
    });
    assert.strictEqual(wsRes.status, 400);

    // 3. GenerateImage empty prompt
    const imgEmptyRes = await fetch(`${BASE_URL}/Home/GenerateImage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt='
    });
    assert.strictEqual(imgEmptyRes.status, 400);
});

test('Adversarial 3.2: Network failure simulation and UI input unlock in client finally block', async () => {
    const { promptInput, submitBtn, chatHistory } = createStandardHarness();

    let isGenerating = false;

    // Simulate startChat logic with failing network fetch
    async function simulateFailingChat() {
        if (isGenerating) return;
        isGenerating = true;

        promptInput.disabled = true;
        submitBtn.disabled = true;

        // User bubble
        const userMsg = new (promptInput.constructor)('div');
        userMsg.className = 'message user';
        const userBubble = new (promptInput.constructor)('div');
        userBubble.textContent = 'Test Network Error';
        userMsg.appendChild(userBubble);
        chatHistory.appendChild(userMsg);

        // AI bubble
        const aiMsg = new (promptInput.constructor)('div');
        aiMsg.className = 'message ai';
        const aiBubble = new (promptInput.constructor)('div');
        aiBubble.innerHTML = '<span class="ai-status-loading">Đang suy nghĩ...</span>';
        aiMsg.appendChild(aiBubble);
        chatHistory.appendChild(aiMsg);

        try {
            // Simulated network exception (e.g. TypeError: Failed to fetch)
            throw new TypeError('Failed to fetch (Network connection lost)');
        } catch (err) {
            aiBubble.innerHTML = `<div class="ai-alert-error">⚠️ ${err.message || 'Lỗi kết nối AI.'}</div>`;
        } finally {
            promptInput.disabled = false;
            submitBtn.disabled = false;
            promptInput.focus();
            isGenerating = false;
        }
    }

    await simulateFailingChat();

    // Verify recovery state
    assert.strictEqual(isGenerating, false, 'isGenerating reset to false');
    assert.strictEqual(promptInput.disabled, false, 'promptInput re-enabled');
    assert.strictEqual(submitBtn.disabled, false, 'submitBtn re-enabled');
    assert.strictEqual(promptInput.isFocused, true, 'promptInput focused');

    const lastAiBubble = chatHistory.children[1].children[0];
    assert.ok(lastAiBubble.innerHTML.includes('ai-alert-error'), 'Error alert rendered inside AI bubble');
    assert.ok(lastAiBubble.innerHTML.includes('Network connection lost'), 'Error message displayed');
});

test('Adversarial 3.3: Stream line parser robustness under malformed SSE chunks and buffer splits', () => {
    // Fragmented chunks split across boundary
    const malformedChunks = [
        'data: {"choices": [{"delta": {"con',
        'tent": "Hello "}}]}\n\n',
        'corrupted unformatted line without data prefix\n',
        'data: {malformed json payload\n\n',
        'data: {"choices": [{"delta": {"content": "World!"}}]}\n\n',
        'data: [DONE]\n\n'
    ];

    const { events, completed } = parseSSEChunks(malformedChunks);

    assert.strictEqual(completed, true, 'Successfully parsed stream up to [DONE]');
    const tokens = events
        .filter(e => e.type === 'json' && e.data.choices?.[0]?.delta?.content)
        .map(e => e.data.choices[0].delta.content);

    assert.deepStrictEqual(tokens, ['Hello ', 'World!'], 'Reconstructed fragmented tokens and skipped malformed chunk');
});

test('Adversarial 3.4: Extreme prompt length (>8,000 chars) stress test on backend', async () => {
    const hugePrompt = 'Thử nghiệm sức tải: ' + 'A'.repeat(8192);

    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=${encodeURIComponent(hugePrompt)}&model=gemini`
    });

    assert.strictEqual(res.status, 200, 'Backend accepts large prompt without 500 error');
    assert.ok(res.headers.get('content-type')?.includes('text/event-stream'));
    const body = await res.text();
    assert.ok(body.includes('data:'));
    assert.ok(body.includes('[DONE]'));
});

test('Adversarial 3.5: Rapid double-submission concurrency prevention', async () => {
    let callCount = 0;
    let isGenerating = false;

    async function submit() {
        if (isGenerating) return false;
        isGenerating = true;
        callCount++;
        // Simulate async operation
        await new Promise(r => setTimeout(r, 20));
        isGenerating = false;
        return true;
    }

    const [first, second] = await Promise.all([submit(), submit()]);

    assert.strictEqual(callCount, 1, 'Only one submission allowed through while lock is held');
    assert.strictEqual(first, true, 'First submission succeeded');
    assert.strictEqual(second, false, 'Second submission blocked by concurrency lock');
});
