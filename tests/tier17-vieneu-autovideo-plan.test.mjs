/**
 * Tier 17: VieNeu-TTS, Auto-Video Pipeline & Markdown Plan Approval Gate
 * Verification for COW.AI Multimedia Studio & HyperFrames Integration
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

test('Tier 17 - Probe 1: Models Project.cs and Scene.cs define Auto-Video and VieNeu properties', () => {
    const projectCsPath = path.join(PROJECT_ROOT, 'Models', 'Project.cs');
    const projectContent = fs.readFileSync(projectCsPath, 'utf-8');

    assert.ok(projectContent.includes('public string VideoType { get; set; } = "comparison";'), 'Project must define VideoType with comparison default');
    assert.ok(projectContent.includes('public string? AvatarsJson { get; set; }'), 'Project must define AvatarsJson');
    assert.ok(projectContent.includes('public string? PlanMarkdown { get; set; }'), 'Project must define PlanMarkdown');
    assert.ok(projectContent.includes('public string? SourceFileName { get; set; }'), 'Project must define SourceFileName');
    assert.ok(projectContent.includes('public string? SourceContent { get; set; }'), 'Project must define SourceContent');

    const sceneCsPath = path.join(PROJECT_ROOT, 'Models', 'Scene.cs');
    const sceneContent = fs.readFileSync(sceneCsPath, 'utf-8');

    assert.ok(sceneContent.includes('SceneType'), 'Scene must define SceneType');
    assert.ok(sceneContent.includes('AvatarPosition'), 'Scene must define AvatarPosition');
    assert.ok(sceneContent.includes('Speaker'), 'Scene must define Speaker');
});

test('Tier 17 - Probe 2: VieNeu-TTS Service implements presets and duration probe', () => {
    const iVieNeuPath = path.join(PROJECT_ROOT, 'Services', 'IVieNeuTtsService.cs');
    const iVieNeuContent = fs.readFileSync(iVieNeuPath, 'utf-8');

    assert.ok(iVieNeuContent.includes('GenerateAudioAsync'), 'IVieNeuTtsService must declare GenerateAudioAsync');
    assert.ok(iVieNeuContent.includes('GetAvailableVoices'), 'IVieNeuTtsService must declare GetAvailableVoices');

    const vieNeuPath = path.join(PROJECT_ROOT, 'Services', 'VieNeuTtsService.cs');
    const vieNeuContent = fs.readFileSync(vieNeuPath, 'utf-8');

    assert.ok(vieNeuContent.includes('Hải Đăng'), 'Must define Hải Đăng voice preset');
    assert.ok(vieNeuContent.includes('Lan Nhi'), 'Must define Lan Nhi voice preset');
    assert.ok(vieNeuContent.includes('Binh'), 'Must define Binh voice preset');
    assert.ok(vieNeuContent.includes('auto-compare-video'), 'Must bridge to auto-compare-video repo');
});

test('Tier 17 - Probe 3: AutoVideoService implements Plan Generation, Parsing and 3-Zone Composition', () => {
    const autoVideoServicePath = path.join(PROJECT_ROOT, 'Services', 'AutoVideoService.cs');
    const content = fs.readFileSync(autoVideoServicePath, 'utf-8');

    assert.ok(content.includes('GenerateMarkdownPlanAsync'), 'Must implement GenerateMarkdownPlanAsync');
    assert.ok(content.includes('ParsePlanToScenesAsync'), 'Must implement ParsePlanToScenesAsync');
    assert.ok(content.includes('BuildHyperFramesCompositionAsync'), 'Must implement BuildHyperFramesCompositionAsync');
    assert.ok(content.includes('ProcessChatEditAsync'), 'Must implement ProcessChatEditAsync');
    assert.ok(content.includes('zone-cards') || content.includes('zone-comparison'), 'Must support 3-zone layout structure');
});

test('Tier 17 - Probe 4: StudioController exposes RESTful Auto-Video & Plan endpoints', () => {
    const controllerPath = path.join(PROJECT_ROOT, 'Controllers', 'StudioController.cs');
    const content = fs.readFileSync(controllerPath, 'utf-8');

    assert.ok(content.includes('[HttpGet("/api/studio/voices")]'), 'Must expose GET /api/studio/voices');
    assert.ok(content.includes('[HttpPost("/api/studio/projects/{id:int}/upload-source")]'), 'Must expose POST /api/studio/projects/{id}/upload-source');
    assert.ok(content.includes('[HttpPost("/api/studio/projects/{id:int}/generate-plan")]'), 'Must expose POST /api/studio/projects/{id}/generate-plan');
    assert.ok(content.includes('[HttpPost("/api/studio/projects/{id:int}/apply-plan")]'), 'Must expose POST /api/studio/projects/{id}/apply-plan');
    assert.ok(content.includes('[HttpPost("/api/studio/projects/{id:int}/chat-edit")]'), 'Must expose POST /api/studio/projects/{id}/chat-edit');
});

test('Tier 17 - Probe 5: Views/Studio/Index.cshtml contains Auto-Video Plan Modal, Intake, and Proceed action', () => {
    const indexPath = path.join(PROJECT_ROOT, 'Views', 'Studio', 'Index.cshtml');
    const content = fs.readFileSync(indexPath, 'utf-8');

    assert.ok(content.includes('btnOpenPlanModal'), 'Must include btnOpenPlanModal trigger button');
    assert.ok(content.includes('studioPlanModal'), 'Must define studioPlanModal modal container');
    assert.ok(content.includes('fileDropzone'), 'Must include fileDropzone for PDF/DOCX/TXT/MD');
    assert.ok(content.includes('avatarDropzone'), 'Must include avatarDropzone for 0-3 images');
    assert.ok(content.includes('btnTypeComparison'), 'Must support Comparison 2-column video type');
    assert.ok(content.includes('btnTypeSequential'), 'Must support Sequential narrative video type');
    assert.ok(content.includes('planVoiceSelect'), 'Must include VieNeu voice selection');
    assert.ok(content.includes('btnProceedPlan'), 'Must include Proceed / Chạy ngay button');
    assert.ok(content.includes('studioChatPanel'), 'Must include studioChatPanel for conversational edits');
});

test('Tier 17 - Probe 6: studio-player.js handles intake upload, plan generation, and proceed execution', () => {
    const playerJsPath = path.join(PROJECT_ROOT, 'wwwroot', 'js', 'modules', 'studio-player.js');
    const content = fs.readFileSync(playerJsPath, 'utf-8');

    assert.ok(content.includes('handleSourceFileUpload'), 'Must implement handleSourceFileUpload');
    assert.ok(content.includes('renderAvatarPreviewStrip'), 'Must implement renderAvatarPreviewStrip');
    assert.ok(content.includes('/api/studio/projects/${projectId}/generate-plan'), 'Must call generate-plan API');
    assert.ok(content.includes('/api/studio/projects/${projectId}/apply-plan'), 'Must call apply-plan API');
    assert.ok(content.includes('renderMarkdownCards'), 'Must render review cards from markdown');
    assert.ok(content.includes('studioChatForm'), 'Must wire conversational chat form');
});
