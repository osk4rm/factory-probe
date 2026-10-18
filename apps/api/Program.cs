using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using SwarmApp.Api.Infrastructure.Analytics;
using SwarmApp.Api.Infrastructure.Billing;
using SwarmApp.Api.Data;
using SwarmApp.Api.Infrastructure;
using SwarmApp.Api.Infrastructure.Email;
using SwarmApp.Api.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "swarmapp.auth";
    options.Cookie.HttpOnly = true;
    // Lax rather than Strict: Strict withholds the cookie on top-level navigations
    // from an email link, which would make a confirmation link land the user on a
    // signed-out page. SameAsRequest in Development (the dev server is http) and
    // Always everywhere else, where "SameAsRequest" would silently accept a cookie
    // over plain http if a proxy ever terminated TLS in front of the app.
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Keyed on Production, not on "not Development": the integration tests run in a
    // `Testing` environment over plain http, and forcing secure cookies there made every
    // authenticated request in the suite return 401. The guarantee that matters is about
    // Production, so that is what is keyed on.
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddControllersWithViews();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");
builder.Services.AddSwarmTelemetry(builder.Configuration);
// Email seam. The default provider writes files, so a fresh clone runs the whole
// signup and reset flow with no account, key or network. Configuration is validated
// at startup: a bad provider fails the boot, not a customer's password reset.
builder.Services.AddSwarmEmail(builder.Configuration);
// CORS and telemetry fail silently when wrong: a bad OTLP endpoint retries into a void,
// and a wildcard origin either throws at first use or quietly lets anyone call the API.
// Both are checked at startup instead of during the first request.
builder.Services.AddSwarmConfigurationValidation(builder.Configuration);
builder.Services.Configure<SwarmApp.Api.Infrastructure.Billing.BillingSettings>(
    builder.Configuration.GetSection(SwarmApp.Api.Infrastructure.Billing.BillingSettings.SectionName));
builder.Services.AddScoped<SwarmApp.Api.Infrastructure.Billing.ISubscriptionStatusLookup, SwarmApp.Api.Infrastructure.Billing.DbSubscriptionLookup>();
builder.Services.AddScoped<SwarmApp.Api.Infrastructure.Billing.SubscriptionWriter>();
builder.Services.AddPlanGate(); // extensions live in Infrastructure.Billing
// Analytics is vendor-neutral and off by default: the call sites exist, no SDK does.
builder.Services.AddSwarmAnalytics(builder.Configuration);
// Feature flags: unknown means off, so a typo disables rather than enables.
builder.Services.AddSwarmFeatureFlags();
builder.Services.AddScoped<SwarmApp.Api.Infrastructure.Billing.IBillingSessionProvider, SwarmApp.Api.Infrastructure.Billing.UnconfiguredBillingSessionProvider>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseSerilogRequestLogging();
// Before anything that can write a body: response headers must be set before the
// first byte, and a middleware registered later would be writing into a response
// that already went out.
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
// Redacted configuration view: enough to answer "is this deployment configured the
// way I think?" during an incident, and structurally unable to return a credential
// (it reads a fixed allow-list of reportable keys, not a filter over all of them).
app.MapGet("/health/config", (IConfiguration configuration) => Results.Ok(ConfigSnapshot.Describe(configuration)))
    .AllowAnonymous();

if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
{
    await app.MigrateAndSeedAsync();
}

app.Run();

public partial class Program;
