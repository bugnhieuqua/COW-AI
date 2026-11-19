using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using COW_AI.Data;
using COW_AI.Models;
using COW_AI.Models.ViewModels;

namespace COW_AI.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<User> _passwordHasher;
    private readonly ILogger<AuthService> _logger;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public AuthService(AppDbContext db, ILogger<AuthService> logger, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        _logger = logger;
        _config = config;
        _env = env;
        _passwordHasher = new PasswordHasher<User>();
    }

    public async Task<(bool Success, string? Error, User? User)> RegisterAsync(RegisterViewModel model)
    {
        var cleanUsername = model.Username.Trim().ToLowerInvariant();
        var cleanEmail = model.Email.Trim().ToLowerInvariant();

        // Kiểm tra username đã tồn tại chưa
        var usernameExists = await _db.Users.AnyAsync(u => u.Username.ToLower() == cleanUsername);
        if (usernameExists)
        {
            return (false, "Tên đăng nhập này đã được sử dụng. Vui lòng chọn tên khác.", null);
        }

        // Kiểm tra email đã tồn tại chưa
        var emailExists = await _db.Users.AnyAsync(u => u.Email.ToLower() == cleanEmail);
        if (emailExists)
        {
            return (false, "Địa chỉ email này đã được đăng ký. Vui lòng sử dụng email khác hoặc đăng nhập.", null);
        }

        // Nếu đây là tài khoản đầu tiên trong hệ thống, cấp quyền Admin
        var isFirstUser = !await _db.Users.AnyAsync();
        var role = isFirstUser ? "Admin" : "Member";

        var user = new User
        {
            Username = cleanUsername,
            Email = cleanEmail,
            FullName = string.IsNullOrWhiteSpace(model.FullName) ? model.Username : model.FullName.Trim(),
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, model.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Người dùng mới đã đăng ký thành công: {Username} (Role: {Role})", user.Username, user.Role);
        return (true, null, user);
    }

    public async Task<(bool Success, string? Error, User? User)> ValidateUserAsync(string usernameOrEmail, string password)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "Vui lòng nhập đầy đủ thông tin đăng nhập.", null);
        }

        var cleanInput = usernameOrEmail.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Username.ToLower() == cleanInput || u.Email.ToLower() == cleanInput);

        if (user == null)
        {
            return (false, "Tên đăng nhập hoặc mật khẩu không chính xác.", null);
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return (false, "Tên đăng nhập hoặc mật khẩu không chính xác.", null);
        }

        // Cập nhật thời điểm đăng nhập gần nhất
        user.LastLoginAt = DateTime.UtcNow;
        if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
        }
        await _db.SaveChangesAsync();

        _logger.LogInformation("Người dùng {Username} đăng nhập thành công vào COW.AI", user.Username);
        return (true, null, user);
    }

    public async Task<User?> GetUserByIdAsync(int id)
    {
        return await _db.Users.FindAsync(id);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        var clean = username.Trim().ToLowerInvariant();
        return await _db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == clean);
    }

    public async Task SeedDefaultAdminAsync()
    {
        var hasUsers = await _db.Users.AnyAsync();
        if (hasUsers) return;

        var defaultAdminUsername = _config["DEFAULT_ADMIN_USERNAME"] ?? "admin";
        var defaultAdminEmail = _config["DEFAULT_ADMIN_EMAIL"] ?? "admin@cow.ai";
        var configuredPassword = _config["DEFAULT_ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(configuredPassword) && !_env.IsDevelopment())
        {
            _logger.LogWarning("Bỏ qua khởi tạo Admin mặc định vì chưa cấu hình biến môi trường DEFAULT_ADMIN_PASSWORD trong môi trường Production.");
            return;
        }
        var defaultAdminPassword = string.IsNullOrWhiteSpace(configuredPassword) ? "AdminPassword@123" : configuredPassword;

        var adminUser = new User
        {
            Username = defaultAdminUsername.Trim().ToLowerInvariant(),
            Email = defaultAdminEmail.Trim().ToLowerInvariant(),
            FullName = "Quản trị viên COW.AI",
            Role = "Admin",
            CreatedAt = DateTime.UtcNow
        };

        adminUser.PasswordHash = _passwordHasher.HashPassword(adminUser, defaultAdminPassword);

        _db.Users.Add(adminUser);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Khởi tạo tài khoản Quản trị viên mặc định: {Username} ({Email})", adminUser.Username, adminUser.Email);
    }
}
