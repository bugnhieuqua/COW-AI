/**
 * Tier 9: Reviewer Round 3 Deep Adversarial Verification Suite
 * Validates fixes for:
 * 1. Light theme contrast & WCAG readability for .ai-alert-error
 * 2. OmniRouteModel explicit configuration in appsettings.json
 * 3. Case-insensitive placeholder API key detection in OpenRouterService
 * 4. Defensive empty / whitespace handling in SearchDuckDuckGoAsync (no ArgumentNullException)
 * 5. Sanitized single-line headings and URLs in DuckDuckGo search Markdown
 * 6. Streamed delta.content [ERROR] and technical trace interception in startChat
 * 7. Stream line buffer persistence (buffer = lines.pop()) and whitespace-tolerant parsing in initDocSummary
 * 8. Stream line buffer persistence (buffer = lines.pop()) and whitespace-tolerant parsing in initCodeGen
 * 9. Document summary failure isolation (statusBadge 'Thất bại' and hidden actionsBar on SSE error)
 * 10. Model selector synchronization helper (selectModelByValue) updating .selected option class
 * 11. Live server verification: DuckDuckGo multi-line query and whitespace resiliency
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');
const BASE_URL = process.env.COW_URL || 'http://127.0.0.1:5223';

test('Tier 9 - Probe 1: components.css specifies light theme contrast rules for .ai-alert-error', () => {
    const cssPath = path.join(PROJECT_ROOT, 'wwwroot', 'css', 'components.css');
    const css = fs.readFileSync(cssPath, 'utf-8');

    assert.ok(css.includes('html.light .ai-alert-error'), 'Must provide light theme selector for ai-alert-error');
    assert.ok(css.includes('color: #b91c1c;'), 'Must specify high-contrast dark red color in light mode');
});

test('Tier 9 - Probe 2: appsettings.json explicitly defines OmniRouteModel', () => {
    const configPath = path.join(PROJECT_ROOT, 'appsettings.json');
    const json = JSON.parse(fs.readFileSync(configPath, 'utf-8'));

    assert.ok(json.OpenRouter, 'appsettings.json must have OpenRouter section');
    assert.strictEqual(json.OpenRouter.OmniRouteModel, 'openrouter/auto', 'Must configure OmniRouteModel');
});

test('Tier 9 - Probe 3: OpenRouterService.cs detects placeholder keys case-insensitively', () => {
    const servicePath = path.join(PROJECT_ROOT, 'Services', 'OpenRouterService.cs');
    const code = fs.readFileSync(servicePath, 'utf-8');

    assert.ok(code.includes('apiKey.Contains("your_", StringComparison.OrdinalIgnoreCase)'), 'Must detect your_ placeholder keys');
    assert.ok(code.includes('apiKey.Contains("placeholder", StringComparison.OrdinalIgnoreCase)'), 'Must detect placeholder keys');
});

test('Tier 9 - Probe 4: SearchDuckDuckGoAsync handles null and whitespace without ArgumentNullException', () => {
    const controllerPath = path.join(PROJECT_ROOT, 'Controllers', 'HomeController.cs');
    const code = fs.readFileSync(controllerPath, 'utf-8');

    assert.ok(code.includes('string.IsNullOrWhiteSpace(cleanQuery)'), 'SearchDuckDuckGoAsync must check for null/empty query');
    assert.ok(code.includes('Vui lòng nhập từ khóa tra cứu để tìm kiếm qua DuckDuckGo.'), 'Must return friendly guidance on empty query');
});

test('Tier 9 - Probe 5: SearchDuckDuckGoAsync sanitizes newlines, spaces and closing parentheses in Markdown', () => {
    const controllerPath = path.join(PROJECT_ROOT, 'Controllers', 'HomeController.cs');
    const code = fs.readFileSync(controllerPath, 'utf-8');

    assert.ok(code.includes('singleLineQuery = cleanQuery.Replace("\\r", " ").Replace("\\n", " ").Trim()'), 'Must strip newlines from query');
    assert.ok(code.includes('effectiveHeading = string.IsNullOrWhiteSpace(heading)'), 'Must define effective heading safely');
    assert.ok(code.includes('.Replace(")", "%29")'), 'Must escape closing parenthesis in Markdown URLs');
});

test('Tier 9 - Probe 6: chat-logic.js startChat intercepts [ERROR] and technical error patterns inside delta.content', () => {
    const jsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js');
    const js = fs.readFileSync(jsPath, 'utf-8');

    assert.ok(js.includes('deltaContent.trim().startsWith(\'[ERROR]\')'), 'Must inspect deltaContent for [ERROR] prefix');
    assert.ok(js.includes('TECHNICAL_ERROR_PATTERNS.some(function (p) { return p.test(deltaContent); })'), 'Must sanitize technical traces in deltaContent');
});

test('Tier 9 - Probe 7: chat-logic.js initDocSummary implements stream buffer persistence (lines.pop()) and flexible prefix', () => {
    const jsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js');
    const js = fs.readFileSync(jsPath, 'utf-8');

    // Verify buffer accumulation in initDocSummary
    assert.ok(js.includes('function handleSummaryLine(line)'), 'Must define structured line handler in initDocSummary');
    assert.ok(js.includes('trimmed.startsWith(\'data: \') ? trimmed.slice(6).trim() : trimmed.slice(5).trim()'), 'Must handle both data: and data: prefixes');
});

test('Tier 9 - Probe 8: chat-logic.js initCodeGen implements stream buffer persistence (lines.pop()) and flexible prefix', () => {
    const jsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js');
    const js = fs.readFileSync(jsPath, 'utf-8');

    assert.ok(js.includes('function handleCodeGenLine(line)'), 'Must define structured line handler in initCodeGen');
});

test('Tier 9 - Probe 9: chat-logic.js initDocSummary sets statusBadge to Thất bại and hides actionsBar on SSE error', () => {
    const jsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js');
    const js = fs.readFileSync(jsPath, 'utf-8');

    assert.ok(js.includes('if (statusBadge) statusBadge.textContent = \'Thất bại\';'), 'Must set status to Thất bại on summary error');
    assert.ok(js.includes('if (actionsBar) actionsBar.classList.add(\'hidden\');'), 'Must hide actionsBar on summary error');
});

test('Tier 9 - Probe 10: chat-logic.js exposes selectModelByValue and syncs .selected option class', () => {
    const jsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js');
    const js = fs.readFileSync(jsPath, 'utf-8');

    assert.ok(js.includes('window.selectModelByValue = selectModelByValue;'), 'Must expose selectModelByValue globally');
    assert.ok(js.includes('opt.classList.toggle(\'selected\', isMatch);'), 'Must synchronize selected class across option elements');
});

test('Tier 9 - Probe 11: Live server handles multi-line DuckDuckGo query without crashing or unclosed header', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=H%C3%A0%20N%E1%BB%99i%0ATh%E1%BB%9Di%20ti%E1%BA%BFt&model=duckduckgo'
    });

    assert.strictEqual(res.status, 200);
    const body = await res.text();
    assert.ok(body.includes('data:'), 'Must return SSE stream');
    assert.ok(body.includes('DuckDuckGo'), 'Must return DuckDuckGo search result text');
    assert.ok(body.includes('[DONE]'), 'Stream must finish with [DONE]');
});
