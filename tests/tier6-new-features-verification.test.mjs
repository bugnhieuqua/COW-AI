/**
 * Tier 6: New Features & Security Error Handling Verification Test Suite
 * Tests R1 (Safe Error Handling & No Sensitive Data Leak),
 * R2 (Streamlined ModelSelector UI, Removed Descriptions, 240px/320px CSS, 7 models),
 * R3 (Backend StreamChat handling for auto, omniroute, and duckduckgo).
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');
const BASE_URL = process.env.COW_URL || 'http://127.0.0.1:5223';

// =============================================================================
// AREA 1: ModelSelector HTML & CSS Verification (R2)
// =============================================================================

test('Tier 6 - Area 1.1: Live page contains all 7 model selector options', async () => {
    const res = await fetch(`${BASE_URL}/`);
    assert.strictEqual(res.status, 200);
    const html = await res.text();

    const expectedModels = [
        'data-value="auto"',
        'data-value="gemini"',
        'data-value="llama3"',
        'data-value="deepseek"',
        'data-value="pollinations"',
        'data-value="omniroute"',
        'data-value="duckduckgo"'
    ];

    for (const val of expectedModels) {
        assert.ok(html.includes(val), `Page must contain model option ${val}`);
    }

    assert.ok(html.includes('Auto (Tự động)'), 'Must include Auto option text');
    assert.ok(html.includes('Gemini 2.0 Flash'), 'Must include Gemini option text');
    assert.ok(html.includes('Llama 3 8B'), 'Must include Llama 3 8B option text');
    assert.ok(html.includes('DeepSeek R1'), 'Must include DeepSeek R1 option text');
    assert.ok(html.includes('Pollinations AI'), 'Must include Pollinations AI option text');
    assert.ok(html.includes('OmniRoute'), 'Must include OmniRoute option text');
    assert.ok(html.includes('DuckDuckGo Search'), 'Must include DuckDuckGo Search option text');
});

test('Tier 6 - Area 1.2: _ModelSelector.cshtml has no long description tags (model-option__desc)', () => {
    const selectorPath = path.join(PROJECT_ROOT, 'Views', 'Shared', '_ModelSelector.cshtml');
    const content = readFileSync(selectorPath, 'utf-8');

    assert.ok(!content.includes('model-option__desc'), '_ModelSelector must not contain model-option__desc tags');
    assert.ok(!content.includes('Tốc độ siêu nhanh'), 'Old descriptions must be removed');
});

test('Tier 6 - Area 1.3: components.css enforces compact 240px width, 320px max-height, and 6px scrollbar', () => {
    const cssPath = path.join(PROJECT_ROOT, 'wwwroot', 'css', 'components.css');
    const css = readFileSync(cssPath, 'utf-8');

    assert.ok(css.includes('width: 240px;'), 'CSS specifies width: 240px for .model-menu');
    assert.ok(css.includes('max-height: 320px;'), 'CSS specifies max-height: 320px for .model-menu');
    assert.ok(css.includes('overflow-y: auto;'), 'CSS specifies overflow-y: auto for .model-menu');
    assert.ok(css.includes('.model-menu::-webkit-scrollbar'), 'CSS defines webkit scrollbar for .model-menu');
    assert.ok(css.includes('width: 6px;'), 'CSS specifies thin 6px scrollbar');
    assert.ok(css.includes('padding: 8px 10px;'), 'CSS optimizes .model-option padding');
    assert.ok(css.includes('font-size: 13px;'), 'CSS optimizes .model-option font size to 13px');
});

// =============================================================================
// AREA 2: Backend Routing for New Models (R3)
// =============================================================================

test('Tier 6 - Area 2.1: POST /Home/StreamChat with model=auto routes without exception', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Viết%20hàm%20tính%20giai%20thừa%20trong%20C%23&model=auto'
    });

    assert.strictEqual(res.status, 200, 'StreamChat must accept auto model');
    assert.ok(res.headers.get('content-type')?.includes('text/event-stream'));
    const body = await res.text();
    assert.ok(body.includes('data:'), 'Should emit SSE data stream');
});

test('Tier 6 - Area 2.2: POST /Home/StreamChat with model=omniroute routes to SSE stream', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Giải%20thích%20kiến%20trúc%20microservices&model=omniroute'
    });

    assert.strictEqual(res.status, 200, 'StreamChat must accept omniroute model');
    assert.ok(res.headers.get('content-type')?.includes('text/event-stream'));
    const body = await res.text();
    assert.ok(body.includes('data:'), 'Should emit SSE data stream');
});

test('Tier 6 - Area 2.3: POST /Home/StreamChat with model=duckduckgo returns web lookup results', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=ASP.NET%20Core%2010&model=duckduckgo'
    });

    assert.strictEqual(res.status, 200, 'StreamChat must accept duckduckgo model');
    assert.ok(res.headers.get('content-type')?.includes('text/event-stream'));
    const body = await res.text();
    assert.ok(body.includes('DuckDuckGo'), 'SSE stream must contain DuckDuckGo search result text');
    assert.ok(body.includes('[DONE]'), 'Stream must complete with [DONE]');
});

// =============================================================================
// AREA 3: Safe Error Handling & No Sensitive Leak (R1)
// =============================================================================

test('Tier 6 - Area 3.1: Backend error SSE hides technical C# exceptions and sensitive info', async () => {
    // When calling with text model where OpenRouter key is unset/mocked
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Test%20Exception%20Safety&model=gemini'
    });

    assert.strictEqual(res.status, 200);
    const body = await res.text();

    // Verify ABSENCE of technical keywords
    assert.ok(!body.includes('InvalidOperationException'), 'Must NOT leak InvalidOperationException');
    assert.ok(!body.includes('StackTrace'), 'Must NOT leak StackTrace');
    assert.ok(!body.includes('OpenRouter:ApiKey'), 'Must NOT leak OpenRouter:ApiKey');
    assert.ok(!body.includes('dotnet user-secrets'), 'Must NOT leak internal commands');
    assert.ok(!body.includes('at Microsoft.AspNetCore'), 'Must NOT leak C# stack traces');

    // If an error occurred in stream, it must use the friendly user-facing message
    if (body.includes('[ERROR]')) {
        assert.ok(
            body.includes('Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút.') ||
            body.includes('Không thể tạo ảnh vào lúc này.'),
            'Error message must be user-friendly'
        );
    }
});

test('Tier 6 - Area 3.2: chat-logic.js sanitizes any technical exception to friendly text', () => {
    const jsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js');
    const js = readFileSync(jsPath, 'utf-8');

    assert.ok(js.includes('TECHNICAL_ERROR_PATTERNS'), 'chat-logic.js must include TECHNICAL_ERROR_PATTERNS');
    assert.ok(js.includes('sanitizeErrorMessage'), 'chat-logic.js must include sanitizeErrorMessage');
    assert.ok(js.includes('Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút.'), 'Must have friendly fallback string');
});
