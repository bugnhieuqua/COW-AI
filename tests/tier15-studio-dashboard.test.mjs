import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');

test('Tier 15 - Probe 1: Project & Scene entities and DbContext define cascade relationships', () => {
    const projectModel = fs.readFileSync(path.join(PROJECT_ROOT, 'Models', 'Project.cs'), 'utf8');
    assert.ok(projectModel.includes('public class Project'), 'Must define Project model');
    assert.ok(projectModel.includes('public List<Scene> Scenes'), 'Project must have Scenes collection');
    assert.ok(projectModel.includes('public string Status'), 'Project must track Status');

    const sceneModel = fs.readFileSync(path.join(PROJECT_ROOT, 'Models', 'Scene.cs'), 'utf8');
    assert.ok(sceneModel.includes('public class Scene'), 'Must define Scene model');
    assert.ok(sceneModel.includes('public int OrderIndex'), 'Scene must track OrderIndex');
    assert.ok(sceneModel.includes('public double DurationSeconds'), 'Scene must track DurationSeconds');

    const dbContext = fs.readFileSync(path.join(PROJECT_ROOT, 'Data', 'AppDbContext.cs'), 'utf8');
    assert.ok(dbContext.includes('public DbSet<Project> Projects'), 'DbContext must expose Projects DbSet');
    assert.ok(dbContext.includes('public DbSet<Scene> Scenes'), 'DbContext must expose Scenes DbSet');
    assert.ok(dbContext.includes('OnDelete(DeleteBehavior.Cascade)'), 'Must configure cascade delete on Scenes');
});

test('Tier 15 - Probe 2: Prompts/StudioScriptGen.txt and StudioService implement 3-in-1 pipeline', () => {
    const promptPath = path.join(PROJECT_ROOT, 'Prompts', 'StudioScriptGen.txt');
    assert.ok(fs.existsSync(promptPath), 'Prompts/StudioScriptGen.txt must exist');
    const promptContent = fs.readFileSync(promptPath, 'utf8');
    assert.ok(promptContent.includes('VALID JSON ARRAY'), 'Prompt must instruct AI to output valid JSON array');
    assert.ok(promptContent.includes('imagePrompt'), 'Prompt must specify imagePrompt');

    const studioService = fs.readFileSync(path.join(PROJECT_ROOT, 'Services', 'StudioService.cs'), 'utf8');
    assert.ok(studioService.includes('GenerateScriptAsync'), 'Must implement GenerateScriptAsync');
    assert.ok(studioService.includes('GenerateSceneAudioAsync'), 'Must implement GenerateSceneAudioAsync');
    assert.ok(studioService.includes('GenerateSceneImageAsync'), 'Must implement GenerateSceneImageAsync');
    assert.ok(studioService.includes('AutoProduceProjectAsync'), 'Must implement AutoProduceProjectAsync');
});

test('Tier 15 - Probe 3: StudioController exposes SSR routes and REST APIs', () => {
    const controller = fs.readFileSync(path.join(PROJECT_ROOT, 'Controllers', 'StudioController.cs'), 'utf8');
    assert.ok(controller.includes('[HttpGet("/studio/{id:int?}")]'), 'Must expose GET /studio route');
    assert.ok(controller.includes('[HttpGet("/studio/about")]'), 'Must expose GET /studio/about route');
    assert.ok(controller.includes('[HttpPost("/api/studio/projects")]'), 'Must expose POST /api/studio/projects');
    assert.ok(controller.includes('[HttpPost("/api/studio/projects/{id:int}/script")]'), 'Must expose script gen API');
    assert.ok(controller.includes('[HttpPost("/api/studio/projects/{id:int}/auto-produce")]'), 'Must expose auto-produce API');
});

test('Tier 15 - Probe 4: SEO Standards - About page is indexable with Schema.org; Workspace is noindex', () => {
    const aboutHtml = fs.readFileSync(path.join(PROJECT_ROOT, 'Views', 'Studio', 'About.cshtml'), 'utf8');
    assert.ok(aboutHtml.includes('content="index, follow"'), 'About page must allow index and follow');
    assert.ok(aboutHtml.includes('rel="canonical"'), 'About page must define canonical URL');
    assert.ok(aboutHtml.includes('"WebApplication"'), 'About page must include WebApplication Schema.org JSON-LD');
    assert.ok(aboutHtml.includes('property="og:title"'), 'About page must include OpenGraph tags');

    const studioHtml = fs.readFileSync(path.join(PROJECT_ROOT, 'Views', 'Studio', 'Index.cshtml'), 'utf8');
    assert.ok(studioHtml.includes('content="noindex, nofollow"'), 'Studio workspace must have noindex for privacy');
});

test('Tier 15 - Probe 5: Bento Box Layout renders 3 zones (Sidebar, Workspace Grid, Timeline Track)', () => {
    const studioHtml = fs.readFileSync(path.join(PROJECT_ROOT, 'Views', 'Studio', 'Index.cshtml'), 'utf8');
    assert.ok(studioHtml.includes('class="studio-sidebar'), 'Must render studio-sidebar');
    assert.ok(studioHtml.includes('class="studio-main-workspace"'), 'Must render studio-main-workspace');
    assert.ok(studioHtml.includes('class="studio-stage-screen"'), 'Must render studio-stage-screen');
    assert.ok(studioHtml.includes('class="studio-timeline-dock'), 'Must render studio-timeline-dock');
    assert.ok(studioHtml.includes('id="scriptGenModal"'), 'Must render AI Script Modal');
});

test('Tier 15 - Probe 6: studio-player.js provides reactive stage preview and scene synchronization', () => {
    const playerJs = fs.readFileSync(path.join(PROJECT_ROOT, 'wwwroot', 'js', 'modules', 'studio-player.js'), 'utf8');
    assert.ok(playerJs.includes('function scanScenes'), 'Must implement scanScenes');
    assert.ok(playerJs.includes('function loadScene'), 'Must implement loadScene');
    assert.ok(playerJs.includes('function togglePlay'), 'Must implement togglePlay');
    assert.ok(playerJs.includes('audioElement.addEventListener'), 'Must bind audio events');
    assert.ok(playerJs.includes('btnAutoProduceAll'), 'Must handle 3-in-1 auto produce');
});

test('Tier 15 - Probe 7: components.css styles Studio Bento with zero AI-slop', () => {
    const css = fs.readFileSync(path.join(PROJECT_ROOT, 'wwwroot', 'css', 'components.css'), 'utf8');
    assert.ok(css.includes('.studio-app-container'), 'Must style .studio-app-container');
    assert.ok(css.includes('.studio-bento-grid'), 'Must style .studio-bento-grid');
    assert.ok(css.includes('.stage-background-image'), 'Must style .stage-background-image');
    assert.ok(css.includes('.timeline-segment'), 'Must style .timeline-segment');
});
