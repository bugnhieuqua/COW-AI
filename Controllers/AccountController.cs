using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using COW_AI.Models;
using COW_AI.Models.ViewModels;
using COW_AI.Services;

namespace COW_AI.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IAuthService authService, ILogger<AccountController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["ReturnUrl"] = model.ReturnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var (success, error, user) = await _authService.ValidateUserAsync(model.Username, model.Password);
        if (!success || user == null)
        {
            ModelState.AddModelError(string.Empty, error ?? "Đăng nhập không thành công.");
            return View(model);
        }

        await SignInUserAsync(user, model.RememberMe);
        TempData["ToastSuccess"] = $"Chào mừng trở lại, {user.FullName}!";
        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpPost("/api/account/login")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> ApiLogin([FromBody] ApiLoginRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest(new { success = false, error = "Tên đăng nhập và mật khẩu không được để trống." });
        }

        var (success, error, user) = await _authService.ValidateUserAsync(req.Username, req.Password);
        if (!success || user == null)
        {
            return Unauthorized(new { success = false, error = error ?? "Tên đăng nhập hoặc mật khẩu không chính xác." });
        }

        await SignInUserAsync(user, rememberMe: true);

        return Ok(new
        {
            success = true,
            user = new
            {
                id = user.Id,
                username = user.Username,
                fullName = user.FullName,
                role = user.Role,
                email = user.Email
            }
        });
    }

    [HttpPost("/api/account/register")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> ApiRegister([FromBody] ApiRegisterRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.Email))
        {
            return BadRequest(new { success = false, error = "Vui lòng điền đầy đủ các thông tin đăng ký." });
        }

        var regModel = new RegisterViewModel
        {
            Username = req.Username,
            Email = req.Email,
            Password = req.Password,
            ConfirmPassword = req.Password,
            FullName = req.FullName ?? req.Username
        };

        var (success, error, user) = await _authService.RegisterAsync(regModel);
        if (!success || user == null)
        {
            return BadRequest(new { success = false, error = error ?? "Đăng ký không thành công." });
        }

        await SignInUserAsync(user, rememberMe: true, justRegistered: true);

        return Ok(new
        {
            success = true,
            user = new
            {
                id = user.Id,
                username = user.Username,
                fullName = user.FullName,
                role = user.Role,
                email = user.Email
            }
        });
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var (success, error, user) = await _authService.RegisterAsync(model);
        if (!success || user == null)
        {
            ModelState.AddModelError(string.Empty, error ?? "Đăng ký không thành công.");
            return View(model);
        }

        await SignInUserAsync(user, rememberMe: true, justRegistered: true);
        TempData["ToastSuccess"] = $"Đăng ký tài khoản thành công! Chào mừng {user.FullName} đến với COW.AI.";
        return RedirectToAction("Index", "Home");
    }

    private Task SignInUserAsync(User user, bool rememberMe, bool justRegistered = false)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("FullName", user.FullName)
        };
        if (justRegistered)
        {
            claims.Add(new Claim("JustRegistered", "true"));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var props = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(14) : null,
            IssuedUtc = DateTimeOffset.UtcNow
        };

        return HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            props);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["ToastInfo"] = "Bạn đã đăng xuất an toàn khỏi hệ thống.";
        var returnUrl = Request.HasFormContentType ? Request.Form["returnUrl"].ToString() : null;
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(idClaim, out var userId))
        {
            var user = await _authService.GetUserByIdAsync(userId);
            if (user != null)
            {
                var vm = new ProfileViewModel
                {
                    Id = user.Id,
                    Username = user.Username,
                    FullName = user.FullName,
                    Email = user.Email,
                    Role = user.Role,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = user.LastLoginAt
                };
                return View(vm);
            }
        }

        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [HttpGet("/api/account/status")]
    public IActionResult GetStatus()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return Ok(new
            {
                isAuthenticated = true,
                userId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                username = User.Identity.Name,
                role = User.FindFirstValue(ClaimTypes.Role),
                fullName = User.FindFirstValue("FullName") ?? User.Identity.Name
            });
        }

        return Ok(new
        {
            isAuthenticated = false,
            userId = (string?)null,
            username = "Khách",
            role = "Guest",
            fullName = "Khách vãng lai"
        });
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Home");
    }
}

public record ApiLoginRequest(string Username, string Password);
public record ApiRegisterRequest(string Username, string Email, string Password, string? FullName);

