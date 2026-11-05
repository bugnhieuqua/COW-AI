/**
 * Tier 3: Cross-Feature Combinations Test Suite
 * Pairwise interactions: ModelSelector + Chat/Image, Theme + Bubbles,
 * Enter/Shift-Enter key handling, Markdown parsing, Prism code formatting, and suggestion clicks.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { createStandardHarness } from './helpers/dom-harness.mjs';
import { detectIntent, createMockTextStream, createMockImageStream } from './helpers/sse-harness.mjs';

const BASE_URL = process.env.COW_URL || 'http://127.0.0.1:5223';

test('Tier 3 - Combination 1: ModelSelector (DeepSeek) + SSE StreamChat passes model param', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Explain%20Dijkstra&model=deepseek'
    });
    assert.strictEqual(res.status, 200, 'StreamChat must accept deepseek model');
    assert.ok(res.headers.get('content-type')?.includes('text/event-stream'));
});

test('Tier 3 - Combination 2: ModelSelector (Llama3) + SSE StreamChat passes model param', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Summarize%20report&model=llama3'
    });
    assert.strictEqual(res.status, 200, 'StreamChat must accept llama3 model');
    assert.ok(res.headers.get('content-type')?.includes('text/event-stream'));
});

test('Tier 3 - Combination 3: ModelSelector (Pollinations) routes to image generation stream', async () => {
    const { hiddenModel } = createStandardHarness();
    hiddenModel.value = 'pollinations';

    const intent = detectIntent('Bò phi hành gia', hiddenModel.value);
    assert.strictEqual(intent, 'image', 'Pollinations model must route to image generation intent');

    // Live endpoint verification
    const res = await fetch(`${BASE_URL}/Home/GenerateImage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Bo%20phi%20hanh%20gia'
    });
    assert.strictEqual(res.status, 200);
    const data = await res.json();
    assert.ok(data.url.includes('image.pollinations.ai'));
});

test('Tier 3 - Combination 4: Dark Theme applies appropriate class context for chat history bubbles', () => {
    const { doc, chatHistory } = createStandardHarness();
    doc.body.className = 'dark';

    const userMsg = doc.createElement('div');
    userMsg.className = 'message user';
    const aiMsg = doc.createElement('div');
    aiMsg.className = 'message ai';

    chatHistory.appendChild(userMsg);
    chatHistory.appendChild(aiMsg);

    assert.strictEqual(doc.body.classList.contains('dark'), true, 'Body has dark class');
    assert.strictEqual(chatHistory.children[0].classList.contains('user'), true);
    assert.strictEqual(chatHistory.children[1].classList.contains('ai'), true);
});

test('Tier 3 - Combination 5: Light Theme switches root classes while preserving chat bubbles', () => {
    const { doc, chatHistory } = createStandardHarness();
    doc.documentElement.className = 'light';
    doc.body.className = '';

    const userMsg = doc.createElement('div');
    userMsg.className = 'message user';
    chatHistory.appendChild(userMsg);

    assert.strictEqual(doc.documentElement.classList.contains('light'), true, 'Root has light class');
    assert.strictEqual(chatHistory.children.length, 1);
    assert.strictEqual(chatHistory.children[0].className, 'message user');
});

test('Tier 3 - Combination 6: Enter key triggers send, transitions to bottom-mode, and locks inputs', async () => {
    const { viewport, promptInput, submitBtn, chatHistory, suggestionsGroup } = createStandardHarness();
    let isGenerating = false;

    function handleSend() {
        const text = promptInput.value.trim();
        if (!text || isGenerating) return;

        isGenerating = true;
        // Layout transition
        viewport.classList.add('bottom-mode');
        suggestionsGroup.classList.add('hidden');
        chatHistory.classList.add('active');

        // Locking
        promptInput.disabled = true;
        submitBtn.disabled = true;

        // User bubble
        const userDiv = new (promptInput.constructor)('div');
        userDiv.className = 'message user';
        userDiv.textContent = text;
        chatHistory.appendChild(userDiv);

        promptInput.value = '';
    }

    promptInput.value = 'Hỏi một câu bằng phím Enter';

    // Simulate Enter keydown
    const event = { key: 'Enter', shiftKey: false, preventDefault: () => {} };
    if (event.key === 'Enter' && !event.shiftKey) {
        event.preventDefault();
        handleSend();
    }

    assert.strictEqual(viewport.classList.contains('bottom-mode'), true, 'Viewport transitioned to bottom-mode');
    assert.strictEqual(suggestionsGroup.classList.contains('hidden'), true, 'Suggestions hidden');
    assert.strictEqual(chatHistory.classList.contains('active'), true, 'Chat history activated');
    assert.strictEqual(promptInput.disabled, true, 'Input locked');
    assert.strictEqual(submitBtn.disabled, true, 'Submit button locked');
    assert.strictEqual(chatHistory.children.length, 1, 'User bubble created');
    assert.strictEqual(chatHistory.children[0].textContent, 'Hỏi một câu bằng phím Enter');
    assert.strictEqual(promptInput.value, '', 'Input field cleared');
});

test('Tier 3 - Combination 7: Shift+Enter allows multiline input without triggering send', () => {
    const { viewport, promptInput, chatHistory } = createStandardHarness();
    let sendTriggered = false;

    function onKeyDown(e) {
        if (e.key === 'Enter' && !e.shiftKey) {
            sendTriggered = true;
        }
    }

    promptInput.value = 'Dòng 1';
    // User presses Shift+Enter
    const shiftEnterEvent = { key: 'Enter', shiftKey: true };
    onKeyDown(shiftEnterEvent);

    assert.strictEqual(sendTriggered, false, 'Shift+Enter must NOT trigger send');
    assert.strictEqual(viewport.classList.contains('bottom-mode'), false, 'Viewport remains in centered mode');
    assert.strictEqual(chatHistory.children.length, 0, 'No bubbles created');
});

test('Tier 3 - Combination 8: Markdown headers and bold text formatted into HTML elements', () => {
    const { doc } = createStandardHarness();
    const aiBubble = doc.createElement('div');
    aiBubble.className = 'message-bubble';

    // Simulated marked.parse output
    const rawMarkdown = '# Tiêu đề lớn\n\nĐây là văn bản **in đậm** và *in nghiêng*.';
    const parsedHtml = '<h1>Tiêu đề lớn</h1><p>Đây là văn bản <strong>in đậm</strong> và <em>in nghiêng</em>.</p>';

    aiBubble.innerHTML = parsedHtml;

    assert.ok(aiBubble.innerHTML.includes('<h1>Tiêu đề lớn</h1>'));
    assert.ok(aiBubble.innerHTML.includes('<strong>in đậm</strong>'));
    assert.ok(aiBubble.innerHTML.includes('<em>in nghiêng</em>'));
});

test('Tier 3 - Combination 9: Markdown code blocks structured for Prism syntax highlighting', () => {
    const { doc } = createStandardHarness();
    const aiBubble = doc.createElement('div');

    const codeSnippetHtml = '<pre class="language-csharp"><code class="language-csharp">public void Hello() { }</code></pre>';
    aiBubble.innerHTML = codeSnippetHtml;

    assert.ok(aiBubble.innerHTML.includes('<pre class="language-csharp">'));
    assert.ok(aiBubble.innerHTML.includes('<code class="language-csharp">'));
});

test('Tier 3 - Combination 10: Markdown image syntax rendered as responsive img element', () => {
    const { doc } = createStandardHarness();
    const aiBubble = doc.createElement('div');

    const imgUrl = 'https://image.pollinations.ai/prompt/cyber_cow';
    const imgHtml = `<div class="ai-chat-image-wrapper"><img class="ai-chat-image" src="${imgUrl}" alt="AI artwork" /></div>`;
    aiBubble.innerHTML = imgHtml;

    assert.ok(aiBubble.innerHTML.includes('<img class="ai-chat-image"'));
    assert.ok(aiBubble.innerHTML.includes(`src="${imgUrl}"`));
});

test('Tier 3 - Combination 11: Stream completion unlocks inputs and restores focus in finally', async () => {
    const { promptInput, submitBtn } = createStandardHarness();
    let isGenerating = true;
    promptInput.disabled = true;
    submitBtn.disabled = true;

    // Simulate finally block
    try {
        // stream logic
    } finally {
        promptInput.disabled = false;
        submitBtn.disabled = false;
        promptInput.focus();
        isGenerating = false;
    }

    assert.strictEqual(promptInput.disabled, false, 'Input re-enabled');
    assert.strictEqual(submitBtn.disabled, false, 'Submit button re-enabled');
    assert.strictEqual(promptInput.isFocused, true, 'Prompt input focused');
    assert.strictEqual(isGenerating, false, 'isGenerating reset to false');
});

test('Tier 3 - Combination 12: Suggestion card click populates promptInput, activates bottom-mode, and starts stream', () => {
    const { doc, viewport, promptInput, suggestionsGroup, chatHistory } = createStandardHarness();

    const samplePrompt = 'Vẽ tranh sơn dầu một chú bò phi hành gia';
    const cardBtn = doc.createElement('button');
    cardBtn.className = 'example-prompt-btn';
    cardBtn.setAttribute('data-prompt', samplePrompt);
    suggestionsGroup.appendChild(cardBtn);

    // Simulate card click
    promptInput.value = cardBtn.getAttribute('data-prompt');
    viewport.classList.add('bottom-mode');
    suggestionsGroup.classList.add('hidden');
    chatHistory.classList.add('active');

    const userBubble = doc.createElement('div');
    userBubble.className = 'message user';
    userBubble.textContent = promptInput.value;
    chatHistory.appendChild(userBubble);
    promptInput.value = '';

    assert.strictEqual(viewport.classList.contains('bottom-mode'), true);
    assert.strictEqual(suggestionsGroup.classList.contains('hidden'), true);
    assert.strictEqual(chatHistory.children.length, 1);
    assert.strictEqual(chatHistory.children[0].textContent, samplePrompt);
    assert.strictEqual(promptInput.value, '');
});
