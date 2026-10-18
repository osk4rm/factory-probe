using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SwarmApp.Api.Contracts;
using SwarmApp.Api.Data;
using SwarmApp.Api.Infrastructure.Analytics;
using SwarmApp.Api.Infrastructure.Email;

namespace SwarmApp.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    IAntiforgery antiforgery,
    IEmailSender email,
    IOptions<EmailOptions> emailOptions,
    IAnalytics analytics,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Issues the CSRF token the SPA must echo in the X-CSRF-TOKEN header.</summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request)
    {
        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
        };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(ToDictionary(result)));
        }

        await users.AddToRoleAsync(user, DatabaseExtensions.UserRole);

        // The account exists but is not confirmed. Signing in is still allowed, because
        // refusing it would lock a user out of their own account if the email never
        // arrives; the client gates features on `emailConfirmed` instead. A product that
        // needs confirmation before any access should gate at the feature, not the login.
        await SendConfirmationAsync(user);

        await signIn.SignInAsync(user, isPersistent: false);

        // No email, no identifier: the guard would drop either, and an event that silently
        // loses a property is worse than one that never had it.
        analytics.Track("user.registered", new Dictionary<string, object?>
        {
            ["method"] = "password",
            ["confirmation_sent"] = true,
        });

        return Ok(await ToResponseAsync(user));
    }

    [HttpPost("confirm")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(ConfirmEmailRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        // One message for every failure: a distinct "no such user" reply turns this
        // endpoint into an account-existence oracle.
        if (user is null) return BadRequest(new { error = "invalid_or_expired_token" });

        var result = await users.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded) return BadRequest(new { error = "invalid_or_expired_token" });

        return NoContent();
    }

    [HttpPost("forgot")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Forgot(ForgotPasswordRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        // Always 204, whether or not the account exists: this endpoint is not a way to
        // discover which addresses are registered.
        if (user is not null) await SendResetAsync(user);
        return NoContent();
    }

    [HttpPost("reset")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(ResetPasswordRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null) return BadRequest(new { error = "invalid_or_expired_token" });

        var result = await users.ResetPasswordAsync(user, request.Token, request.Password);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(ToDictionary(result)));
        }

        // A reset is the moment to end other sessions: whoever asked for it may be
        // recovering from a compromise.
        await signIn.SignOutAsync();
        return NoContent();
    }

    [HttpPost("resend-confirmation")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendConfirmation()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (!user.EmailConfirmed) await SendConfirmationAsync(user);
        return NoContent();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<UserResponse>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await signIn.PasswordSignInAsync(
            user, request.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status423Locked);
        }

        if (!result.Succeeded)
        {
            return Unauthorized();
        }

        analytics.Track("user.signed_in", new Dictionary<string, object?>
        {
            ["method"] = "password",
            ["persistent"] = true,
        });

        return Ok(await ToResponseAsync(user));
    }

    [HttpPost("logout")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> Me()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(await ToResponseAsync(user));
    }

    private async Task SendConfirmationAsync(AppUser user)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = Link("confirm", user.Email!, token);
        await email.SendAsync(EmailTemplates.Confirm(user.Email ?? string.Empty, link));
    }

    private async Task SendResetAsync(AppUser user)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = Link("reset", user.Email!, token);
        await email.SendAsync(EmailTemplates.Reset(user.Email ?? string.Empty, link));
    }

    /// <summary>
    /// Builds an absolute link. The template must never receive a relative URL, so a
    /// missing base URL is a configuration error rather than something to paper over.
    /// </summary>
    private string Link(string action, string address, string token)
    {
        var baseUrl = emailOptions.Value.PublicBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogError("Email:PublicBaseUrl is not set; cannot build a {Action} link", action);
            throw new InvalidOperationException("Email:PublicBaseUrl must be configured to send link emails");
        }

        return $"{baseUrl.TrimEnd('/')}/{action}?email={Uri.EscapeDataString(address)}&token={Uri.EscapeDataString(token)}";
    }

    private async Task<UserResponse> ToResponseAsync(AppUser user) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            (await users.GetRolesAsync(user)).ToArray(),
            user.EmailConfirmed);

    private static Dictionary<string, string[]> ToDictionary(IdentityResult result) =>
        result.Errors
            .GroupBy(error => error.Code)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray());
}
