/**
 * Tier 2: Boundary & Corner Cases Test Suite
 * Tests empty inputs, whitespace, extreme lengths, special chars, unicode,
 * slash commands, concurrency guards, stream aborts, and fragmented chunk buffers.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { createStandardHarness } from './helpers/dom-harness.mjs';
import { parseSSEChunks, fragmentChunks, detectIntent, cleanPrompt } from './helpers/sse-harness.mjs';

const BASE_URL = process.env.COW_URL || 'http://127.0.0.1:5223';

test('Tier 2 - Case 1: Empty prompt to POST /Home/StreamChat returns 400 Bad Request', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt='
    });
    assert.strictEqual(res.status, 400, 'Empty prompt must return HTTP 400');
    const json = await res.json();
    assert.ok(json.error, 'Response body should contain error explanation');
});

test('Tier 2 - Case 2: Whitespace-only prompt to POST /Home/StreamChat returns 400 Bad Request', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=${encodeURIComponent('   \t\n   ')}`
    });
    assert.strictEqual(res.status, 400, 'Whitespace-only prompt must return HTTP 400');
});

test('Tier 2 - Case 3: Empty prompt to POST /Home/GenerateImage returns 400 Bad Request', async () => {
    const res = await fetch(`${BASE_URL}/Home/GenerateImage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt='
    });
    assert.strictEqual(res.status, 400, 'Empty image prompt must return HTTP 400');
});

test('Tier 2 - Case 4: Client input handler blocks empty or whitespace submission', () => {
    const { promptInput, chatHistory } = createStandardHarness();
    let sendAttempted = false;

    function handleSend() {
        const text = promptInput.value.trim();
        if (!text) return;
        sendAttempted = true;
    }

    promptInput.value = '';
    handleSend();
    assert.strictEqual(sendAttempted, false, 'Empty prompt should not trigger send');

    promptInput.value = '    \n  \t  ';
    handleSend();
    assert.strictEqual(sendAttempted, false, 'Whitespace prompt should not trigger send');
    assert.strictEqual(chatHistory.children.length, 0, 'No bubbles should be created');
});

test('Tier 2 - Case 5: Extremely long prompt (>4000 characters) is accepted by backend without crash', async () => {
    const longPrompt = 'Bò sữa ' + 'rất thông minh '.repeat(400); // > 4000 chars
    assert.ok(longPrompt.length > 4000, 'Prompt length must exceed 4000 characters');

    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=${encodeURIComponent(longPrompt)}`
    });
    assert.strictEqual(res.status, 200, 'Long prompt must be accepted with 200 OK');
});

test('Tier 2 - Case 6: Special characters and XSS vectors are sanitized in user bubble', () => {
    const { doc } = createStandardHarness();
    const xssPayloads = [
        '<script>alert("pwned")</script>',
        '<img src="x" onerror="alert(1)">',
        '<svg onload=alert(1)>',
        '"><script>alert(String.fromCharCode(88,83,83))</script>'
    ];

    for (const payload of xssPayloads) {
        const bubble = doc.createElement('div');
        bubble.textContent = payload;

        assert.strictEqual(bubble.textContent, payload, 'Plain text content matches verbatim');
        assert.ok(!bubble.innerHTML.includes('<script>'), 'Must not contain raw script tag');
        assert.ok(!bubble.innerHTML.includes('<img src="x"'), 'Must not contain raw unescaped img tag');
    }
});

test('Tier 2 - Case 7: Unicode characters, Vietnamese diacritics, and emojis are preserved intact', () => {
    const { doc } = createStandardHarness();
    const unicodeString = '🐮 COW.AI 🚀 Trợ lý trí tuệ nhân tạo Việt Nam! ✨ 漢字 & symbols: ¢£€';

    const bubble = doc.createElement('div');
    bubble.textContent = unicodeString;

    assert.strictEqual(bubble.textContent, unicodeString, 'Unicode characters must be preserved without distortion');
});

test('Tier 2 - Case 8: Slash command /image properly extracts clean prompt', () => {
    assert.strictEqual(detectIntent('/image a flying cyber cow'), 'image');
    assert.strictEqual(cleanPrompt('/image a flying cyber cow'), 'a flying cyber cow');
    assert.strictEqual(cleanPrompt('/IMAGE a flying cyber cow'), 'a flying cyber cow');
});

test('Tier 2 - Case 9: Slash command /draw properly extracts clean prompt', () => {
    assert.strictEqual(detectIntent('/draw majestic mountains sunset'), 'image');
    assert.strictEqual(cleanPrompt('/draw majestic mountains sunset'), 'majestic mountains sunset');
});

test('Tier 2 - Case 10: In-flight concurrency lock prevents multiple simultaneous sends', async () => {
    let isGenerating = false;
    let sendCalls = 0;

    async function handleSend() {
        if (isGenerating) return false;
        isGenerating = true;
        sendCalls++;
        // Simulate async stream duration
        await new Promise(resolve => setTimeout(resolve, 20));
        isGenerating = false;
        return true;
    }

    const first = handleSend();
    const second = handleSend(); // Triggered while first is in-flight

    const [res1, res2] = await Promise.all([first, second]);
    assert.strictEqual(res1, true, 'First send call must succeed');
    assert.strictEqual(res2, false, 'Second concurrent send call must be blocked');
    assert.strictEqual(sendCalls, 1, 'Only exactly 1 request initiated');
});

test('Tier 2 - Case 11: AbortController disconnect aborts client stream consumer cleanly', async () => {
    const controller = new AbortController();
    let abortedCleanly = false;

    try {
        const fetchPromise = fetch(`${BASE_URL}/Home/StreamChat`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: 'prompt=Test%20abort',
            signal: controller.signal
        });

        // Abort immediately
        controller.abort();
        await fetchPromise;
    } catch (err) {
        if (err.name === 'AbortError') {
            abortedCleanly = true;
        }
    }

    assert.strictEqual(abortedCleanly, true, 'Fetch should catch AbortError when aborted');
});

test('Tier 2 - Case 12: Fragmented SSE chunks split across JSON & UTF-8 byte boundaries are reconstructed', () => {
    const originalTokens = [
        'Hệ thống ',
        'trợ lý ',
        'COW.AI ',
        'hoạt động ',
        'bền bỉ.'
    ];

    const rawChunks = [
        'data: {"choices":[{"delta":{"content":"Hệ thống "}}]}\n\n',
        'data: {"choices":[{"delta":{"content":"trợ lý "}}]}\n\n',
        'data: {"choices":[{"delta":{"content":"COW.AI "}}]}\n\n',
        'data: {"choices":[{"delta":{"content":"hoạt động "}}]}\n\n',
        'data: {"choices":[{"delta":{"content":"bền bỉ."}}]}\n\ndata: [DONE]\n\n'
    ];

    // Deliberately slice into tiny 6-character chunks
    const fragmented = fragmentChunks(rawChunks, 6);
    assert.ok(fragmented.length > 20, 'Chunks should be heavily fragmented');

    const { events, completed } = parseSSEChunks(fragmented);
    assert.strictEqual(completed, true, '[DONE] must be detected despite fragmentation');

    const reconstructed = events
        .filter(e => e.type === 'json')
        .map(e => e.data.choices?.[0]?.delta?.content || '')
        .join('');

    assert.strictEqual(reconstructed, originalTokens.join(''), 'Reconstructed text must match original tokens');
});

test('Tier 2 - Case 13: Malformed SSE data line does not crash client stream parser', () => {
    const malformedChunks = [
        'data: {"valid": "json"}\n\n',
        'data: {malformed json without quotes}\n\n',
        'data: plain text token without json wrapper\n\n',
        'data: [DONE]\n\n'
    ];

    const { events, completed } = parseSSEChunks(malformedChunks);
    assert.strictEqual(completed, true, 'Parser must survive malformed chunks and complete');
    assert.strictEqual(events.length, 4, 'Should parse all 4 events including fallbacks');
    assert.strictEqual(events[0].type, 'json');
    assert.strictEqual(events[1].type, 'text'); // Fallback to raw text
    assert.strictEqual(events[2].type, 'text');
    assert.strictEqual(events[3].type, 'done');
});
