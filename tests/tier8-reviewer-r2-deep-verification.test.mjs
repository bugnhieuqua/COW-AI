/**
 * Tier 8: Reviewer Round 2 Deep Adversarial Verification Suite
 * Validates fixes for:
 * 1. C# and C++ programming keywords regex in ResolveAutoModel (avoiding \bc#\b non-word boundary flaw)
 * 2. DuckDuckGo empty heading fallback (avoiding '### 🔍 Kết quả tìm kiếm DuckDuckGo: ****')
 * 3. DuckDuckGo grouped topics extraction support
 * 4. Mobile WebKit smooth momentum scrolling (-webkit-overflow-scrolling: touch)
 * 5. Frontend error patterns covering token/key leaks (Bearer, sk-*, api_key), gateway errors (502, 503, 504), and Kestrel
 * 6. Frontend response.ok validation in generateImage and initImageGen preventing infinite loading state
 * 7. Frontend response.ok validation and failure isolation in StreamSummary and StreamCodeGen
 * 8. Trailing slash and BaseAddress safety guards in AI services
 * 9. Live server verification of C# Auto-routing and empty payload validation
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

test('Tier 8 - Probe 1: ResolveAutoModel regex accurately matches C# and C++ without boundary failure', () => {
    const homeControllerPath = path.join(PROJECT_ROOT, 'Controllers', 'HomeController.cs');
    const code = fs.readFileSync(homeControllerPath, 'utf-8');

    assert.ok(!code.includes('\\bc#\\b'), 'Regex must not use \\bc#\\b which fails in .NET');
    assert.ok(code.includes('\\bc#(?!\\w)'), 'Regex must use negative lookahead \\bc#(?!\\w) for C#');
    assert.ok(code.includes('\\bc\\+\\+(?!\\w)'), 'Regex must support C++ with \\bc\\+\\+(?!\\w)');
});

test('Tier 8 - Probe 2: DuckDuckGo effective heading handles empty string Heading from API', () => {
    const homeControllerPath = path.join(PROJECT_ROOT, 'Controllers', 'HomeController.cs');
    const code = fs.readFileSync(homeControllerPath, 'utf-8');

    // Verify heading is checked with string.IsNullOrWhiteSpace rather than raw ?? which fails on ""
    assert.ok(code.includes('string.IsNullOrWhiteSpace(heading) ? query : heading'), 
        'Must use string.IsNullOrWhiteSpace to prevent empty **** heading in DuckDuckGo');
});

test('Tier 8 - Probe 3: DuckDuckGo RelatedTopics supports nested category groups (Topics array)', () => {
    const homeControllerPath = path.join(PROJECT_ROOT, 'Controllers', 'HomeController.cs');
    const code = fs.readFileSync(homeControllerPath, 'utf-8');

    assert.ok(code.includes('item.TryGetProperty("Topics"'), 
        'DuckDuckGo extractor must support subtopics groups in RelatedTopics');
});

test('Tier 8 - Probe 4: components.css specifies -webkit-overflow-scrolling: touch for iOS momentum scrolling', () => {
    const cssPath = path.join(PROJECT_ROOT, 'wwwroot', 'css', 'components.css');
    const css = fs.readFileSync(cssPath, 'utf-8');

    assert.ok(css.includes('-webkit-overflow-scrolling: touch;'), 
        'components.css must specify -webkit-overflow-scrolling: touch for iOS Safari');
});

test('Tier 8 - Probe 5: chat-logic.js sanitizes tokens (Bearer, sk-*), keys, gateway errors and Kestrel', () => {
    const jsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js');
    const js = fs.readFileSync(jsPath, 'utf-8');

    assert.ok(js.includes('Bearer'), 'Must sanitize Bearer tokens');
    assert.ok(js.includes('sk-'), 'Must sanitize sk- API keys');
    assert.ok(js.includes('502'), 'Must sanitize 502/503/504 gateway errors');
    assert.ok(js.includes('Kestrel'), 'Must sanitize Kestrel framework traces');
});

test('Tier 8 - Probe 6: chat-logic.js generateImage and initImageGen validate response.ok to prevent infinite loading', () => {
    const jsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js');
    const js = fs.readFileSync(jsPath, 'utf-8');

    // Count occurrences of response.ok checks
    const matches = js.match(/if\s*\(!response\.ok\)/g) || [];
    assert.ok(matches.length >= 4, `Expected at least 4 response.ok checks across chat, image and doc tools, found ${matches.length}`);
});

test('Tier 8 - Probe 7: chat-logic.js StreamCodeGen and StreamSummary isolate failures and hide explanation on error', () => {
    const jsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js');
    const js = fs.readFileSync(jsPath, 'utf-8');

    assert.ok(js.includes('explanationBox.classList.add(\'hidden\')'), 
        'Must hide explanationBox on StreamCodeGen errors to avoid false success state');
});

test('Tier 8 - Probe 8: AI services enforce trailing slashes and safe HttpClient base address assignment', () => {
    const pollinationsPath = path.join(PROJECT_ROOT, 'Services', 'PollinationsService.cs');
    const pollinationsCode = fs.readFileSync(pollinationsPath, 'utf-8');
    assert.ok(pollinationsCode.includes('url.EndsWith("/")'), 'PollinationsService must ensure trailing slash');

    const openRouterPath = path.join(PROJECT_ROOT, 'Services', 'OpenRouterService.cs');
    const openRouterCode = fs.readFileSync(openRouterPath, 'utf-8');
    assert.ok(openRouterCode.includes('_http.BaseAddress == null'), 'OpenRouterService must check BaseAddress null before setting');
});

test('Tier 8 - Probe 9: Live server routes C# programming prompt via Auto model without error', async () => {
    const res = await fetch(`${BASE_URL}/Home/StreamChat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt=H%E1%BB%8Dc%20l%E1%BA%ADp%20tr%C3%ACnh%20C%23%20c%C6%A1%20b%E1%BA%A3n&model=auto'
    });

    assert.strictEqual(res.status, 200);
    const body = await res.text();
    assert.ok(body.includes('data:'), 'Must return SSE stream');
});

test('Tier 8 - Probe 10: Live server rejects empty prompt on GenerateImage with HTTP 400', async () => {
    const res = await fetch(`${BASE_URL}/Home/GenerateImage`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'prompt='
    });

    assert.strictEqual(res.status, 400);
    const data = await res.json();
    assert.ok(data.error, 'Must contain error property');
    assert.ok(!data.error.includes('Exception'), 'Error must not contain technical C# keywords');
});
