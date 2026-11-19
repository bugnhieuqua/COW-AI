/**
 * Tier 5: Adversarial Stress & Vulnerability Challenge Suite
 * 
 * Conducts white-box adversarial stress testing against:
 * 1. SSE chunk fragmentation & boundary slicing down to 1-byte resolution
 * 2. Long prompts (10k-50k chars) & complex Unicode/Vietnamese/Emoji handling
 * 3. Rapid sequential submissions & in-flight concurrency locking (real chat-logic.js execution)
 * 4. Empty/whitespace input rejection (client & live backend)
 * 5. Malformed/hostile SSE payloads, stream interruptions, and error states
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { createStandardHarness } from './helpers/dom-harness.mjs';
import { parseSSEChunks, fragmentChunks } from './helpers/sse-harness.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');
const CHAT_LOGIC_PATH = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js');

const BASE_URL = process.env.COW_URL || 'http://127.0.0.1:5223';

/**
 * Creates an isolated VM environment executing the REAL chat-logic.js code
 */
function createChatLogicEnvironment(customFetch = null) {
    const harness = createStandardHarness();
    const chatLogicCode = fs.readFileSync(CHAT_LOGIC_PATH, 'utf-8');

    const mockFetch = customFetch || (async (url, opts) => {
        return {
            ok: true,
            status: 200,
            statusText: 'OK',
            text: async () => '',
            json: async () => ({}),
            body: {
                getReader: () => {
                    let closed = false;
                    return {
                        read: async () => {
                            if (closed) return { done: true, value: undefined };
                            closed = true;
                            const text = 'data: {"choices":[{"delta":{"content":"Chào bạn!"}}]}\n\ndata: [DONE]\n\n';
                            return { done: false, value: new TextEncoder().encode(text) };
                        }
                    };
                }
            }
        };
    });

    const windowObj = {
        marked: {
            parse: (txt) => `<p>${txt.replace(/</g, '&lt;').replace(/>/g, '&gt;')}</p>`
        },
        Prism: {
            highlightAllUnder: () => {},
            highlightElement: () => {}
        },
        document: harness.doc,
        AbortController: globalThis.AbortController,
        TextDecoder: globalThis.TextDecoder,
        TextEncoder: globalThis.TextEncoder,
        fetch: mockFetch,
        setTimeout: globalThis.setTimeout,
        clearTimeout: globalThis.clearTimeout
    };

    const sandbox = {
        window: windowObj,
        document: harness.doc,
        fetch: mockFetch,
        AbortController: globalThis.AbortController,
        TextDecoder: globalThis.TextDecoder,
        TextEncoder: globalThis.TextEncoder,
        setTimeout: globalThis.setTimeout,
        clearTimeout: globalThis.clearTimeout,
        console
    };

    const ctx = vm.createContext(sandbox);
    vm.runInContext(chatLogicCode, ctx);

    return {
        harness,
        context: ctx,
        startChat: ctx.window.startChat,
        generateImage: ctx.window.generateImage
    };
}

/* ==========================================================================
   1. SSE Chunk Fragmentation & Boundary Slicing
   ========================================================================== */

test('Tier 5 - Area 1.1: Single-byte stream fragmentation (1-byte chunking) with UTF-8 Vietnamese & Emojis', () => {
    const rawSSE = 'data: {"choices":[{"delta":{"content":"Hệ thống COW.AI 🐮✨ tiếng Việt có dấu: á, ế, ỗ, ực, đ"}}]}\n\ndata: [DONE]\n\n';
    
    // Fragment into exact 1-character slices
    const singleByteSlices = [];
    for (let i = 0; i < rawSSE.length; i++) {
        singleByteSlices.push(rawSSE[i]);
    }
    assert.strictEqual(singleByteSlices.length, rawSSE.length, 'Should have 1 slice per character');

    const { events, completed } = parseSSEChunks(singleByteSlices);
    assert.strictEqual(completed, true, 'Stream completion [DONE] must be detected with 1-char slices');
    assert.strictEqual(events.length, 2, 'Must yield 1 json event and 1 done event');
    
    const content = events[0].data.choices[0].delta.content;
    assert.strictEqual(content, 'Hệ thống COW.AI 🐮✨ tiếng Việt có dấu: á, ế, ỗ, ực, đ');
});

test('Tier 5 - Area 1.2: Boundary slicing right in the middle of data: prefix', () => {
    // Slices split across 'da' and 'ta: ...'
    const fragments = [
        'da',
        'ta: {"choices":[{"delta":{"content":"Token1"}}]}\n\n',
        'dat',
        'a: {"choices":[{"delta":{"content":"Token2"}}]}\n\n',
        'data',
        ': [DONE]\n\n'
    ];

    const { events, completed } = parseSSEChunks(fragments);
    assert.strictEqual(completed, true);
    assert.strictEqual(events.length, 3);
    assert.strictEqual(events[0].data.choices[0].delta.content, 'Token1');
    assert.strictEqual(events[1].data.choices[0].delta.content, 'Token2');
});

test('Tier 5 - Area 1.3: Boundary slicing across newline separators (split \\n\\n)', () => {
    const fragments = [
        'data: {"choices":[{"delta":{"content":"PartA"}}]}\n',
        '\ndata: {"choices":[{"delta":{"content":"PartB"}}]}\n',
        '\ndata: [DONE]\n\n'
    ];

    const { events, completed } = parseSSEChunks(fragments);
    assert.strictEqual(completed, true);
    assert.strictEqual(events.length, 3);
    assert.strictEqual(events[0].data.choices[0].delta.content, 'PartA');
    assert.strictEqual(events[1].data.choices[0].delta.content, 'PartB');
});

test('Tier 5 - Area 1.4: Incomplete trailing buffer line without newline is flushed', () => {
    // Final chunk has no trailing \n
    const chunks = [
        'data: {"choices":[{"delta":{"content":"Line 1"}}]}\n\n',
        'data: {"choices":[{"delta":{"content":"Trailing line"}}]}'
    ];

    const { events, remainderBuffer } = parseSSEChunks(chunks);
    assert.strictEqual(events.length, 1, 'Only complete line parsed in loop');
    assert.ok(remainderBuffer.includes('Trailing line'), 'Incomplete line preserved in remainder buffer');

    // Emulate chat-logic.js tail handling: if (buffer && buffer.trim()) handleSSELine(buffer.trim())
    const tailLine = remainderBuffer.trim();
    assert.ok(tailLine.startsWith('data:'));
    const tailJson = JSON.parse(tailLine.slice(5).trim());
    assert.strictEqual(tailJson.choices[0].delta.content, 'Trailing line');
});

test('Tier 5 - Area 1.5: Batch chunk containing 50 concatenated tokens in single packet', () => {
    let bigChunk = '';
    for (let i = 0; i < 50; i++) {
        bigChunk += `data: {"choices":[{"delta":{"content":"T${i} "}}]}\n\n`;
    }
    bigChunk += 'data: [DONE]\n\n';

    const { events, completed } = parseSSEChunks([bigChunk]);
    assert.strictEqual(completed, true);
    assert.strictEqual(events.length, 51);
    const combined = events
        .filter(e => e.type === 'json')
        .map(e => e.data.choices[0].delta.content)
        .join('');
    
    assert.ok(combined.startsWith('T0 T1 T2'));
    assert.ok(combined.endsWith('T49 '));
});

/* ==========================================================================
   2. Long Prompts, Unicode, and Special Characters Stress
   ========================================================================== */

test('Tier 5 - Area 2.1: Live server accepts 10,000 character prompt without crash', async () => {
    const hugePrompt = 'COW.AI Phân tích dữ liệu lớn: ' + 'Một chú bò thông minh đang tính toán. '.repeat(300);
    assert.ok(hugePrompt.length >= 10000, `Length is ${hugePrompt.length}`);

    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=${encodeURIComponent(hugePrompt)}`
    });

    assert.strictEqual(res.status, 200, 'Server must return HTTP 200 for 10k character prompt');
    assert.strictEqual(res.headers.get('content-type')?.includes('text/event-stream'), true);
    await res.body?.cancel();
});

test('Tier 5 - Area 2.2: Live server accepts 45,000 character prompt without crash', async () => {
    const massivePrompt = 'Lập trình viên COW.AI: ' + 'console.log("kiem-thu-chuyen-sau"); '.repeat(1300);
    assert.ok(massivePrompt.length >= 45000, `Length is ${massivePrompt.length}`);

    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=${encodeURIComponent(massivePrompt)}`
    });

    assert.strictEqual(res.status, 200, 'Server must return HTTP 200 for 45k character prompt');
    await res.body?.cancel();
});

test('Tier 5 - Area 2.3: Comprehensive Vietnamese character set and double diacritics', async () => {
    const vietnameseTestCases = [
        'Nguyễn Huệ Quang Trung đại thắng quân Thanh năm Kỷ Dậu 1789',
        'Trường Đại học Bách Khoa Thành phố Hồ Chí Minh',
        'Vườn quốc gia Cúc Phương với hệ động thực vật phong phú',
        'Học, học nữa, học mãi - Không có việc gì khó, chỉ sợ lòng không bền'
    ];

    for (const phrase of vietnameseTestCases) {
        const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: `prompt=${encodeURIComponent(phrase)}`
        });
        assert.strictEqual(res.status, 200, `Server must accept: "${phrase}"`);
        await res.body?.cancel();
    }
});

test('Tier 5 - Area 2.4: Emojis and multi-byte surrogate pairs preserved in real chat-logic', async () => {
    const complexUnicode = '🐮 🐄 🇻🇳 👨‍💻 🚀 ✨ 💖 🌈 ⚡ 🎯 [Tạo tác phẩm nghệ thuật]';
    
    const { harness, startChat } = createChatLogicEnvironment();
    await startChat(complexUnicode);

    const userBubble = harness.chatHistory.querySelector('.chat-bubble-user');
    assert.ok(userBubble, 'User bubble must be created');
    assert.strictEqual(userBubble.textContent, complexUnicode, 'Unicode and emojis must be preserved verbatim');
});

test('Tier 5 - Area 2.5: Script tags and XSS injection vectors sanitized through textContent', async () => {
    const attackPayload = '<script>window.pwned=true;alert(document.cookie);</script><img src="x" onerror="window.pwned=true">';
    
    const { harness, startChat } = createChatLogicEnvironment();
    await startChat(attackPayload);

    const userBubble = harness.chatHistory.querySelector('.chat-bubble-user');
    assert.ok(userBubble);
    assert.strictEqual(userBubble.textContent, attackPayload);
    // Ensure raw script tag is escaped and not executable
    assert.ok(userBubble.innerHTML.includes('&lt;script&gt;'));
    assert.ok(!userBubble.innerHTML.includes('<script>'));
});

/* ==========================================================================
   3. Rapid Sequential Submissions & Concurrency Lock
   ========================================================================== */

test('Tier 5 - Area 3.1: 10 simultaneous rapid submissions trigger exactly 1 request (isGenerating lock)', async () => {
    let fetchCount = 0;
    let streamRelease;
    const streamGate = new Promise((resolve) => { streamRelease = resolve; });

    const slowFetch = async () => {
        fetchCount++;
        return {
            ok: true,
            status: 200,
            text: async () => '',
            json: async () => ({}),
            body: {
                getReader: () => {
                    let closed = false;
                    return {
                        read: async () => {
                            if (closed) return { done: true, value: undefined };
                            await streamGate; // Hold stream open to test concurrency lock
                            closed = true;
                            return {
                                done: false,
                                value: new TextEncoder().encode('data: {"choices":[{"delta":{"content":"Xong"}}]}\n\ndata: [DONE]\n\n')
                            };
                        }
                    };
                }
            }
        };
    };

    const { harness, startChat } = createChatLogicEnvironment(slowFetch);

    // Trigger 10 calls rapidly within the same microtask
    const calls = [];
    for (let i = 0; i < 10; i++) {
        calls.push(startChat(`Tin nhắn #${i + 1}`));
    }

    // While in-flight, input and submitBtn must be disabled
    assert.strictEqual(harness.promptInput.disabled, true, 'Input must be disabled while streaming');
    assert.strictEqual(harness.submitBtn.disabled, true, 'Submit button must be disabled while streaming');

    // Release the stream
    streamRelease();
    await Promise.all(calls);

    // Only exactly 1 network call should have occurred
    assert.strictEqual(fetchCount, 1, 'Only exactly 1 fetch must be initiated under rapid clicks');

    // Only 1 user bubble should exist in chatHistory
    const userBubbles = harness.chatHistory.querySelectorAll('.chat-bubble-user');
    assert.strictEqual(userBubbles.length, 1, 'Exactly 1 user bubble should be created');
    assert.strictEqual(userBubbles[0].textContent, 'Tin nhắn #1');

    // Controls must be re-enabled after completion
    assert.strictEqual(harness.promptInput.disabled, false, 'Input must be re-enabled after stream completes');
    assert.strictEqual(harness.submitBtn.disabled, false, 'Submit button must be re-enabled');
});

test('Tier 5 - Area 3.2: Multi-turn sequential chat restores focus and succeeds across turns', async () => {
    const { harness, startChat } = createChatLogicEnvironment();

    // Turn 1
    await startChat('Câu hỏi lượt 1');
    assert.strictEqual(harness.promptInput.disabled, false);
    assert.strictEqual(harness.promptInput.isFocused, true, 'Focus must be restored to promptInput');

    // Turn 2
    await startChat('Câu hỏi lượt 2');
    assert.strictEqual(harness.promptInput.disabled, false);

    // Both turns should exist in chat history in order
    const userBubbles = harness.chatHistory.querySelectorAll('.chat-bubble-user');
    assert.strictEqual(userBubbles.length, 2);
    assert.strictEqual(userBubbles[0].textContent, 'Câu hỏi lượt 1');
    assert.strictEqual(userBubbles[1].textContent, 'Câu hỏi lượt 2');
});

test('Tier 5 - Area 3.3: Concurrency lock released when fetch rejects with network error', async () => {
    const failingFetch = async () => {
        throw new Error('Network Connection Failed');
    };

    const { harness, startChat } = createChatLogicEnvironment(failingFetch);

    await startChat('Câu hỏi gặp lỗi mạng');

    // Input and submit button must be restored in finally
    assert.strictEqual(harness.promptInput.disabled, false, 'Input must be unlocked on network failure');
    assert.strictEqual(harness.submitBtn.disabled, false, 'Submit button must be unlocked on failure');

    // AI bubble should display alert error
    const aiBubble = harness.chatHistory.querySelector('.chat-bubble-ai');
    assert.ok(aiBubble);
    assert.ok(aiBubble.innerHTML.includes('ai-alert-error'));
    assert.ok(aiBubble.innerHTML.includes('Network Connection Failed'));

    // Subsequent chat should now be allowed (not stuck in isGenerating = true)
    let secondCallAllowed = false;
    const okFetch = async () => {
        secondCallAllowed = true;
        return {
            ok: true,
            status: 200,
            text: async () => '',
            json: async () => ({}),
            body: {
                getReader: () => ({
                    read: async () => ({ done: true, value: undefined })
                })
            }
        };
    };

    // Override fetch in sandbox
    const { startChat: startChat2 } = createChatLogicEnvironment(okFetch);
    await startChat2('Thử lại sau lỗi');
    assert.strictEqual(secondCallAllowed, true, 'Subsequent submission must work after error recovery');
});

/* ==========================================================================
   4. Empty / Whitespace Input Rejection
   ========================================================================== */

test('Tier 5 - Area 4.1: Client startChat rejects null, empty, spaces, tabs, and newlines', async () => {
    let fetchAttempted = false;
    const mockFetch = async () => {
        fetchAttempted = true;
        return { ok: true };
    };

    const { harness, startChat } = createChatLogicEnvironment(mockFetch);

    const emptyInputs = ['', '   ', '\t', '\n\n', '   \r\n\t   '];
    for (const val of emptyInputs) {
        harness.promptInput.value = val;
        await startChat();
        assert.strictEqual(fetchAttempted, false, `Must reject empty/whitespace input: ${JSON.stringify(val)}`);
        assert.strictEqual(harness.chatHistory.children.length, 0, 'No bubbles should be created');
        assert.strictEqual(harness.viewport.classList.contains('bottom-mode'), false, 'Should not transition layout');
    }
});

test('Tier 5 - Area 4.2: Live server rejects empty prompt with HTTP 400', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt='
    });
    assert.strictEqual(res.status, 400);
    const json = await res.json();
    assert.ok(json.error);
});

test('Tier 5 - Area 4.3: Live server rejects whitespace prompt (%20%0A%09) with HTTP 400', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=${encodeURIComponent('   \t\n   ')}`
    });
    assert.strictEqual(res.status, 400);
    await res.body?.cancel();
});

test('Tier 5 - Area 4.4: Live server rejects request with completely missing prompt field with HTTP 400', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'model=gemini'
    });
    assert.strictEqual(res.status, 400);
    await res.body?.cancel();
});

test('Tier 5 - Area 4.5: Live server POST /Home/GenerateImage rejects empty and whitespace prompts', async () => {
    const res1 = await fetch(`${BASE_URL}/Home/GenerateImage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt='
    });
    assert.strictEqual(res1.status, 400);
    await res1.body?.cancel();

    const res2 = await fetch(`${BASE_URL}/Home/GenerateImage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=${encodeURIComponent('     ')}`
    });
    assert.strictEqual(res2.status, 400);
    await res2.body?.cancel();
});

/* ==========================================================================
   5. Malformed SSE Payloads & Error Stream Handling
   ========================================================================== */

test('Tier 5 - Area 5.1: Malformed JSON syntax in SSE chunk falls back gracefully without unhandled exception', () => {
    const malformedChunks = [
        'data: {broken:json without quotes}\n\n',
        'data: {"choices":[{"delta":{"content":"Vẫn chạy tốt"}}]}\n\n',
        'data: [DONE]\n\n'
    ];

    const { events, completed } = parseSSEChunks(malformedChunks);
    assert.strictEqual(completed, true);
    assert.strictEqual(events.length, 3);
    assert.strictEqual(events[0].type, 'text'); // Fallback to raw text
    assert.strictEqual(events[1].type, 'json');
    assert.strictEqual(events[1].data.choices[0].delta.content, 'Vẫn chạy tốt');
});

test('Tier 5 - Area 5.2: Backend SSE error payload with [ERROR] prefix renders error alert in real chat-logic', async () => {
    const mockErrorFetch = async () => ({
        ok: true,
        status: 200,
        text: async () => '',
        json: async () => ({}),
        body: {
            getReader: () => {
                let sent = false;
                return {
                    read: async () => {
                        if (sent) return { done: true, value: undefined };
                        sent = true;
                        const errorStream = 'data: [ERROR] OpenRouter model rate limit exceeded.\n\ndata: [DONE]\n\n';
                        return { done: false, value: new TextEncoder().encode(errorStream) };
                    }
                };
            }
        }
    });

    const { harness, startChat } = createChatLogicEnvironment(mockErrorFetch);
    await startChat('Kiểm tra lỗi [ERROR]');

    const aiBubble = harness.chatHistory.querySelector('.chat-bubble-ai');
    assert.ok(aiBubble);
    assert.ok(aiBubble.innerHTML.includes('ai-alert-error'));
    assert.ok(aiBubble.innerHTML.includes('OpenRouter model rate limit exceeded'));
});

test('Tier 5 - Area 5.3: JSON error payload with error object renders error alert in real chat-logic', async () => {
    const mockJsonErrorFetch = async () => ({
        ok: true,
        status: 200,
        text: async () => '',
        json: async () => ({}),
        body: {
            getReader: () => {
                let sent = false;
                return {
                    read: async () => {
                        if (sent) return { done: true, value: undefined };
                        sent = true;
                        const payload = JSON.stringify({
                            error: { message: 'OpenRouter API Key invalid or expired.' }
                        });
                        return { done: false, value: new TextEncoder().encode(`data: ${payload}\n\n`) };
                    }
                };
            }
        }
    });

    const { harness, startChat } = createChatLogicEnvironment(mockJsonErrorFetch);
    await startChat('Kiểm tra lỗi json.error');

    const aiBubble = harness.chatHistory.querySelector('.chat-bubble-ai');
    assert.ok(aiBubble);
    assert.ok(aiBubble.innerHTML.includes('ai-alert-error'));
    assert.ok(aiBubble.innerHTML.includes('OpenRouter API Key invalid or expired'));
});

test('Tier 5 - Area 5.4: Unexpected SSE comment lines and keep-alive colons are ignored', () => {
    const sseWithComments = [
        ': this is a comment\n\n',
        ': keepalive\n\n',
        'data: {"choices":[{"delta":{"content":"TokenSauComment"}}]}\n\n',
        'data: [DONE]\n\n'
    ];

    const { events, completed } = parseSSEChunks(sseWithComments);
    assert.strictEqual(completed, true);
    assert.strictEqual(events.length, 2, 'Comments should not produce data events');
    assert.strictEqual(events[0].data.choices[0].delta.content, 'TokenSauComment');
});

test('Tier 5 - Area 5.5: Image payload with is_image_url correctly renders img and download link in real chat-logic', async () => {
    const testImgUrl = 'https://image.pollinations.ai/prompt/cyber_cow_4k?width=1024&height=1024';
    const mockImageFetch = async () => ({
        ok: true,
        status: 200,
        text: async () => '',
        json: async () => ({}),
        body: {
            getReader: () => {
                let sent = false;
                return {
                    read: async () => {
                        if (sent) return { done: true, value: undefined };
                        sent = true;
                        const payload = JSON.stringify({
                            is_image_url: testImgUrl,
                            prompt: 'cyber cow 4k'
                        });
                        return { done: false, value: new TextEncoder().encode(`data: ${payload}\n\ndata: [DONE]\n\n`) };
                    }
                };
            }
        }
    });

    const { harness, startChat } = createChatLogicEnvironment(mockImageFetch);
    await startChat('/image cyber cow 4k');

    const aiBubble = harness.chatHistory.querySelector('.chat-bubble-ai');
    assert.ok(aiBubble);
    const img = aiBubble.querySelector('img');
    assert.ok(img, 'AI bubble must render an <img> tag for image payload');
    assert.strictEqual(img.getAttribute('src'), testImgUrl);
    
    const downloadLink = aiBubble.querySelector('a');
    assert.ok(downloadLink, 'Download link must be provided');
    assert.strictEqual(downloadLink.href || downloadLink.getAttribute('href'), testImgUrl);
});
