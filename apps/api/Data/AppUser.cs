using Microsoft.AspNetCore.Identity;

namespace SwarmApp.Api.Data;

public sealed class AppUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
}
