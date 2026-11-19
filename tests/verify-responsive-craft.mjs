import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

const root = process.cwd();
const read = (rel) => fs.readFileSync(path.join(root, rel), 'utf8');

const siteCss = read('wwwroot/css/site.css');
const pagesCss = read('wwwroot/css/pages.css');
const componentsCss = read('wwwroot/css/components.css');
const layoutHtml = read('Views/Shared/_Layout.cshtml');
const studioLayoutHtml = read('Views/Shared/_StudioLayout.cshtml');
const studioIndexHtml = read('Views/Studio/Index.cshtml');

// 1. 4-Layer Atmospheric Backdrop (Silk Mesh + Curved Horizon Arc + Vignette + Cursor Spotlight)
assert.match(siteCss, /--cow-silk-primary:/, 'site.css must define --cow-silk-primary');
assert.match(siteCss, /--cow-vignette-center:/, 'site.css must define --cow-vignette-center');
assert.match(siteCss, /--cow-cursor-glow:/, 'site.css must define --cow-cursor-glow');
assert.match(siteCss, /--cow-card-highlight:/, 'site.css must define --cow-card-highlight');
assert.match(siteCss, /body:not\(\.studio-standalone-body\)::before/, 'Atmospheric backdrop must exclude Studio');
assert.match(siteCss, /Q720 42 1480 280/, 'Curved Horizon Light Arc SVG must exist in site.css');
assert.match(siteCss, /feTurbulence/, 'Anti-banding film grain SVG filter must exist in site.css');
assert.doesNotMatch(siteCss, /--cow-grid-line:/, 'Square graph-paper grid must stay removed');
assert.match(siteCss, /::selection/, 'Browser selection styling must be themed');
assert.match(siteCss, /caret-color:/, 'Input caret color must be themed');

// 2. Unified Responsive Breakpoints & Mobile Safety
for (const bp of ['max-width: 1024px', 'max-width: 768px', 'max-width: 480px']) {
  assert.ok(pagesCss.includes(bp), `pages.css missing breakpoint: ${bp}`);
  assert.ok(componentsCss.includes(bp), `components.css missing breakpoint: ${bp}`);
}
assert.ok(pagesCss.includes('env(safe-area-inset-bottom)'), 'pages.css must respect mobile safe-area inset');
assert.ok(componentsCss.includes('env(safe-area-inset-bottom)'), 'components.css must respect mobile safe-area inset');
assert.match(componentsCss, /min-height:\s*44px/, 'Touch targets >= 44px required on mobile');
assert.match(pagesCss, /font-size:\s*16px/, 'Mobile inputs must be >= 16px to prevent iOS Safari zoom');

// 3. Mobile Navigation Drawer (Clean SVG Icons + Theme Switcher, Zero Emojis)
assert.ok(layoutHtml.includes('viewport-fit=cover'), '_Layout.cshtml must include viewport-fit=cover');
assert.ok(layoutHtml.includes('mobile-nav-theme-group'), '_Layout.cshtml mobile drawer must include theme quick-switch');
const mobileNavSection = layoutHtml.slice(layoutHtml.indexOf('id="mobileNavMenu"'), layoutHtml.indexOf('</header>'));
assert.doesNotMatch(mobileNavSection, /[💬🎨📄💻🎬👤🚪🔑✨]/u, 'Mobile drawer must use SVG icons instead of raw emojis');

// 4. Studio Accessible Viewport & Adaptive Hub/Editor Responsive Rules
assert.ok(!studioLayoutHtml.includes('user-scalable=no'), '_StudioLayout.cshtml must not block pinch-zoom (WCAG 1.4.4)');
assert.ok(studioLayoutHtml.includes('btnHubMobileSidebarToggle'), '_StudioLayout.cshtml must wire Hub drawer toggle');
assert.ok(studioIndexHtml.includes('id="btnHubMobileSidebarToggle"'), 'Studio Hub must render mobile drawer toggle');
assert.ok(studioIndexHtml.includes('id="hubSidebarBackdrop"'), 'Studio Hub must render drawer backdrop');
assert.ok(componentsCss.includes('transform: none;'), 'Studio Hub drawer must avoid transform containing-block trap for fixed popover');
assert.ok(componentsCss.includes('order: -1;'), 'Studio Editor must place stage preview above tabs on <= 1024px');

console.log('ALL RESPONSIVE & HORIZON CRAFT CHECKS PASSED');
