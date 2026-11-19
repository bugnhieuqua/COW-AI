/**
 * Tier 16: HyperFrames Studio Advanced, Scene Property Panel & MediaRecorder Video Export
 * Verification for COW.AI Multimedia Studio Evolution (Plan/Studio.md)
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

test('Tier 16 - Probe 1: Models/Scene.cs defines AnimationEffect, FilterPreset, and SubtitlePosition', () => {
    const sceneCsPath = path.join(PROJECT_ROOT, 'Models', 'Scene.cs');
    const content = fs.readFileSync(sceneCsPath, 'utf-8');

    assert.ok(content.includes('public string AnimationEffect { get; set; } = "ken-burns";'), 'Must have AnimationEffect with ken-burns default');
    assert.ok(content.includes('public string FilterPreset { get; set; } = "none";'), 'Must have FilterPreset with none default');
    assert.ok(content.includes('public string SubtitlePosition { get; set; } = "bottom";'), 'Must have SubtitlePosition with bottom default');
});

test('Tier 16 - Probe 2: StudioService implements UpdateScenePropertiesAsync, AddSceneAsync, and GenerateCompositionAsync with GSAP', () => {
    const servicePath = path.join(PROJECT_ROOT, 'Services', 'StudioService.cs');
    const content = fs.readFileSync(servicePath, 'utf-8');

    assert.ok(content.includes('UpdateScenePropertiesAsync'), 'Must implement UpdateScenePropertiesAsync');
    assert.ok(content.includes('AddSceneAsync'), 'Must implement AddSceneAsync');
    assert.ok(content.includes('DeleteSceneAsync'), 'Must implement DeleteSceneAsync');
    assert.ok(content.includes('GenerateCompositionAsync'), 'Must implement GenerateCompositionAsync');
    assert.ok(content.includes('gsap.min.js'), 'Composition must load GSAP library');
    assert.ok(content.includes('hyperframes-stage'), 'Composition must render hyperframes-stage');
});

test('Tier 16 - Probe 3: StudioController exposes RESTful scene mutation and preview composition routes', () => {
    const controllerPath = path.join(PROJECT_ROOT, 'Controllers', 'StudioController.cs');
    const content = fs.readFileSync(controllerPath, 'utf-8');

    assert.ok(content.includes('[HttpPut("/api/studio/scenes/{id:int}")]'), 'Must expose PUT /api/studio/scenes/{id}');
    assert.ok(content.includes('[HttpPost("/api/studio/projects/{id:int}/scenes")]'), 'Must expose POST /api/studio/projects/{id}/scenes');
    assert.ok(content.includes('[HttpDelete("/api/studio/scenes/{id:int}")]'), 'Must expose DELETE /api/studio/scenes/{id}');
    assert.ok(content.includes('[HttpGet("/api/studio/projects/{id:int}/composition")]'), 'Must expose GET /api/studio/projects/{id}/composition');
    assert.ok(content.includes('[HttpGet("/studio/{id:int}/preview")]'), 'Must expose GET /studio/{id}/preview');
});

test('Tier 16 - Probe 4: Views/Studio/Index.cshtml contains Scene Property Panel, Export Video, and Preview Modal', () => {
    const indexPath = path.join(PROJECT_ROOT, 'Views', 'Studio', 'Index.cshtml');
    const content = fs.readFileSync(indexPath, 'utf-8');

    assert.ok(content.includes('btnExportVideo'), 'Must include Export Video button');
    assert.ok(content.includes('btnOpenPreview'), 'Must include Open Preview button');
    assert.ok(content.includes('scene-props-details'), 'Must include Scene Property Accordion');
    assert.ok(content.includes('animationEffect'), 'Must support animationEffect select');
    assert.ok(content.includes('filterPreset'), 'Must support filterPreset select');
    assert.ok(content.includes('subtitlePosition'), 'Must support subtitlePosition select');
    assert.ok(content.includes('recordProgressOverlay'), 'Must include recording progress overlay');
    assert.ok(content.includes('compositionPreviewModal'), 'Must include composition preview modal');
});

test('Tier 16 - Probe 5: studio-player.js implements Client-Side Video Exporter (MediaRecorder) and scene properties saving', () => {
    const playerJsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'modules', 'studio-player.js');
    const content = fs.readFileSync(playerJsPath, 'utf-8');

    assert.ok(content.includes('exportVideoClientSide'), 'Must implement exportVideoClientSide function');
    assert.ok(content.includes('new MediaRecorder'), 'Must instantiate MediaRecorder for offline video generation');
    assert.ok(content.includes('canvas.captureStream'), 'Must capture 60 FPS video stream from Canvas');
    assert.ok(content.includes('saveSceneData'), 'Must implement saveSceneData for scene properties');
    assert.ok(content.includes('btnQuickAddScene'), 'Must handle adding new scene');
    assert.ok(content.includes('compositionPreviewModal'), 'Must wire HyperFrames composition preview modal');
});

test('Tier 16 - Probe 6: components.css styles Scene Property Panel, Filter Presets, and Record Overlay without AI-slop', () => {
    const cssPath = path.join(PROJECT_ROOT, 'wwwroot', 'css', 'components.css');
    const css = fs.readFileSync(cssPath, 'utf-8');

    assert.ok(css.includes('.scene-props-details'), 'Must style .scene-props-details');
    assert.ok(css.includes('.filter-cinematic'), 'Must define .filter-cinematic');
    assert.ok(css.includes('.filter-noir'), 'Must define .filter-noir');
    assert.ok(css.includes('.stage-record-overlay'), 'Must style .stage-record-overlay');
    assert.ok(css.includes('.btn-studio-action--accent'), 'Must style .btn-studio-action--accent');

    // Anti-slop checks
    assert.ok(!css.includes('border-radius: 9999px !important'), 'No pill button overrides');
    assert.ok(!css.includes('box-shadow: 0 0 50px'), 'No oversaturated neon shadows');
});
