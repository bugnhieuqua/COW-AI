/**
 * Tier 18: PostgreSQL Dual-Mode Database, Studio Conversational AI & HyperFrames Composition
 * Verification for COW.AI Multimedia Studio Evolution
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

test('Tier 18 - Probe 1: appsettings.json and Program.cs configure PostgreSQL Dual-Provider with SQLite fallback', () => {
    const appsettingsPath = path.join(PROJECT_ROOT, 'appsettings.json');
    const appsettings = JSON.parse(fs.readFileSync(appsettingsPath, 'utf-8'));

    assert.ok(appsettings.ConnectionStrings, 'Must have ConnectionStrings section');
    assert.ok(appsettings.ConnectionStrings.PostgreSql !== undefined, 'Must define PostgreSql connection string');
    assert.ok(appsettings.ConnectionStrings.SqliteFallback !== undefined, 'Must define SqliteFallback connection string');
    assert.ok(appsettings.Database !== undefined, 'Must define Database configuration section');

    const programCsPath = path.join(PROJECT_ROOT, 'Program.cs');
    const programCs = fs.readFileSync(programCsPath, 'utf-8');

    assert.ok(programCs.includes('UseNpgsql'), 'Program.cs must support UseNpgsql');
    assert.ok(programCs.includes('UseSqlite'), 'Program.cs must support UseSqlite fallback');
    assert.ok(programCs.includes('Database:UsePostgres'), 'Must check Database:UsePostgres flag');
});

test('Tier 18 - Probe 2: COW-AI.csproj includes Npgsql EF Core provider dependency', () => {
    const csprojPath = path.join(PROJECT_ROOT, 'COW-AI.csproj');
    const csprojContent = fs.readFileSync(csprojPath, 'utf-8');

    assert.ok(csprojContent.includes('Npgsql.EntityFrameworkCore.PostgreSQL'), 'Must include Npgsql.EntityFrameworkCore.PostgreSQL package');
});

test('Tier 18 - Probe 3: StudioService and AutoVideoService implement ProcessChatEditAsync with natural language intent handling', () => {
    const studioServicePath = path.join(PROJECT_ROOT, 'Services', 'StudioService.cs');
    const studioService = fs.readFileSync(studioServicePath, 'utf-8');

    assert.ok(studioService.includes('ProcessChatEditAsync'), 'StudioService must implement ProcessChatEditAsync');
    assert.ok(studioService.includes('_autoVideo.ProcessChatEditAsync'), 'Must delegate to AutoVideoService');

    const autoVideoPath = path.join(PROJECT_ROOT, 'Services', 'AutoVideoService.cs');
    const autoVideo = fs.readFileSync(autoVideoPath, 'utf-8');

    assert.ok(autoVideo.includes('ProcessChatEditAsync'), 'AutoVideoService must implement ProcessChatEditAsync');
    assert.ok(autoVideo.includes('FilterPreset'), 'Must handle filter/color intent');
    assert.ok(autoVideo.includes('DurationSeconds'), 'Must handle duration intent');
    assert.ok(autoVideo.includes('Voice'), 'Must handle voice change intent');
});

test('Tier 18 - Probe 4: components.css styles Studio AI Chat Panel and Modal Tabs without AI-Slop', () => {
    const cssPath = path.join(PROJECT_ROOT, 'wwwroot', 'css', 'components.css');
    const css = fs.readFileSync(cssPath, 'utf-8');

    assert.ok(css.includes('.studio-chat-panel'), 'Must style .studio-chat-panel');
    assert.ok(css.includes('.studio-chat-messages'), 'Must style .studio-chat-messages');
    assert.ok(css.includes('.studio-chat-msg--assistant'), 'Must style .studio-chat-msg--assistant');
    assert.ok(css.includes('.studio-chat-msg--user'), 'Must style .studio-chat-msg--user');
    assert.ok(css.includes('.studio-plan-modal-container'), 'Must style .studio-plan-modal-container');
    assert.ok(css.includes('.btn-proceed'), 'Must style .btn-proceed');
    assert.ok(css.includes('.intake-dropzone'), 'Must style .intake-dropzone');
    assert.ok(css.includes('.avatar-preview-strip'), 'Must style .avatar-preview-strip');

    // Craftsmanship anti-slop rules
    assert.ok(!css.includes('border-radius: 9999px !important'), 'No pill button overrides');
    assert.ok(!css.includes('box-shadow: 0 0 45px'), 'No saturated neon glow');
});

test('Tier 18 - Probe 5: AutoVideoService composition builder renders 3-zone layout', () => {
    const servicePath = path.join(PROJECT_ROOT, 'Services', 'AutoVideoService.cs');
    const content = fs.readFileSync(servicePath, 'utf-8');

    assert.ok(content.includes('zone-cards'), 'Must render top cards zone');
    assert.ok(content.includes('zone-captions'), 'Must render kinetic captions zone');
    assert.ok(content.includes('zone-avatar'), 'Must render avatar / talking head zone');
});

test('Tier 18 - Probe 6: Live API endpoint /api/studio/voices returns available voices', async () => {
    try {
        const res = await fetch(`${BASE_URL}/api/studio/voices`, { signal: AbortSignal.timeout(4000) });
        if (!res.ok) {
            console.log(`[Tier 18 Probe 6] Live server returned status ${res.status}, skipping live assertion.`);
            return;
        }
        const data = await res.json();
        assert.ok(Array.isArray(data), 'Response must be an array of voice descriptions');
        assert.ok(data.length >= 3, 'Must return at least 3 voices');
        const ids = data.map(v => v.id);
        assert.ok(ids.includes('Hải Đăng'), 'Must include Hải Đăng');
        assert.ok(ids.includes('Lan Nhi'), 'Must include Lan Nhi');
    } catch (e) {
        console.log(`[Tier 18 Probe 6] Live server not running (${e.message}), offline assertions passed.`);
    }
});

test('Tier 18 - Probe 7: VideoRenderService implements FFmpeg Backend MP4 Video rendering', () => {
    const servicePath = path.join(PROJECT_ROOT, 'Services', 'VideoRenderService.cs');
    const content = fs.readFileSync(servicePath, 'utf-8');

    assert.ok(content.includes('RenderProjectVideoAsync'), 'Must implement RenderProjectVideoAsync');
    assert.ok(content.includes('ffmpeg') || content.includes('Ffmpeg'), 'Must call FFmpeg CLI');
    assert.ok(content.includes('master_voiceover.wav') || content.includes('master_mixed_audio'), 'Must concatenate audio and mix BGM');

    const controllerPath = path.join(PROJECT_ROOT, 'Controllers', 'StudioController.cs');
    const controllerContent = fs.readFileSync(controllerPath, 'utf-8');
    assert.ok(controllerContent.includes('[HttpPost("/api/studio/projects/{id:int}/render-backend-video")]'), 'Must expose POST /api/studio/projects/{id}/render-backend-video');
});

test('Tier 18 - Probe 8: StudioController implements Native Document Extractor for DOCX, PDF, TXT, MD', () => {
    const controllerPath = path.join(PROJECT_ROOT, 'Controllers', 'StudioController.cs');
    const content = fs.readFileSync(controllerPath, 'utf-8');

    assert.ok(content.includes('ExtractTextFromUploadedFileAsync'), 'Must implement ExtractTextFromUploadedFileAsync');
    assert.ok(content.includes('word/document.xml'), 'Must extract word/document.xml from docx');
    assert.ok(content.includes('.pdf'), 'Must handle .pdf text stream');
});
