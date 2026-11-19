import assert from 'node:assert/strict';

async function testStudioAuthAndLayout() {
  const baseUrl = 'http://127.0.0.1:5223';
  
  console.log('--- 1. Kiểm thử Trang chủ (localhost:5223) giữ nguyên Chat Assistant kinh điển ---');
  const homeRes = await fetch(`${baseUrl}/`, { redirect: 'manual' });
  assert.strictEqual(homeRes.status, 200, 'Trang chủ / phục vụ giao diện chat chính');
  console.log('✔ Mở localhost:5223 giữ nguyên COW.AI Chat Assistant.');

  console.log('--- 2. Khách vãng lai dùng thử Studio ở chế độ Xem trước (Preview Mode) ---');
  const guestStudioRes = await fetch(`${baseUrl}/studio`);
  assert.strictEqual(guestStudioRes.status, 200, 'Khách vãng lai phải truy cập được Studio (HTTP 200)');
  const guestHtml = await guestStudioRes.text();

  // Kiểm tra Cấu trúc Panel bên trái (Left Sidebar)
  assert.ok(guestHtml.includes('studio-sidebar-tabs'), 'Cột trái phải có bộ Tab Switcher');
  assert.ok(guestHtml.includes('tabNavScenes'), 'Tab 1: Phân cảnh');
  assert.ok(guestHtml.includes('tabNavAssets'), 'Tab 2: Dự án & Nguồn tài liệu');
  assert.ok(guestHtml.includes('tabNavCopilot'), 'Tab 3: Trợ lý AI Copilot');
  assert.ok(guestHtml.includes('id="paneScenes"'), 'Pane 1: Danh sách phân cảnh');
  assert.ok(guestHtml.includes('id="paneAssets"'), 'Pane 2: Dự án & file nguồn');
  assert.ok(guestHtml.includes('id="paneCopilot"'), 'Pane 3: Khung chat trợ lý AI');
  console.log('✔ Khách xem được toàn bộ Cột Panel Quản Lý Bên Trái (3 tabs: Phân cảnh, Nguồn, Trợ lý AI).');

  // Kiểm tra Sân khấu trung tâm & Timeline
  assert.ok(guestHtml.includes('stageCyberCanvas'), 'Sân khấu trực tiếp phải có Cyber Canvas bài giảng chống lỗi ảnh');
  assert.ok(guestHtml.includes('stageAudioBars'), 'Sân khấu có sóng âm thanh thuyết minh VieNeu');
  assert.ok(guestHtml.includes('timelineTrack'), 'Đáy màn hình có Multi-Track Timeline phân cảnh');
  assert.ok(guestHtml.includes('studioPlanModal'), 'Modal Kế hoạch Auto-Video tồn tại');
  console.log('✔ Sân khấu trực tiếp (Live Stage Monitor) và Timeline hiển thị mượt mà.');

  console.log('--- 3. Chặn hành động Render/Lưu của khách vãng lai và yêu cầu Đăng nhập (401) ---');
  const renderRes = await fetch(`${baseUrl}/api/studio/projects/1/render-backend-video`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ aspectRatio: '16:9' })
  });
  assert.strictEqual(renderRes.status, 401, 'Khách vãng lai bấm Render phải bị chặn HTTP 401');

  const createProjRes = await fetch(`${baseUrl}/api/studio/projects`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ title: 'Dự án mới' })
  });
  assert.strictEqual(createProjRes.status, 401, 'Khách vãng lai tạo dự án mới phải bị chặn HTTP 401');
  console.log('✔ Bảo vệ nghiệp vụ: Bắt buộc Đăng nhập khi Lưu dự án / Render video.');

  console.log('--- 4. Đăng nhập với tài khoản Quản trị viên (Admin) ---');
  const loginPageRes = await fetch(`${baseUrl}/Account/Login`);
  const loginHtml = await loginPageRes.text();
  const tokenMatch = loginHtml.match(/name="__RequestVerificationToken" type="hidden" value="([^"]+)"/);
  assert.ok(tokenMatch, 'Trang Login phải có antiforgery token');
  const token = tokenMatch[1];
  const initialCookies = loginPageRes.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');

  const formParams = new URLSearchParams();
  formParams.append('__RequestVerificationToken', token);
  formParams.append('Username', 'admin');
  formParams.append('Password', 'AdminPassword@123');
  formParams.append('ReturnUrl', '/studio');

  const postLoginRes = await fetch(`${baseUrl}/Account/Login`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      'Cookie': initialCookies
    },
    body: formParams.toString(),
    redirect: 'manual'
  });

  const authCookies = postLoginRes.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
  const combinedCookies = [initialCookies, authCookies].filter(Boolean).join('; ');

  const authStudioRes = await fetch(`${baseUrl}/studio`, {
    headers: { 'Cookie': combinedCookies }
  });
  assert.strictEqual(authStudioRes.status, 200, 'Đăng nhập xong vào Studio HTTP 200');
  const authHtml = await authStudioRes.text();
  assert.ok(authHtml.includes('OWASP 2026'), 'Hiển thị bài giảng chuẩn');
  console.log('✔ Đăng nhập thành công, sở hữu toàn quyền quản lý bài giảng.');

  console.log('--- TOÀN BỘ KIỂM THỬ XÁC THỰC & BỐ CỤC PANEL TRÁI THÀNH CÔNG 100% ---');
}

testStudioAuthAndLayout().catch(err => {
  console.error('Lỗi kiểm thử:', err);
  process.exit(1);
});
