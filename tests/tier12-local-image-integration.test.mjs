import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');

test('Tier 12 - Probe 1: appsettings.json contains LocalImage configuration block', () => {
    const configPath = path.join(PROJECT_ROOT, 'appsettings.json');
    const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));

    assert.ok(config.LocalImage, 'appsettings.json must have LocalImage block');
    assert.strictEqual(config.LocalImage.BaseUrl, 'http://127.0.0.1:7860');
    assert.ok(config.LocalImage.ExecutablePath.includes('sd-server.exe'));
    assert.ok(config.LocalImage.ModelPath.includes('sd_xl_turbo_1.0_fp16.safetensors'));
    assert.strictEqual(config.LocalImage.Steps, 4);
    assert.strictEqual(config.LocalImage.CfgScale, 1);
});

test('Tier 12 - Probe 2: IntentDispatcherService detects /imags and local-image model', () => {
    const filePath = path.join(PROJECT_ROOT, 'Services', 'IntentDispatcherService.cs');
    const code = fs.readFileSync(filePath, 'utf8');

    assert.ok(code.includes('public bool DetectLocalImageIntent'), 'Must define DetectLocalImageIntent');
    assert.ok(code.includes('"/imags"'), 'Must support /imags slash command');
    assert.ok(code.includes('"local-image"'), 'Must support local-image model key');
});

test('Tier 12 - Probe 3: OllamaService implements UnloadActiveModelsAsync to free VRAM', () => {
    const filePath = path.join(PROJECT_ROOT, 'Services', 'OllamaService.cs');
    const code = fs.readFileSync(filePath, 'utf8');

    assert.ok(code.includes('public async Task UnloadActiveModelsAsync'), 'Must implement UnloadActiveModelsAsync');
    assert.ok(code.includes('keep_alive = 0'), 'Must send keep_alive = 0 to release GPU VRAM');
});

test('Tier 12 - Probe 4: LocalImageService manages sd-server and ephemeral RAM cache', () => {
    const filePath = path.join(PROJECT_ROOT, 'Services', 'LocalImageService.cs');
    const code = fs.readFileSync(filePath, 'utf8');

    assert.ok(code.includes('public async Task<(string ImageUrl, int Width, int Height)> GenerateImageAsync'));
    assert.ok(code.includes('/v1/images/generations'), 'Must target OpenAI-compatible sd-server endpoint');
    assert.ok(code.includes('UnloadActiveModelsAsync'), 'Must unload Ollama VRAM before generating');
    assert.ok(code.includes('/Image/LocalOutput/'), 'Must serve ephemeral RAM images via /Image/LocalOutput/');
});

test('Tier 12 - Probe 5: HomeController & ImageController wire LocalImageService cleanly', () => {
    const homeCode = fs.readFileSync(path.join(PROJECT_ROOT, 'Controllers', 'HomeController.cs'), 'utf8');
    const imgCode = fs.readFileSync(path.join(PROJECT_ROOT, 'Controllers', 'ImageController.cs'), 'utf8');

    assert.ok(homeCode.includes('LocalImageService localImage'), 'HomeController must inject LocalImageService');
    assert.ok(homeCode.includes('DetectLocalImageIntent'), 'HomeController must dispatch /imags branch');
    assert.ok(imgCode.includes('public IActionResult LocalOutput(string id)'), 'ImageController must serve LocalOutput');
    assert.ok(imgCode.includes('_localImage.GenerateImageAsync'), '/Image must render through LocalImageService');
    assert.ok(!imgCode.includes('_pollinations'), '/Image must not call Pollinations anymore');
    assert.ok(imgCode.includes('LocalStatus'), '/Image must expose LocalStatus for the engine badge');
});

test('Tier 12 - Probe 7: /Image page shows a local engine badge instead of a fake quota', () => {
    const html = fs.readFileSync(path.join(PROJECT_ROOT, 'Views', 'Image', 'Index.cshtml'), 'utf8');

    assert.ok(html.includes('id="imageEngineBadge"'), 'Must render the local engine badge');
    assert.ok(html.includes('ai-badge--local'), 'Badge must use the local variant');
    assert.ok(!html.includes('lượt hôm nay'), 'Fake 5/5 quota copy must be gone');
    assert.ok(html.includes('class="ai-btn-primary"'), 'Submit button is a plain primary button now');
});

test('Tier 12 - Probe 8: no page-wide shimmer overlay can come back', () => {
    const pagesCss = fs.readFileSync(path.join(PROJECT_ROOT, 'wwwroot', 'css', 'pages.css'), 'utf8');
    const componentsCss = fs.readFileSync(path.join(PROJECT_ROOT, 'wwwroot', 'css', 'components.css'), 'utf8');

    const primaryBlock = pagesCss.match(/\.ai-btn-primary\s*\{[^}]*\}/);
    assert.ok(/position:\s*relative/.test(primaryBlock[0]), '.ai-btn-primary must be a containing block');
    assert.ok(/overflow:\s*hidden/.test(primaryBlock[0]), '.ai-btn-primary must clip decorations');

    assert.ok(!componentsCss.includes('.shimmer-btn::after'), 'Page-wide shimmer pseudo must stay removed');

    const views = ['Image/Index.cshtml', 'Document/Summary.cshtml', 'Document/CodeGen.cshtml', 'Home/Index.cshtml'];
    for (const rel of views) {
        const viewHtml = fs.readFileSync(path.join(PROJECT_ROOT, 'Views', rel), 'utf8');
        assert.ok(!viewHtml.includes('shimmer-btn'), `${rel} must not use shimmer-btn`);
    }
});

test('Tier 12 - Probe 6: _ModelSelector.cshtml exposes Local Image (SDXL Turbo) option', () => {
    const html = fs.readFileSync(path.join(PROJECT_ROOT, 'Views', 'Shared', '_ModelSelector.cshtml'), 'utf8');

    assert.ok(html.includes('data-value="local-image"'), 'Must include local-image model option');
    assert.ok(html.includes('Local Image (SDXL Turbo)'), 'Must label Local Image (SDXL Turbo)');
    assert.ok(html.includes('/imags'), 'Must hint /imags slash command');
});

