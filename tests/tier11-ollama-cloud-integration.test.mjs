import { test } from 'node:test';
import assert from 'node:assert';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');

test('Tier 11 - Probe 1: _ModelSelector.cshtml contains Ollama Cloud section header and free badge', () => {
    const viewPath = path.join(PROJECT_ROOT, 'Views', 'Shared', '_ModelSelector.cshtml');
    assert.ok(fs.existsSync(viewPath), '_ModelSelector.cshtml must exist');
    const content = fs.readFileSync(viewPath, 'utf8');

    assert.ok(content.includes('model-menu__header--ollama'), 'Must contain model-menu__header--ollama class');
    assert.ok(content.includes('model-badge-free'), 'Must contain model-badge-free badge');
    assert.ok(content.includes('Ollama Cloud (Miễn phí)'), 'Must contain Vietnamese Ollama Cloud section title');
});

test('Tier 11 - Probe 2: _ModelSelector.cshtml contains all 4 Ollama free models with accessible options', () => {
    const viewPath = path.join(PROJECT_ROOT, 'Views', 'Shared', '_ModelSelector.cshtml');
    const content = fs.readFileSync(viewPath, 'utf8');

    assert.ok(content.includes('data-value="gpt20-cloud"'), 'Must contain gpt20-cloud option');
    assert.ok(content.includes('GPT-OSS 20B Cloud'), 'Must contain GPT-OSS 20B Cloud label');
    assert.ok(content.includes('data-value="120b-cloud"'), 'Must contain 120b-cloud option');
    assert.ok(content.includes('GPT-OSS 120B Cloud'), 'Must contain GPT-OSS 120B Cloud label');
    assert.ok(content.includes('data-value="mistral-nemo"'), 'Must contain mistral-nemo option');
    assert.ok(content.includes('Mistral NeMo 12B'), 'Must contain Mistral NeMo 12B label');
    assert.ok(content.includes('data-value="phi4-cloud"'), 'Must contain phi4-cloud option');
    assert.ok(content.includes('Phi-4 14B Cloud'), 'Must contain Phi-4 14B Cloud label');
    assert.ok(content.includes('data-category="ollama"'), 'Must categorize options under ollama category');
});

test('Tier 11 - Probe 3: components.css defines .model-menu__header--ollama, .model-badge-free, and focus-visible', () => {
    const cssPath = path.join(PROJECT_ROOT, 'wwwroot', 'css', 'components.css');
    assert.ok(fs.existsSync(cssPath), 'components.css must exist');
    const css = fs.readFileSync(cssPath, 'utf8');

    assert.ok(css.includes('.model-menu__header--ollama'), 'Must style .model-menu__header--ollama');
    assert.ok(css.includes('.model-badge-free'), 'Must style .model-badge-free badge');
    assert.ok(css.includes('.model-option:focus-visible'), 'Must define keyboard focus-visible state');
});

test('Tier 11 - Probe 4: appsettings.json contains Ollama configuration block with models mapping', () => {
    const configPath = path.join(PROJECT_ROOT, 'appsettings.json');
    assert.ok(fs.existsSync(configPath), 'appsettings.json must exist');
    const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));

    assert.ok(config.Ollama, 'appsettings.json must have Ollama block');
    assert.strictEqual(config.Ollama.BaseUrl, 'https://ollama.com/v1');
    assert.strictEqual(config.Ollama.DefaultModel, 'gpt-oss:20b');
    assert.ok(config.Ollama.Models['gpt20-cloud'], 'Must map gpt20-cloud');
    assert.ok(config.Ollama.Models['120b-cloud'], 'Must map 120b-cloud');
    assert.ok(config.Ollama.Models['mistral-nemo'], 'Must map mistral-nemo');
    assert.ok(config.Ollama.Models['phi4-cloud'], 'Must map phi4-cloud');
});

test('Tier 11 - Probe 5: Services/OllamaService.cs implements streaming and OpenAI-compatible completions', () => {
    const servicePath = path.join(PROJECT_ROOT, 'Services', 'OllamaService.cs');
    assert.ok(fs.existsSync(servicePath), 'OllamaService.cs must exist');
    const code = fs.readFileSync(servicePath, 'utf8');

    assert.ok(code.includes('public async Task<Stream> StreamChatAsync'), 'Must implement StreamChatAsync');
    assert.ok(code.includes('public async Task<string> ChatAsync'), 'Must implement ChatAsync');
    assert.ok(code.includes('public string ResolveModel'), 'Must implement ResolveModel');
    assert.ok(code.includes('public static bool IsOllamaModel'), 'Must implement static IsOllamaModel');
    assert.ok(code.includes('gpt-oss:20b'), 'Must reference gpt-oss:20b');
    assert.ok(code.includes('gpt-oss:120b'), 'Must reference gpt-oss:120b');
});

test('Tier 11 - Probe 6: Controllers/HomeController.cs injects OllamaService and handles Ollama streaming branch', () => {
    const controllerPath = path.join(PROJECT_ROOT, 'Controllers', 'HomeController.cs');
    assert.ok(fs.existsSync(controllerPath), 'HomeController.cs must exist');
    const code = fs.readFileSync(controllerPath, 'utf8');

    assert.ok(code.includes('OllamaService ollama'), 'Constructor must inject OllamaService');
    assert.ok(code.includes('OllamaService.IsOllamaModel(normalizedModel)'), 'Must check IsOllamaModel');
    assert.ok(code.includes('_ollama.StreamChatAsync'), 'Must call _ollama.StreamChatAsync');
});

test('Tier 11 - Probe 7: Program.cs registers OllamaService in dependency injection', () => {
    const programPath = path.join(PROJECT_ROOT, 'Program.cs');
    assert.ok(fs.existsSync(programPath), 'Program.cs must exist');
    const code = fs.readFileSync(programPath, 'utf8');

    assert.ok(code.includes('builder.Services.AddHttpClient<OllamaService>'), 'Program.cs must register OllamaService HttpClient');
});

test('Tier 11 - Probe 8: .env.example documents Ollama environment variables', () => {
    const envPath = path.join(PROJECT_ROOT, '.env.example');
    assert.ok(fs.existsSync(envPath), '.env.example must exist');
    const content = fs.readFileSync(envPath, 'utf8');

    assert.ok(content.includes('Ollama__ApiKey='), 'Must document Ollama__ApiKey');
    assert.ok(content.includes('Ollama__BaseUrl='), 'Must document Ollama__BaseUrl');
    assert.ok(content.includes('Ollama__DefaultModel='), 'Must document Ollama__DefaultModel');
});

test('Tier 11 - Probe 9: Program.cs loads Key.env and OllamaService supports OLLAMA key format', () => {
    const programPath = path.join(PROJECT_ROOT, 'Program.cs');
    const programCode = fs.readFileSync(programPath, 'utf8');
    assert.ok(programCode.includes('Key.env'), 'Program.cs must check for Key.env');
    assert.ok(programCode.includes('Ollama:ApiKey'), 'Program.cs must map OLLAMA to Ollama:ApiKey');

    const servicePath = path.join(PROJECT_ROOT, 'Services', 'OllamaService.cs');
    const serviceCode = fs.readFileSync(servicePath, 'utf8');
    assert.ok(serviceCode.includes('OLLAMA'), 'OllamaService must check OLLAMA environment variable');
});

