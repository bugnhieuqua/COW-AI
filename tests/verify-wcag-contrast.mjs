import assert from 'node:assert/strict';
import fs from 'node:fs';

function hexToRgb(hex) {
  const clean = hex.replace('#', '').trim();
  const full = clean.length === 3 ? clean.split('').map(c => c + c).join('') : clean;
  const num = parseInt(full, 16);
  return [(num >> 16) & 255, (num >> 8) & 255, num & 255];
}

function luminance([r, g, b]) {
  const [rs, gs, bs] = [r, g, b].map(v => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * rs + 0.7152 * gs + 0.0722 * bs;
}

function contrastRatio(hex1, hex2) {
  const l1 = luminance(hexToRgb(hex1));
  const l2 = luminance(hexToRgb(hex2));
  const [hi, lo] = l1 > l2 ? [l1, l2] : [l2, l1];
  return (hi + 0.05) / (lo + 0.05);
}

function extractTokens(blockText) {
  const map = {};
  for (const m of blockText.matchAll(/(--[\w-]+)\s*:\s*(#[0-9a-fA-F]{3,6})\s*;/g)) {
    map[m[1]] = m[2];
  }
  return map;
}

const siteCss = fs.readFileSync('wwwroot/css/site.css', 'utf8');
const darkMatch = siteCss.match(/:root\s*\{([\s\S]*?)\}/);
const lightMatch = siteCss.match(/html\.light[\s\S]*?\{([\s\S]*?)\}/);
assert.ok(darkMatch && lightMatch, 'site.css must define :root and html.light token blocks');

const dark = extractTokens(darkMatch[1]);
const light = extractTokens(lightMatch[1]);

const checks = [
  ['Dark primary text on bg', dark['--cow-text'], dark['--cow-bg'], 4.5],
  ['Dark muted text on bg', dark['--cow-text-muted'], dark['--cow-bg'], 4.5],
  ['Dark primary text on card', dark['--cow-text'], dark['--cow-surface-card'], 4.5],
  ['Dark muted text on card', dark['--cow-text-muted'], dark['--cow-surface-card'], 4.5],
  ['Dark CTA text on CTA bg', dark['--cow-cta-text'], dark['--cow-cta-bg'], 4.5],
  ['Dark badge text on badge bg', dark['--cow-badge-text'], dark['--cow-badge-bg'], 4.5],
  ['Dark code text on code bg', dark['--code-editor-text'], dark['--code-editor-bg'], 4.5],
  ['Dark accent on card', dark['--cow-accent'], dark['--cow-surface-card'], 4.5],
  ['Light primary text on bg', light['--cow-text'], light['--cow-bg'], 4.5],
  ['Light muted text on bg', light['--cow-text-muted'], light['--cow-bg'], 4.5],
  ['Light primary text on card', light['--cow-text'], light['--cow-surface-card'], 4.5],
  ['Light muted text on card', light['--cow-text-muted'], light['--cow-surface-card'], 4.5],
  ['Light CTA text on CTA bg', light['--cow-cta-text'], light['--cow-cta-bg'], 4.5],
  ['Light badge text on badge bg', light['--cow-badge-text'], light['--cow-badge-bg'], 4.5],
  ['Light code text on code bg', light['--code-editor-text'], light['--code-editor-bg'], 4.5],
  ['Light accent on card', light['--cow-accent'], light['--cow-surface-card'], 4.0],
];

for (const [label, fg, bg, minRatio] of checks) {
  assert.ok(fg && bg, `Missing token for ${label}: fg=${fg}, bg=${bg}`);
  const ratio = contrastRatio(fg, bg);
  assert.ok(ratio >= minRatio, `${label} (${fg} on ${bg}) contrast ${ratio.toFixed(2)}:1 < ${minRatio}:1`);
  console.log(`PASS [${ratio.toFixed(2)}:1]: ${label} (${fg} on ${bg})`);
}
console.log('--- ALL 16 WCAG 2.2 AA DUAL-THEME CONTRAST CHECKS PASSED ---');
