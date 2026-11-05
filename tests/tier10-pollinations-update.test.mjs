import { test } from 'node:test';
import assert from 'node:assert';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');
const BASE_URL = process.env.COW_URL || 'http://127.0.0.1:5223';

test('Tier 10 - Probe 1: pages.css defines max-width 500px, max-height 400px, and object-fit contain for message-bubble img', () => {
    const cssPath = path.join(PROJECT_ROOT, 'wwwroot', 'css', 'pages.css');
    assert.ok(fs.existsSync(cssPath), 'pages.css must exist');
    const css = fs.readFileSync(cssPath, 'utf8');

    assert.ok(css.includes('.message-bubble img'), 'Must include .message-bubble img selector');
    assert.ok(css.includes('max-width: 500px'), 'Must constrain max-width to 500px');
    assert.ok(css.includes('max-height: 400px'), 'Must constrain max-height to 400px');
    assert.ok(css.includes('object-fit: contain'), 'Must specify object-fit: contain');
    assert.ok(css.includes('background-color: #1a1a1a'), 'Must specify dark background #1a1a1a');
});

test('Tier 10 - Probe 2: PromptService.cs implements Vietnamese regex detection and translation logic', () => {
    const servicePath = path.join(PROJECT_ROOT, 'Services', 'PromptService.cs');
    assert.ok(fs.existsSync(servicePath), 'PromptService.cs must exist');
    const content = fs.readFileSync(servicePath, 'utf8');

    assert.ok(content.includes('ContainsVietnamese'), 'Must declare ContainsVietnamese method');
    assert.ok(content.includes('TranslateToEnglishAsync'), 'Must declare TranslateToEnglishAsync method');
    assert.ok(content.includes('GetImageGenerationPromptAsync'), 'Must declare GetImageGenerationPromptAsync');
    assert.ok(content.includes('OpenRouterService'), 'Must inject OpenRouterService');
});

test('Tier 10 - Probe 3: OpenRouterService.cs exposes non-streaming ChatAsync for prompt translation', () => {
    const servicePath = path.join(PROJECT_ROOT, 'Services', 'OpenRouterService.cs');
    assert.ok(fs.existsSync(servicePath), 'OpenRouterService.cs must exist');
    const content = fs.readFileSync(servicePath, 'utf8');

    assert.ok(content.includes('public async Task<string> ChatAsync'), 'Must implement ChatAsync');
    assert.ok(content.includes('stream = false'), 'Must send stream = false');
});

test('Tier 10 - Probe 4: Live server POST /Home/GenerateImage handles Vietnamese prompt gracefully', async () => {
    const res = await fetch(`${BASE_URL}/Home/GenerateImage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=' + encodeURIComponent('tạo ảnh con chó dễ thương đang chạy trên bãi cỏ')
    });
    assert.strictEqual(res.status, 200, 'Must return HTTP 200');
    const data = await res.json();
    assert.ok(typeof data.url === 'string' && data.url.includes('image.pollinations.ai/prompt/'), 'Must return valid Pollinations URL');
    assert.ok(typeof data.enhanced_prompt === 'string' && data.enhanced_prompt.length > 0, 'Must include enhanced_prompt');
});

test('Tier 10 - Probe 5: Live server POST /Home/StreamChat with Vietnamese image prompt returns is_image_url SSE', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=' + encodeURIComponent('tạo ảnh con mèo') + '&model=pollinations'
    });
    assert.strictEqual(res.status, 200, 'Must return HTTP 200 SSE stream');
    const text = await res.text();
    assert.ok(text.includes('is_image_url'), 'SSE stream must include is_image_url');
    assert.ok(text.includes('image.pollinations.ai/prompt/'), 'Payload must include pollinations URL');
});
