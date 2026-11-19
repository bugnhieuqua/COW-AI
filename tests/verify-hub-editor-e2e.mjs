import assert from 'node:assert/strict';

async function verifyHubEditorFlow() {
  const baseUrl = 'http://127.0.0.1:5223';

  // 1. Studio Hub (Hình 1 & 3)
  const hubRes = await fetch(`${baseUrl}/studio`);
  assert.strictEqual(hubRes.status, 200);
  const hubHtml = await hubRes.text();

  assert.ok(hubHtml.includes('id="studioHubView"'), 'Must render Studio Hub container');
  assert.ok(hubHtml.includes('id="btnHubCreateNew"'), 'Must render + Tạo mới button');
  assert.ok(hubHtml.includes('id="studioCreatePopover"'), 'Must render 3-column popover (Hình 3)');
  assert.ok(hubHtml.includes('id="studioAuthModal"'), 'Must render in-page auth modal');
  assert.ok(hubHtml.includes('id="btnCloseAuthModal"'), 'Must render close [X] button on auth modal');

  // 2. Editor Workspace (Hình 2)
  const editorRes = await fetch(`${baseUrl}/studio/editor/1`);
  assert.strictEqual(editorRes.status, 200);
  const editorHtml = await editorRes.text();
  assert.ok(editorHtml.includes('id="btnBackToHub"'), 'Must render Back to Hub button');
  assert.ok(editorHtml.includes('id="studioEditorView"'), 'Must render Editor workspace');
  assert.ok(editorHtml.includes('id="paneScenes"'), 'Editor contains Scenes pane');
  assert.ok(editorHtml.includes('id="paneAssets"'), 'Editor contains Assets pane');

  // 3. Standalone Login Page
  const loginRes = await fetch(`${baseUrl}/Account/Login`);
  const loginHtml = await loginRes.text();
  assert.ok(loginHtml.includes('auth-card__close-btn'), 'Login page must have [X] close button');

  // 4. In-page API Login
  const failLogin = await fetch(`${baseUrl}/api/account/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: 'wrong' })
  });
  assert.strictEqual(failLogin.status, 401, 'Wrong credentials return 401');

  const okLogin = await fetch(`${baseUrl}/api/account/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'admin', password: 'AdminPassword@123' })
  });
  assert.strictEqual(okLogin.status, 200, 'Valid credentials return 200');
  const userObj = await okLogin.json();
  assert.strictEqual(userObj.success, true);
  assert.strictEqual(userObj.user.username, 'admin');

  console.log('--- TOÀN BỘ KIỂM TRA HUB - POPOVER 3 CỘT - AUTH MODAL [X] - EDITOR THÀNH CÔNG 100% ---');
}

verifyHubEditorFlow().catch(err => {
  console.error('Lỗi kiểm thử:', err);
  process.exit(1);
});
