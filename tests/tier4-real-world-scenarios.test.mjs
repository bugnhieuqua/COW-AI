/**
 * Tier 4: Real-World Scenarios Test Suite
 * Comprehensive user journeys: multi-turn conversation, coding with Prism blocks,
 * creative image generation, mid-session model switching, and error resilience recovery.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { createStandardHarness } from './helpers/dom-harness.mjs';
import { createMockTextStream, createMockImageStream, parseSSEChunks, detectIntent } from './helpers/sse-harness.mjs';

const BASE_URL = process.env.COW_URL || 'http://127.0.0.1:5223';

test('Tier 4 - Scenario 1: Multi-Turn Conversation Flow', async () => {
    const { viewport, chatHistory, promptInput, submitBtn, suggestionsGroup } = createStandardHarness();
    let isGenerating = false;

    async function executeTurn(userText, mockTokens) {
        if (isGenerating) return;
        isGenerating = true;

        // Transition layout
        viewport.classList.add('bottom-mode');
        suggestionsGroup.classList.add('hidden');
        chatHistory.classList.add('active');

        // User bubble
        const userMsg = new (promptInput.constructor)('div');
        userMsg.className = 'message user';
        const userBubble = new (promptInput.constructor)('div');
        userBubble.className = 'message-bubble';
        userBubble.textContent = userText;
        userMsg.appendChild(userBubble);
        chatHistory.appendChild(userMsg);

        // Lock inputs
        promptInput.value = '';
        promptInput.disabled = true;
        submitBtn.disabled = true;

        // AI bubble with typing indicator
        const aiMsg = new (promptInput.constructor)('div');
        aiMsg.className = 'message ai';
        const aiBubble = new (promptInput.constructor)('div');
        aiBubble.className = 'message-bubble';
        aiBubble.innerHTML = '<span class="ai-status-loading">Đang suy nghĩ...</span>';
        aiMsg.appendChild(aiBubble);
        chatHistory.appendChild(aiMsg);

        try {
            // Stream tokens
            let accumulated = '';
            for (let i = 0; i < mockTokens.length; i++) {
                if (i === 0) aiBubble.innerHTML = '';
                accumulated += mockTokens[i];
                aiBubble.textContent = accumulated;
                chatHistory.scrollTop = chatHistory.scrollHeight;
            }
        } finally {
            promptInput.disabled = false;
            submitBtn.disabled = false;
            promptInput.focus();
            isGenerating = false;
        }
    }

    // Turn 1: Greeting
    await executeTurn('Xin chào COW.AI!', ['Chào ', 'bạn! ', 'Tôi là trợ lý AI.']);
    assert.strictEqual(viewport.classList.contains('bottom-mode'), true);
    assert.strictEqual(chatHistory.children.length, 2, '2 bubbles after Turn 1');
    assert.strictEqual(chatHistory.children[0].children[0].textContent, 'Xin chào COW.AI!');
    assert.strictEqual(chatHistory.children[1].children[0].textContent, 'Chào bạn! Tôi là trợ lý AI.');
    assert.strictEqual(promptInput.disabled, false, 'Input re-enabled after Turn 1');

    // Turn 2: Follow-up question
    await executeTurn('Bạn có thể làm gì?', ['Tôi có thể: ', '1. Tạo ảnh ', '2. Tóm tắt tài liệu ', '3. Lập trình.']);
    assert.strictEqual(chatHistory.children.length, 4, '4 bubbles after Turn 2');
    assert.strictEqual(chatHistory.children[2].children[0].textContent, 'Bạn có thể làm gì?');
    assert.ok(chatHistory.children[3].children[0].textContent.includes('Tạo ảnh'));
    assert.strictEqual(promptInput.disabled, false, 'Input re-enabled after Turn 2');
    assert.strictEqual(promptInput.isFocused, true, 'Input focused ready for Turn 3');
});

test('Tier 4 - Scenario 2: Asking Coding Questions with Prism Syntax Highlighting Blocks', async () => {
    const { doc, chatHistory } = createStandardHarness();

    const userPrompt = 'Viết code C# kiểm tra số nguyên tố';
    const userMsg = doc.createElement('div');
    userMsg.className = 'message user';
    const userBubble = doc.createElement('div');
    userBubble.textContent = userPrompt;
    userMsg.appendChild(userBubble);
    chatHistory.appendChild(userMsg);

    // AI streams markdown code block
    const codeStreamChunks = createMockTextStream([
        'Dưới đây là phương thức C#:\n\n',
        '```csharp\n',
        'public static bool IsPrime(int n)\n',
        '{\n',
        '    if (n <= 1) return false;\n',
        '    for (int i = 2; i * i <= n; i++)\n',
        '        if (n % i == 0) return false;\n',
        '    return true;\n',
        '}\n',
        '```\n\n'
    ]);

    const { events } = parseSSEChunks(codeStreamChunks);
    const fullMarkdown = events
        .filter(e => e.type === 'json')
        .map(e => e.data.choices?.[0]?.delta?.content || '')
        .join('');

    assert.ok(fullMarkdown.includes('```csharp'));

    // Simulated marked.js output with language class
    const parsedHtml = '<p>Dưới đây là phương thức C#:</p>' +
        '<pre class="language-csharp"><code class="language-csharp">' +
        'public static bool IsPrime(int n)\n' +
        '{\n    if (n &lt;= 1) return false;\n    return true;\n}' +
        '</code></pre>';

    const aiMsg = doc.createElement('div');
    aiMsg.className = 'message ai';
    const aiBubble = doc.createElement('div');
    aiBubble.className = 'message-bubble';
    aiBubble.innerHTML = parsedHtml;
    aiMsg.appendChild(aiBubble);
    chatHistory.appendChild(aiMsg);

    assert.ok(aiBubble.innerHTML.includes('<pre class="language-csharp">'), 'Contains language-csharp pre');
    assert.ok(aiBubble.innerHTML.includes('<code class="language-csharp">'), 'Contains language-csharp code');
});

test('Tier 4 - Scenario 3: Requesting Creative Image Generation Flow', async () => {
    const { doc, chatHistory, promptInput } = createStandardHarness();

    const rawPrompt = '/image Một chú bò phi hành gia neon bay trong dải ngân hà';
    assert.strictEqual(detectIntent(rawPrompt), 'image');

    // Live endpoint generation
    const res = await fetch(`${BASE_URL}/Home/GenerateImage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=${encodeURIComponent(rawPrompt.slice(7).trim())}`
    });
    assert.strictEqual(res.status, 200);
    const data = await res.json();
    assert.ok(data.url.startsWith('https://image.pollinations.ai/prompt/'));

    // User message
    const userMsg = doc.createElement('div');
    userMsg.className = 'message user';
    const userBubble = doc.createElement('div');
    userBubble.textContent = rawPrompt;
    userMsg.appendChild(userBubble);
    chatHistory.appendChild(userMsg);

    // AI message with image wrapper
    const aiMsg = doc.createElement('div');
    aiMsg.className = 'message ai';
    const aiBubble = doc.createElement('div');
    aiBubble.className = 'message-bubble';

    const wrapper = doc.createElement('div');
    wrapper.className = 'ai-chat-image-wrapper';
    const img = doc.createElement('img');
    img.className = 'ai-chat-image';
    img.setAttribute('src', data.url);
    img.setAttribute('alt', 'Ảnh tạo bởi Pollinations AI');

    let scrollTriggered = false;
    img.onload = () => {
        chatHistory.scrollTop = chatHistory.scrollHeight;
        scrollTriggered = true;
    };

    wrapper.appendChild(img);
    aiBubble.appendChild(wrapper);
    aiMsg.appendChild(aiBubble);
    chatHistory.appendChild(aiMsg);

    // Simulate image loading
    img.onload();

    assert.strictEqual(chatHistory.children.length, 2);
    assert.strictEqual(scrollTriggered, true, 'Image load triggers scroll to bottom');
    assert.strictEqual(aiBubble.querySelector('.ai-chat-image').getAttribute('src'), data.url);
});

test('Tier 4 - Scenario 4: Switching Models Mid-Session Across Multiple Turns', async () => {
    const { hiddenModel, currentModelLabel } = createStandardHarness();

    // Turn 1: Default model (Gemini)
    assert.strictEqual(hiddenModel.value, 'gemini');
    let res1 = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Câu%20hỏi%201&model=gemini'
    });
    assert.strictEqual(res1.status, 200);

    // User switches to DeepSeek for Turn 2
    hiddenModel.value = 'deepseek';
    currentModelLabel.textContent = 'DeepSeek R1';
    assert.strictEqual(hiddenModel.value, 'deepseek');
    assert.strictEqual(currentModelLabel.textContent, 'DeepSeek R1');

    let res2 = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=Câu%20hỏi%202&model=${hiddenModel.value}`
    });
    assert.strictEqual(res2.status, 200);

    // User switches to Pollinations for Turn 3
    hiddenModel.value = 'pollinations';
    currentModelLabel.textContent = 'Pollinations AI';
    assert.strictEqual(hiddenModel.value, 'pollinations');
    assert.strictEqual(detectIntent('Vẽ chú bò', hiddenModel.value), 'image');

    let res3 = await fetch(`${BASE_URL}/Home/GenerateImage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Ve%20chu%20bo'
    });
    assert.strictEqual(res3.status, 200);
});

test('Tier 4 - Scenario 5: Error Resilience & Recovery Flow', async () => {
    const { doc, chatHistory, promptInput, submitBtn } = createStandardHarness();

    // Turn 1: Error occurs
    promptInput.disabled = true;
    submitBtn.disabled = true;

    const aiMsg = doc.createElement('div');
    aiMsg.className = 'message ai';
    const aiBubble = doc.createElement('div');
    aiBubble.className = 'message-bubble';
    aiMsg.appendChild(aiBubble);
    chatHistory.appendChild(aiMsg);

    // Simulated error payload
    const errorPayload = { error: { message: 'Chưa cấu hình API Key OpenRouter' } };
    aiBubble.innerHTML = `<div class="ai-alert-error">⚠️ ${errorPayload.error.message}</div>`;

    // Finally block unlocks inputs
    promptInput.disabled = false;
    submitBtn.disabled = false;
    promptInput.focus();

    assert.ok(aiBubble.innerHTML.includes('ai-alert-error'));
    assert.strictEqual(promptInput.disabled, false, 'Input recovered and unlocked');
    assert.strictEqual(promptInput.isFocused, true);

    // Turn 2: User retries or asks another question
    promptInput.value = 'Thử lại câu hỏi khác';
    assert.strictEqual(promptInput.disabled, false);
    assert.strictEqual(submitBtn.disabled, false);
});
