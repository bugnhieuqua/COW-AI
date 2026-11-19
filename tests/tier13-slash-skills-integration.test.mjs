import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');

test('Tier 13 - Probe 1: Prompts/Skills.json defines /imags, /code and extended skills cleanly', () => {
    const skillsPath = path.join(PROJECT_ROOT, 'Prompts', 'Skills.json');
    assert.ok(fs.existsSync(skillsPath), 'Prompts/Skills.json must exist');
    const skills = JSON.parse(fs.readFileSync(skillsPath, 'utf8'));
    assert.ok(Array.isArray(skills) && skills.length >= 5, 'Must contain featured + extended skills');

    const cmds = skills.map(s => s.command);
    assert.ok(cmds.includes('/imags'), 'Must include /imags skill');
    assert.ok(cmds.includes('/code'), 'Must include /code skill');
    assert.ok(cmds.includes('/image'), 'Must include /image skill');
    assert.ok(cmds.includes('/search'), 'Must include /search skill');
    assert.ok(cmds.includes('/tts'), 'Must include /tts skill');
});

test('Tier 13 - Probe 2: HomeController exposes GET /api/skills and handles /code and /search', () => {
    const code = fs.readFileSync(path.join(PROJECT_ROOT, 'Controllers', 'HomeController.cs'), 'utf8');
    assert.ok(code.includes('[HttpGet("/api/skills")]'), 'Must expose GET /api/skills');
    assert.ok(code.includes('DetectCodeSkillIntent'), 'Must dispatch /code skill');
    assert.ok(code.includes('GetCodeGenPrompt'), 'Must wrap /code with Prompts/CodeGeneration.txt');
    assert.ok(code.includes('DetectSearchSkillIntent'), 'Must dispatch /search skill');
});

test('Tier 13 - Probe 3: Views/Home/Index.cshtml mounts #skillsPopover and #skillsTriggerBtn', () => {
    const html = fs.readFileSync(path.join(PROJECT_ROOT, 'Views', 'Home', 'Index.cshtml'), 'utf8');
    assert.ok(html.includes('id="skillsPopover"'), 'Must render #skillsPopover container');
    assert.ok(html.includes('id="skillsTriggerBtn"'), 'Must render #skillsTriggerBtn button');
    assert.ok(html.includes('aria-autocomplete="list"'), 'Prompt input must expose aria-autocomplete');
});

test('Tier 13 - Probe 4: chat-logic.js implements Slash Skills autocomplete and keyboard navigation', () => {
    const js = fs.readFileSync(path.join(PROJECT_ROOT, 'wwwroot', 'js', 'chat-logic.js'), 'utf8');
    assert.ok(js.includes("fetch('/api/skills')"), 'Must fetch skills dynamically from /api/skills');
    assert.ok(js.includes('function renderSkillsPopover'), 'Must implement renderSkillsPopover');
    assert.ok(js.includes('function selectSkill'), 'Must implement selectSkill');
    assert.ok(js.includes("event.key === 'ArrowDown'"), 'Must support ArrowDown keyboard navigation');
    assert.ok(js.includes("event.key === 'Tab'"), 'Must support Tab selection');
    assert.ok(js.includes('ttsSlashMatch'), 'Must support /tts slash skill routing to audio player');
});

test('Tier 13 - Probe 5: components.css styles .skills-popover with focus-visible and light mode support', () => {
    const css = fs.readFileSync(path.join(PROJECT_ROOT, 'wwwroot', 'css', 'components.css'), 'utf8');
    assert.ok(css.includes('.skills-popover'), 'Must style .skills-popover');
    assert.ok(css.includes('.skills-popover__item:focus-visible'), 'Must include focus-visible state');
    assert.ok(css.includes('html.light .skills-popover'), 'Must support light theme contrast');
});

