import assert from 'node:assert/strict';
import zlib from 'node:zlib';

const BASE = process.env.COW_BASE_URL || 'http://127.0.0.1:5223';

// Build a minimal valid ZIP buffer (STORE method) in pure Node without external deps
function makeZip(entries) {
  const localParts = [];
  const centralParts = [];
  let offset = 0;

  for (const { name, data } of entries) {
    const nameBuf = Buffer.from(name, 'utf8');
    const contentBuf = Buffer.isBuffer(data) ? data : Buffer.from(data, 'utf8');
    const crc = crc32(contentBuf);

    const local = Buffer.alloc(30 + nameBuf.length);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0, 6);
    local.writeUInt16LE(0, 8);
    local.writeUInt16LE(0, 10);
    local.writeUInt16LE(0, 12);
    local.writeUInt32LE(crc >>> 0, 14);
    local.writeUInt32LE(contentBuf.length, 18);
    local.writeUInt32LE(contentBuf.length, 22);
    local.writeUInt16LE(nameBuf.length, 26);
    local.writeUInt16LE(0, 28);
    nameBuf.copy(local, 30);

    localParts.push(local, contentBuf);

    const central = Buffer.alloc(46 + nameBuf.length);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0, 8);
    central.writeUInt16LE(0, 10);
    central.writeUInt16LE(0, 12);
    central.writeUInt16LE(0, 14);
    central.writeUInt32LE(crc >>> 0, 16);
    central.writeUInt32LE(contentBuf.length, 20);
    central.writeUInt32LE(contentBuf.length, 24);
    central.writeUInt16LE(nameBuf.length, 28);
    central.writeUInt16LE(0, 30);
    central.writeUInt16LE(0, 32);
    central.writeUInt16LE(0, 34);
    central.writeUInt16LE(0, 36);
    central.writeUInt32LE(0, 38);
    central.writeUInt32LE(offset, 42);
    nameBuf.copy(central, 46);
    centralParts.push(central);

    offset += local.length + contentBuf.length;
  }

  const centralDir = Buffer.concat(centralParts);
  const eocd = Buffer.alloc(22);
  eocd.writeUInt32LE(0x06054b50, 0);
  eocd.writeUInt16LE(0, 4);
  eocd.writeUInt16LE(0, 6);
  eocd.writeUInt16LE(entries.length, 8);
  eocd.writeUInt16LE(entries.length, 10);
  eocd.writeUInt32LE(centralDir.length, 12);
  eocd.writeUInt32LE(offset, 16);
  eocd.writeUInt16LE(0, 20);

  return Buffer.concat([...localParts, centralDir, eocd]);
}

function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) {
    c ^= buf[i];
    for (let k = 0; k < 8; k++) c = (c >>> 1) ^ (0xedb88320 & -(c & 1));
  }
  return (c ^ 0xffffffff) >>> 0;
}

async function main() {
  // 1. Anonymous writes/deletes must be rejected with 401
  for (const [method, url, body] of [
    ['DELETE', '/api/studio/projects/1', null],
    ['POST', '/api/studio/projects/bulk-delete', JSON.stringify({ projectIds: [1] })],
    ['POST', '/api/studio/projects/1/chat-edit', JSON.stringify({ instruction: 'hack' })],
    ['POST', '/api/studio/projects/1/scenes', null]
  ]) {
    const res = await fetch(BASE + url, {
      method,
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body
    });
    assert.equal(res.status, 401, `Expected 401 for anonymous ${method} ${url}, got ${res.status}`);
  }
  console.log('PASS: Anonymous Studio writes/deletes blocked with 401');

  // 2. Login as Admin
  const loginRes = await fetch(BASE + '/api/account/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: 'AdminPassword@123' })
  });
  assert.equal(loginRes.status, 200, 'Admin login must succeed');
  const cookie = (loginRes.headers.get('set-cookie') || '').split(';')[0];
  assert.ok(cookie.includes('COWAI_Auth='), 'Auth cookie must be returned');

  // 3. Create temp project
  const createRes = await fetch(BASE + '/api/studio/projects', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Cookie: cookie },
    body: JSON.stringify({ title: 'Security & PPTX Verify', description: 'Temp test' })
  });
  assert.equal(createRes.status, 200);
  const project = await createRes.json();
  const projectId = project.id;

  try {
    // 4. Reject .svg avatar upload (XSS prevention)
    const svgForm = new FormData();
    svgForm.append('avatars', new Blob(['<svg onload="alert(1)"></svg>'], { type: 'image/svg+xml' }), 'evil.svg');
    const svgRes = await fetch(`${BASE}/api/studio/projects/${projectId}/upload-source`, {
      method: 'POST',
      headers: { Cookie: cookie },
      body: svgForm
    });
    assert.equal(svgRes.status, 400, `Expected 400 for .svg avatar upload, got ${svgRes.status}`);
    console.log('PASS: Dangerous .svg avatar upload rejected with 400');

    // 5. Real PPTX ZIP upload extracts Vietnamese text cleanly (ignoring .rels)
    const pptxBuf = makeZip([
      {
        name: 'ppt/slides/slide1.xml',
        data: '<?xml version="1.0" encoding="UTF-8"?><p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:t>Tham luận Kiến trúc An toàn Phần mềm OWASP 2026</a:t></p:sld>'
      },
      {
        name: 'ppt/slides/_rels/slide1.xml.rels',
        data: '<?xml version="1.0"?><Relationships><Relationship Id="rId1" Target="../media/image1.png"/></Relationships>'
      }
    ]);
    const pptxForm = new FormData();
    pptxForm.append('file', new Blob([pptxBuf], { type: 'application/vnd.openxmlformats-officedocument.presentationml.presentation' }), 'Tham luận.pptx');
    const pptxRes = await fetch(`${BASE}/api/studio/projects/${projectId}/upload-source`, {
      method: 'POST',
      headers: { Cookie: cookie },
      body: pptxForm
    });
    assert.equal(pptxRes.status, 200, `Expected 200 for PPTX upload, got ${pptxRes.status}`);
    const pptxData = await pptxRes.json();
    assert.equal(pptxData.success, true);
    assert.ok(pptxData.sourceContent.includes('Tham luận Kiến trúc An toàn Phần mềm OWASP 2026'), 'PPTX text must be extracted');
    assert.ok(!pptxData.sourceContent.includes('PK\x03\x04'), 'Must never dump raw ZIP bytes');
    console.log('PASS: PPTX upload extracted Vietnamese slide text cleanly');

    // 6. Image-only PPTX returns empty content + helpful warning (never binary garbage)
    const imgOnlyPptx = makeZip([
      { name: 'ppt/slides/slide1.xml', data: '<?xml version="1.0"?><p:sld><p:pic/></p:sld>' }
    ]);
    const imgForm = new FormData();
    imgForm.append('file', new Blob([imgOnlyPptx]), 'ImageOnly.pptx');
    const imgRes = await fetch(`${BASE}/api/studio/projects/${projectId}/upload-source`, {
      method: 'POST',
      headers: { Cookie: cookie },
      body: imgForm
    });
    assert.equal(imgRes.status, 200);
    const imgData = await imgRes.json();
    assert.equal(imgData.sourceContent, '', 'Image-only PPTX must not dump raw binary');
    assert.ok(imgData.warning && imgData.warning.length > 10, 'Image-only PPTX must return helpful warning');
    console.log('PASS: Image-only PPTX handled cleanly without binary dump');

    // 7. Stored XSS check in HyperFrames preview HTML
    const addSceneRes = await fetch(`${BASE}/api/studio/projects/${projectId}/scenes`, {
      method: 'POST',
      headers: { Cookie: cookie }
    });
    const added = await addSceneRes.json();
    const xssPayload = '</script><script>alert(1)</script><img src=x onerror=alert(1)>';
    await fetch(`${BASE}/api/studio/scenes/${added.scene.id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Cookie: cookie },
      body: JSON.stringify({ title: xssPayload, content: xssPayload })
    });
    const previewRes = await fetch(`${BASE}/studio/${projectId}/preview`, {
      headers: { Cookie: cookie }
    });
    assert.equal(previewRes.status, 200);
    const previewHtml = await previewRes.text();
    assert.ok(!previewHtml.includes('<img src=x onerror=alert(1)>'), 'Raw XSS HTML must be encoded');
    assert.ok(!previewHtml.includes('</script><script>alert(1)'), 'Raw </script> break-out must be escaped in JSON');
    console.log('PASS: HyperFrames preview safely encodes HTML & JSON against Stored XSS');
  } finally {
    await fetch(`${BASE}/api/studio/projects/${projectId}`, {
      method: 'DELETE',
      headers: { Cookie: cookie }
    });
  }

  console.log('--- ALL SECURITY & PPTX UPLOAD GUARDS PASSED 100% ---');
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
