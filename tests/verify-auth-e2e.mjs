import assert from 'node:assert/strict';

async function testAuthFlow() {
    const baseUrl = 'http://127.0.0.1:5223';
    console.log('--- Bắt đầu kiểm thử E2E Hệ thống Đăng nhập / Xác thực ---');

    // 1. Lấy trang Login và bóc tách Antiforgery Cookie & Token
    const loginPageRes = await fetch(`${baseUrl}/Account/Login`);
    assert.strictEqual(loginPageRes.status, 200, 'Trang Login phải trả về 200 OK');
    const html = await loginPageRes.text();

    const tokenMatch = html.match(/name="__RequestVerificationToken" type="hidden" value="([^"]+)"/);
    assert.ok(tokenMatch, 'Trang Login phải chứa __RequestVerificationToken');
    const antiForgeryToken = tokenMatch[1];

    const initialCookies = loginPageRes.headers.getSetCookie();
    const cookieHeader = initialCookies.map(c => c.split(';')[0]).join('; ');

    // 2. Thử đăng nhập sai mật khẩu -> Phải hiển thị khối lỗi auth-alert--error
    const failedParams = new URLSearchParams();
    failedParams.append('__RequestVerificationToken', antiForgeryToken);
    failedParams.append('Username', 'admin');
    failedParams.append('Password', 'WrongPassword123');

    const failLoginRes = await fetch(`${baseUrl}/Account/Login`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/x-www-form-urlencoded',
            'Cookie': cookieHeader
        },
        body: failedParams.toString(),
        redirect: 'manual'
    });
    const failHtml = await failLoginRes.text();
    assert.ok(failHtml.includes('auth-alert--error'), 'Phải render hộp thoại lỗi auth-alert--error');
    console.log('✔ Xác thực bảo mật: Chặn đăng nhập sai mật khẩu chính xác.');

    // 3. Đăng nhập đúng với tài khoản Admin mặc định
    const successParams = new URLSearchParams();
    successParams.append('__RequestVerificationToken', antiForgeryToken);
    successParams.append('Username', 'admin');
    successParams.append('Password', 'AdminPassword@123');

    const loginRes = await fetch(`${baseUrl}/Account/Login`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/x-www-form-urlencoded',
            'Cookie': cookieHeader
        },
        body: successParams.toString(),
        redirect: 'manual'
    });

    assert.strictEqual(loginRes.status, 302, 'Đăng nhập thành công phải chuyển hướng 302');
    const authCookies = loginRes.headers.getSetCookie();
    const authCookieHeader = authCookies.map(c => c.split(';')[0]).join('; ');
    assert.ok(authCookieHeader.includes('COWAI_Auth'), 'Cookie xác thực COWAI_Auth phải được cấp phát');
    console.log('✔ Cấp phát phiên làm việc: Cookie COWAI_Auth được tạo thành công.');

    // 4. Kiểm tra endpoint /api/account/status với Auth Cookie
    const statusRes = await fetch(`${baseUrl}/api/account/status`, {
        headers: {
            'Cookie': authCookieHeader
        }
    });
    const statusData = await statusRes.json();
    assert.strictEqual(statusData.isAuthenticated, true, 'Tài khoản phải ở trạng thái Authenticated');
    assert.strictEqual(statusData.username, 'admin', 'Tên đăng nhập phải là admin');
    assert.strictEqual(statusData.role, 'Admin', 'Vai trò phải là Admin');
    console.log(`✔ Quyền hạn người dùng: Xác thực thành công ${statusData.fullName} (Vai trò: ${statusData.role}).`);

    // 5. Kiểm tra truy cập trang Profile bảo mật
    const profileRes = await fetch(`${baseUrl}/Account/Profile`, {
        headers: {
            'Cookie': authCookieHeader
        }
    });
    assert.strictEqual(profileRes.status, 200, 'Đã đăng nhập phải vào được /Account/Profile (HTTP 200)');
    const profileHtml = await profileRes.text();
    assert.ok(profileHtml.includes('Qu&#x1EA3;n tr&#x1ECB; vi&#xEA;n') || profileHtml.includes('Admin'), 'Profile phải hiển thị role badge Admin');
    console.log('✔ Trang hồ sơ: /Account/Profile render đầy đủ thông tin định danh và vai trò.');

    // 6. Kiểm tra đăng ký người dùng mới (Member)
    const regPageRes = await fetch(`${baseUrl}/Account/Register`);
    const regHtml = await regPageRes.text();
    const regTokenMatch = regHtml.match(/name="__RequestVerificationToken" type="hidden" value="([^"]+)"/);
    const regToken = regTokenMatch[1];
    const regCookies = regPageRes.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');

    const testUsername = 'dev_user_' + Date.now().toString(36);
    const regParams = new URLSearchParams();
    regParams.append('__RequestVerificationToken', regToken);
    regParams.append('Username', testUsername);
    regParams.append('FullName', 'Lập Trình Viên Mới');
    regParams.append('Email', `${testUsername}@example.com`);
    regParams.append('Password', 'MemberPass@123');
    regParams.append('ConfirmPassword', 'MemberPass@123');

    const doRegRes = await fetch(`${baseUrl}/Account/Register`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/x-www-form-urlencoded',
            'Cookie': regCookies
        },
        body: regParams.toString(),
        redirect: 'manual'
    });
    assert.strictEqual(doRegRes.status, 302, 'Đăng ký thành công phải redirect 302');
    const newMemberCookies = doRegRes.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');

    const newMemberStatusRes = await fetch(`${baseUrl}/api/account/status`, {
        headers: { 'Cookie': newMemberCookies }
    });
    const newMemberData = await newMemberStatusRes.json();
    assert.strictEqual(newMemberData.isAuthenticated, true);
    assert.strictEqual(newMemberData.username, testUsername);
    assert.strictEqual(newMemberData.role, 'Member', 'Tài khoản người dùng tiếp theo phải là Member');
    console.log(`✔ Đăng ký thành viên mới: Tạo thành công ${newMemberData.username} với vai trò ${newMemberData.role}.`);

    // 7. Kiểm tra Đăng xuất an toàn: Lấy Antiforgery Token và Cookie mới nhất của phiên đã đăng nhập
    const profilePageAfterReg = await fetch(`${baseUrl}/Account/Profile`, {
        headers: { 'Cookie': newMemberCookies }
    });
    const profileHtmlAfterReg = await profilePageAfterReg.text();
    const logoutTokenMatch = profileHtmlAfterReg.match(/name="__RequestVerificationToken" type="hidden" value="([^"]+)"/);
    const logoutToken = logoutTokenMatch ? logoutTokenMatch[1] : regToken;

    // Cập nhật cookie Antiforgery mới nếu server trả về
    let mergedCookies = newMemberCookies;
    const profileCookies = profilePageAfterReg.headers.getSetCookie ? profilePageAfterReg.headers.getSetCookie() : [];
    if (profileCookies.length > 0) {
        const profileCookieHeader = profileCookies.map(c => c.split(';')[0]).join('; ');
        mergedCookies = mergedCookies + '; ' + profileCookieHeader;
    }

    const logoutParams = new URLSearchParams();
    logoutParams.append('__RequestVerificationToken', logoutToken);
    const logoutRes = await fetch(`${baseUrl}/Account/Logout`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/x-www-form-urlencoded',
            'Cookie': mergedCookies
        },
        body: logoutParams.toString(),
        redirect: 'manual'
    });
    assert.strictEqual(logoutRes.status, 302, 'Đăng xuất phải redirect 302');
    const loggedOutCookies = logoutRes.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');

    const postLogoutStatusRes = await fetch(`${baseUrl}/api/account/status`, {
        headers: { 'Cookie': loggedOutCookies }
    });
    const postLogoutData = await postLogoutStatusRes.json();
    assert.strictEqual(postLogoutData.isAuthenticated, false, 'Sau đăng xuất phải trở lại trạng thái Guest');
    console.log('✔ Đăng xuất an toàn: Hủy bỏ phiên làm việc và trở về trạng thái Guest thành công.');

    console.log('\n--- TOÀN BỘ 7 BƯỚC KIỂM THỬ XÁC THỰC E2E THÀNH CÔNG 100% ---');
}

testAuthFlow().catch(err => {
    console.error('Lỗi kiểm thử E2E:', err);
    process.exit(1);
});
