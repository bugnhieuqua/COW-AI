using COW_AI.Models;
using COW_AI.Models.ViewModels;

namespace COW_AI.Services;

public interface IAuthService
{
    Task<(bool Success, string? Error, User? User)> RegisterAsync(RegisterViewModel model);
    Task<(bool Success, string? Error, User? User)> ValidateUserAsync(string usernameOrEmail, string password);
    Task<User?> GetUserByIdAsync(int id);
    Task<User?> GetUserByUsernameAsync(string username);
    Task SeedDefaultAdminAsync();
}
