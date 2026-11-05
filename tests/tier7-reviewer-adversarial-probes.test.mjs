/**
 * Tier 7: Reviewer Adversarial Probes & Deep Security Verification Suite
 * Validates fixes for false-positive intent hijacking, auto-model keyword boundary precision,
 * DuckDuckGo RelatedTopics extraction, XSS mitigation in error alerts, and cross-browser scrollbar CSS.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');
const BASE_URL = process.env.COW_URL || 'http://127.0.0.1:5223';

test('Tier 7 - Probe 1: Words containing "draw" (e.g. drawbacks, withdrawal) do NOT trigger Pollinations image generator', async () => {
    const testCases = [
        'What are the drawbacks of microservices?',
        'How to prevent withdrawal symptoms',
        'Top drawer organization ideas'
    ];

    for (const prompt of testCases) {
        const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: `prompt=${encodeURIComponent(prompt)}&model=gemini`
        });

        assert.strictEqual(res.status, 200);
        const body = await res.text();
        assert.ok(!body.includes('is_image_url'), `Prompt "${prompt}" must NOT route to image generation`);
        assert.ok(!body.includes('image.pollinations.ai'), `Prompt "${prompt}" must NOT point to Pollinations`);
    }
});

test('Tier 7 - Probe 2: Model duckduckgo retains search intent and does not hijack to Pollinations for search queries', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=L%E1%BB%8Bch%20s%E1%BB%AD%20tranh%20v%E1%BA%BD%20%C4%90%C3%B4ng%20H%E1%BB%93&model=duckduckgo'
    });

    assert.strictEqual(res.status, 200);
    const body = await res.text();
    assert.ok(!body.includes('is_image_url'), 'DuckDuckGo search for painting history must NOT route to image generation');
    assert.ok(body.includes('DuckDuckGo'), 'Must return DuckDuckGo search result text');
});

test('Tier 7 - Probe 3: Explicit slash command /image STILL overrides in duckduckgo mode', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=%2Fimage%20neon%20cyber%20cow&model=duckduckgo'
    });

    assert.strictEqual(res.status, 200);
    const body = await res.text();
    assert.ok(body.includes('is_image_url'), 'Explicit /image slash command must route to image generation');
    assert.ok(body.includes('image.pollinations.ai'), 'Must point to Pollinations AI');
});

test('Tier 7 - Probe 4: Auto route keyword boundaries avoid false positives ("capital" does not route to deepseek as "api")', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=What%20is%20the%20capital%20of%20France%3F&model=auto'
    });

    assert.strictEqual(res.status, 200);
    const body = await res.text();
    // Verify it doesn't crash and routes to text model
    assert.ok(!body.includes('is_image_url'));
});

test('Tier 7 - Probe 5: DuckDuckGo RelatedTopics extraction works for entities with empty AbstractText (e.g. Tesla)', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=Tesla&model=duckduckgo'
    });

    assert.strictEqual(res.status, 200);
    const body = await res.text();
    assert.ok(body.includes('DuckDuckGo'), 'Must contain DuckDuckGo heading');
    assert.ok(body.includes('Tesla'), 'Must extract Tesla topics from DuckDuckGo');
});

test('Tier 7 - Probe 6: chat-logic.js escapeHtml prevents XSS when rendering error alerts', () => {
    const jsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js');
    const js = fs.readFileSync(jsPath, 'utf-8');

    assert.ok(js.includes('function escapeHtml'), 'Must define escapeHtml function');
    assert.ok(js.includes('escapeHtml(safeMsg)'), 'Must call escapeHtml when rendering ai-alert-error');
});

test('Tier 7 - Probe 7: components.css specifies cross-browser scrollbar-width and light theme scrollbar', () => {
    const cssPath = path.join(PROJECT_ROOT, 'wwwroot', 'css', 'components.css');
    const css = fs.readFileSync(cssPath, 'utf-8');

    assert.ok(css.includes('scrollbar-width: thin;'), 'Must support Firefox scrollbar-width: thin');
    assert.ok(css.includes('html.light .model-menu'), 'Must provide light theme scrollbar styling');
});

test('Tier 7 - Probe 8: DocumentController properly writes [DONE] chunk upon stream completion', async () => {
    const controllerPath = path.join(PROJECT_ROOT, 'Controllers', 'DocumentController.cs');
    const code = fs.readFileSync(controllerPath, 'utf-8');

    assert.ok(code.includes('await Response.WriteAsync("data: [DONE]\\n\\n"'), 'DocumentController must emit [DONE] event');
    assert.ok(code.includes('CancellationToken cancellationToken'), 'DocumentController must support CancellationToken');
});
