using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SwarmApp.Api.Data;

public static class DatabaseExtensions
{
    public const string AdminRole = "admin";
    public const string UserRole = "user";

    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { AdminRole, UserRole })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                // Two instances booting at once can both see the role as absent and both try to
                // create it; the loser gets a unique-constraint violation. That is a race, not a
                // failure - the role exists, which is what we wanted.
                try
                {
                    await roles.CreateAsync(new IdentityRole<Guid>(role));
                }
                catch (DbUpdateException)
                {
                    // Only benign if the role really is there now; otherwise rethrow.
                    if (!await roles.RoleExistsAsync(role)) throw;
                }
            }
        }
    }
}
