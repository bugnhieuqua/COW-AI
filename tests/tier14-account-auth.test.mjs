import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');

test('Tier 14 - Probe 1: User entity and DbContext configure SQLite with unique indexes', () => {
    const userModel = fs.readFileSync(path.join(PROJECT_ROOT, 'Models', 'User.cs'), 'utf8');
    assert.ok(userModel.includes('public class User'), 'Must define User model');
    assert.ok(userModel.includes('public string PasswordHash'), 'Must store PasswordHash');
    assert.ok(userModel.includes('public string Role'), 'Must define Role');

    const dbContext = fs.readFileSync(path.join(PROJECT_ROOT, 'Data', 'AppDbContext.cs'), 'utf8');
    assert.ok(dbContext.includes('public DbSet<User> Users'), 'Must define Users DbSet');
    assert.ok(dbContext.includes('entity.HasIndex(u => u.Username).IsUnique()'), 'Username must have unique index');
    assert.ok(dbContext.includes('entity.HasIndex(u => u.Email).IsUnique()'), 'Email must have unique index');
});

test('Tier 14 - Probe 2: AuthService implements PBKDF2 PasswordHasher, validation and admin seed', () => {
    const authService = fs.readFileSync(path.join(PROJECT_ROOT, 'Services', 'AuthService.cs'), 'utf8');
    assert.ok(authService.includes('PasswordHasher<User>'), 'Must use ASP.NET Core PasswordHasher');
    assert.ok(authService.includes('RegisterAsync'), 'Must implement RegisterAsync');
    assert.ok(authService.includes('ValidateUserAsync'), 'Must implement ValidateUserAsync');
    assert.ok(authService.includes('SeedDefaultAdminAsync'), 'Must seed default admin if no user exists');
});

test('Tier 14 - Probe 3: AccountController handles Cookie authentication, Claims and status API', () => {
    const controller = fs.readFileSync(path.join(PROJECT_ROOT, 'Controllers', 'AccountController.cs'), 'utf8');
    assert.ok(controller.includes('CookieAuthenticationDefaults.AuthenticationScheme'), 'Must use Cookie authentication');
    assert.ok(controller.includes('ClaimTypes.NameIdentifier'), 'Must emit NameIdentifier claim');
    assert.ok(controller.includes('ClaimTypes.Role'), 'Must emit Role claim');
    assert.ok(controller.includes('public async Task<IActionResult> Logout()'), 'Must provide secure Logout action');
    assert.ok(controller.includes('/api/account/status'), 'Must expose /api/account/status endpoint');
});

test('Tier 14 - Probe 4: Account views render accessible forms, anti-forgery tokens and password toggles', () => {
    const loginHtml = fs.readFileSync(path.join(PROJECT_ROOT, 'Views', 'Account', 'Login.cshtml'), 'utf8');
    assert.ok(loginHtml.includes('@Html.AntiForgeryToken()'), 'Login form must include anti-forgery token');
    assert.ok(loginHtml.includes('togglePasswordBtn'), 'Login must include accessible password toggle');

    const registerHtml = fs.readFileSync(path.join(PROJECT_ROOT, 'Views', 'Account', 'Register.cshtml'), 'utf8');
    assert.ok(registerHtml.includes('@Html.AntiForgeryToken()'), 'Register form must include anti-forgery token');
    assert.ok(registerHtml.includes('asp-for="ConfirmPassword"'), 'Register must include confirm password');

    const profileHtml = fs.readFileSync(path.join(PROJECT_ROOT, 'Views', 'Account', 'Profile.cshtml'), 'utf8');
    assert.ok(profileHtml.includes('profile-role-badge'), 'Profile must render role badge');
});

test('Tier 14 - Probe 5: _Layout.cshtml renders User Avatar chip, role indicator and data-user-id on body', () => {
    const layout = fs.readFileSync(path.join(PROJECT_ROOT, 'Views', 'Shared', '_Layout.cshtml'), 'utf8');
    assert.ok(layout.includes('data-user-id="@(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)'), 'Must bind data-user-id');
    assert.ok(layout.includes('user-chip-btn'), 'Must render user avatar chip');
    assert.ok(layout.includes('user-dropdown-menu'), 'Must render user dropdown menu');
    assert.ok(layout.includes('btn-nav-login'), 'Must render Login CTA for guests');
});

test('Tier 14 - Probe 6: chat-history-store.js isolates sessions per userId for privacy', () => {
    const storeJs = fs.readFileSync(path.join(PROJECT_ROOT, 'wwwroot', 'js', 'modules', 'chat-history-store.js'), 'utf8');
    assert.ok(storeJs.includes('getUserId()'), 'Must detect active userId');
    assert.ok(storeJs.includes('cowai_chat_sessions_u'), 'Must partition sessions key by user');
    assert.ok(storeJs.includes('cowai_active_session_id_u'), 'Must partition active session key by user');
});

test('Tier 14 - Probe 7: components.css styles auth components with zero AI-slop', () => {
    const css = fs.readFileSync(path.join(PROJECT_ROOT, 'wwwroot', 'css', 'components.css'), 'utf8');
    assert.ok(css.includes('.auth-card'), 'Must style .auth-card');
    assert.ok(css.includes('.user-chip-btn'), 'Must style .user-chip-btn');
    assert.ok(css.includes('.app-toast'), 'Must style .app-toast');
});
