using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SwarmApp.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "app";

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Subscription>(entity =>
        {
            // One row per provider subscription: a redelivered webhook updates it rather
            // than creating a second, which is the whole idempotency story in one index.
            entity.HasIndex(s => s.ProviderSubscriptionId).IsUnique();
            entity.HasIndex(s => s.UserId);
            entity.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.HasDefaultSchema(Schema);

        builder.Entity<AppUser>(user =>
        {
            user.Property(entity => entity.DisplayName).HasMaxLength(200);
        });
    }
}
