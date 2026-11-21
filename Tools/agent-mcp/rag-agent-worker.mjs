import crypto from 'crypto';
import fs from 'fs';
import path from 'path';
import MiniSearch from 'minisearch';
import jscpdPkg from 'jscpd';
const { detectClones } = jscpdPkg;

const IGNORED_DIRS = new Set([
  'node_modules', '.git', '.vs', '.vscode', '.idea', 'bin', 'obj', 'dist', 'build',
  '__pycache__', '.venv', 'venv', 'coverage', '.cowai_trash', '.cowai_index', '.kode'
]);

const TEXT_EXTS = new Set([
  '.cs', '.cshtml', '.razor', '.js', '.mjs', '.cjs', '.ts', '.tsx', '.jsx',
  '.py', '.java', '.go', '.rs', '.cpp', '.c', '.h', '.hpp', '.php', '.rb',
  '.json', '.md', '.txt', '.html', '.htm', '.css', '.scss', '.sql', '.xml',
  '.yaml', '.yml', '.toml', '.ini', '.csproj', '.sln', '.sh', '.ps1', '.cmd', '.bat'
]);

const MAX_FILES = 1500;
const MAX_FILE_BYTES = 350 * 1024;
const CHUNK_LINES = 80;
const CHUNK_OVERLAP = 15;

function foldVi(str) {
  return String(str || '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .toLowerCase();
}

function buildTrigrams(text) {
  const clean = foldVi(text).replace(/\s+/g, ' ').trim();
  const counts = new Map();
  if (clean.length < 3) return counts;
  for (let i = 0; i <= clean.length - 3 && i < 6000; i++) {
    const g = clean.slice(i, i + 3);
    counts.set(g, (counts.get(g) || 0) + 1);
  }
  return counts;
}

function trigramCosine(aMap, bText) {
  const bMap = buildTrigrams(bText);
  if (aMap.size === 0 || bMap.size === 0) return 0;
  let dot = 0;
  let normA = 0;
  let normB = 0;
  for (const [k, v] of aMap.entries()) {
    normA += v * v;
    const bv = bMap.get(k);
    if (bv) dot += v * bv;
  }
  for (const v of bMap.values()) normB += v * v;
  const denom = Math.sqrt(normA) * Math.sqrt(normB);
  return denom > 0 ? dot / denom : 0;
}

function walkFiles(rootDir, currentDir = rootDir, out = []) {
  if (out.length >= MAX_FILES) return out;
  let entries = [];
  try {
    entries = fs.readdirSync(currentDir, { withFileTypes: true });
  } catch {
    return out;
  }
  entries.sort((a, b) => a.name.localeCompare(b.name));
  for (const ent of entries) {
    if (out.length >= MAX_FILES) break;
    if (IGNORED_DIRS.has(ent.name)) continue;
    const full = path.join(currentDir, ent.name);
    if (ent.isDirectory()) {
      walkFiles(rootDir, full, out);
    } else if (ent.isFile()) {
      const rel = path.relative(rootDir, full).replace(/\\/g, '/');
      out.push({ rel, full });
    }
  }
  return out;
}

function extractImportsOrSymbols(text) {
  const symbols = new Set();
  const lines = text.split('\n').slice(0, 120);
  for (const line of lines) {
    const m = line.match(/\b(?:class|interface|record|enum|struct|function|def|export\s+function|export\s+class|public\s+(?:async\s+)?[\w<>\[\]]+)\s+([A-Za-z_][A-Za-z0-9_]*)/);
    if (m) symbols.add(m[1]);
  }
  return Array.from(symbols).slice(0, 20).join(' ');
}

function createMiniSearch(docs) {
  const ms = new MiniSearch({
    fields: ['path', 'symbols', 'content', 'foldedContent'],
    storeFields: ['id', 'path', 'startLine', 'endLine', 'symbols', 'content'],
    searchOptions: {
      boost: { path: 3.5, symbols: 2.5, content: 1.5, foldedContent: 1.2 },
      fuzzy: 0.2,
      prefix: true,
      combineWith: 'OR'
    }
  });
  if (docs.length > 0) ms.addAll(docs);
  return ms;
}

async function cmdIndex(workspaceDir, indexFile) {
  const absRoot = path.resolve(workspaceDir);
  const allFiles = walkFiles(absRoot);
  const chunks = [];
  const fileMetas = [];
  let idCounter = 1;

  for (const { rel, full } of allFiles) {
    let stat;
    try { stat = fs.statSync(full); } catch { continue; }
    let sha256 = '';
    try {
      const buf = fs.readFileSync(full);
      sha256 = crypto.createHash('sha256').update(buf).digest('hex');
      fileMetas.push({
        path: rel,
        size: stat.size,
        mtimeMs: stat.mtimeMs,
        sha256
      });

      const ext = path.extname(rel).toLowerCase();
      if (!TEXT_EXTS.has(ext) || stat.size > MAX_FILE_BYTES) continue;
      const text = buf.toString('utf8');
      if (text.includes('\u0000')) continue;

      const lines = text.replace(/\r\n/g, '\n').split('\n');
      const symbols = extractImportsOrSymbols(text);
      for (let start = 0; start < lines.length; start += Math.max(1, CHUNK_LINES - CHUNK_OVERLAP)) {
        const end = Math.min(lines.length, start + CHUNK_LINES);
        const slice = lines.slice(start, end).join('\n').trim();
        if (!slice) continue;
        chunks.push({
          id: idCounter++,
          path: rel,
          startLine: start + 1,
          endLine: end,
          symbols,
          content: slice.slice(0, 4500),
          foldedContent: foldVi(slice.slice(0, 4500))
        });
        if (end >= lines.length) break;
      }
    } catch {
      continue;
    }
  }

  const ms = createMiniSearch(chunks);
  const payload = {
    version: 1,
    workspaceDir: absRoot,
    indexedAt: new Date().toISOString(),
    fileCount: allFiles.length,
    textChunkCount: chunks.length,
    files: fileMetas,
    chunks,
    miniSearch: ms.toJSON()
  };

  fs.mkdirSync(path.dirname(indexFile), { recursive: true });
  fs.writeFileSync(indexFile, JSON.stringify(payload), 'utf8');

  process.stdout.write(JSON.stringify({
    success: true,
    workspaceDir: absRoot,
    fileCount: allFiles.length,
    chunkCount: chunks.length,
    indexFile
  }));
}

function cmdSearch(indexFile, query, topK = 8) {
  if (!fs.existsSync(indexFile)) {
    process.stdout.write(JSON.stringify({ success: false, error: 'Chưa có chỉ mục RAG.' }));
    return;
  }

  const payload = JSON.parse(fs.readFileSync(indexFile, 'utf8'));
  const ms = MiniSearch.loadJSON(JSON.stringify(payload.miniSearch), {
    fields: ['path', 'symbols', 'content', 'foldedContent'],
    storeFields: ['id', 'path', 'startLine', 'endLine', 'symbols', 'content']
  });

  const foldedQuery = foldVi(query);
  const queryTrigrams = buildTrigrams(foldedQuery);
  const bm25Hits = ms.search(query, { combineWith: 'OR', fuzzy: 0.2, prefix: true });
  const foldedHits = foldedQuery && foldedQuery !== query.toLowerCase()
    ? ms.search(foldedQuery, { combineWith: 'OR', fuzzy: 0.2, prefix: true })
    : [];

  const scoreMap = new Map();
  const chunkById = new Map();
  for (const c of payload.chunks || []) chunkById.set(c.id, c);

  for (const h of [...bm25Hits.slice(0, 40), ...foldedHits.slice(0, 30)]) {
    const chunk = chunkById.get(h.id);
    if (!chunk) continue;
    const prev = scoreMap.get(h.id) || 0;
    const sem = trigramCosine(queryTrigrams, `${chunk.path} ${chunk.symbols} ${chunk.content}`);
    scoreMap.set(h.id, Math.max(prev, h.score) + sem * 4.0);
  }

  // Fallback semantic scan if BM25 returned too few hits (NLP paraphrase queries)
  if (scoreMap.size < topK) {
    for (const chunk of (payload.chunks || []).slice(0, 600)) {
      if (scoreMap.has(chunk.id)) continue;
      const sem = trigramCosine(queryTrigrams, `${chunk.path} ${chunk.symbols} ${chunk.content}`);
      if (sem > 0.12) scoreMap.set(chunk.id, sem * 5.0);
    }
  }

  const ranked = Array.from(scoreMap.entries())
    .sort((a, b) => b[1] - a[1])
    .slice(0, topK)
    .map(([id, score]) => {
      const c = chunkById.get(id);
      return {
        path: c.path,
        startLine: c.startLine,
        endLine: c.endLine,
        symbols: c.symbols,
        score: Number(score.toFixed(3)),
        snippet: c.content
      };
    });

  process.stdout.write(JSON.stringify({
    success: true,
    query,
    totalFiles: payload.fileCount,
    hits: ranked
  }));
}

function chooseCanonicalFile(group) {
  const copyPenalty = (p) => {
    const base = path.basename(p).toLowerCase();
    let score = p.length;
    if (/(copy|bak|backup|old|dup|\(\d+\)|_v\d+| - copy)/i.test(base)) score += 200;
    score += p.split('/').length * 5;
    return score;
  };
  return [...group].sort((a, b) => {
    const diff = copyPenalty(a.path) - copyPenalty(b.path);
    return diff !== 0 ? diff : a.mtimeMs - b.mtimeMs;
  })[0];
}

async function cmdDuplicates(workspaceDir) {
  const absRoot = path.resolve(workspaceDir);
  const allFiles = walkFiles(absRoot);
  const hashGroups = new Map();

  for (const { rel, full } of allFiles) {
    try {
      const stat = fs.statSync(full);
      if (stat.size === 0 || stat.size > 10 * 1024 * 1024) continue;
      const hash = crypto.createHash('sha256').update(fs.readFileSync(full)).digest('hex');
      if (!hashGroups.has(hash)) hashGroups.set(hash, []);
      hashGroups.get(hash).push({ path: rel, size: stat.size, mtimeMs: stat.mtimeMs, sha256: hash });
    } catch { }
  }

  const exactDuplicateGroups = [];
  for (const [hash, items] of hashGroups.entries()) {
    if (items.length < 2) continue;
    const keep = chooseCanonicalFile(items);
    const duplicates = items.filter(x => x.path !== keep.path).map(x => x.path);
    exactDuplicateGroups.push({
      sha256: hash,
      sizeBytes: keep.size,
      keepPath: keep.path,
      duplicatePaths: duplicates,
      allPaths: items.map(x => x.path)
    });
  }

  // Run jscpd for structural near-duplicate / copy-paste block detection
  let codeClones = [];
  try {
    const clones = await detectClones({
      path: [absRoot],
      ignore: ['**/node_modules/**', '**/.git/**', '**/bin/**', '**/obj/**', '**/dist/**', '**/.cowai_trash/**', '**/.kode/**'],
      silent: true,
      minLines: 8,
      minTokens: 40
    });
    codeClones = (clones || []).slice(0, 40).map(cl => ({
      format: cl.format,
      lines: (cl.duplicationA?.end?.line || 0) - (cl.duplicationA?.start?.line || 0) + 1,
      fileA: path.relative(absRoot, cl.duplicationA?.sourceId || '').replace(/\\/g, '/'),
      startLineA: cl.duplicationA?.start?.line || 0,
      endLineA: cl.duplicationA?.end?.line || 0,
      fileB: path.relative(absRoot, cl.duplicationB?.sourceId || '').replace(/\\/g, '/'),
      startLineB: cl.duplicationB?.start?.line || 0,
      endLineB: cl.duplicationB?.end?.line || 0
    }));
  } catch { }

  process.stdout.write(JSON.stringify({
    success: true,
    workspaceDir: absRoot,
    totalFilesScanned: allFiles.length,
    exactDuplicateGroups,
    codeClones
  }));
}

async function main() {
  const [, , cmd, ...args] = process.argv;
  if (cmd === 'index') {
    await cmdIndex(args[0], args[1]);
  } else if (cmd === 'search') {
    cmdSearch(args[0], args[1] || '', parseInt(args[2] || '8', 10));
  } else if (cmd === 'duplicates') {
    await cmdDuplicates(args[0]);
  } else {
    process.stdout.write(JSON.stringify({ success: false, error: `Unknown command: ${cmd}` }));
    process.exitCode = 1;
  }
}

main().catch(err => {
  process.stdout.write(JSON.stringify({ success: false, error: err?.message || String(err) }));
  process.exitCode = 1;
});
