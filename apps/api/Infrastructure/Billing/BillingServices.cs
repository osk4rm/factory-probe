using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SwarmApp.Api.Data;

namespace SwarmApp.Api.Infrastructure.Billing;

public sealed class BillingSettings
{
    public const string SectionName = "Billing";

    /// <summary>The endpoint signing secret. Absent means billing is not configured.</summary>
    public string? WebhookSecret { get; set; }

    public bool Configured => !string.IsNullOrWhiteSpace(WebhookSecret);
}

/// <summary>
/// Answers "is this user paying?" from our cache.
/// </summary>
/// <remarks>
/// Active means active **and** inside the paid period: a row saying `Active` whose period
/// ended last week is stale, and a stale row must not grant access. This is the one place
/// where a cache is allowed to lose - it falls back to refusing.
/// </remarks>
public sealed class DbSubscriptionLookup(AppDbContext db) : ISubscriptionStatusLookup
{
    public async Task<bool> HasActiveSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        return await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active)
            .AnyAsync(s => s.CurrentPeriodEnd == null || s.CurrentPeriodEnd > now, cancellationToken);
    }
}

/// <summary>Applies a parsed provider change to our row. Upsert by provider id.</summary>
public sealed class SubscriptionWriter(AppDbContext db, ILogger<SubscriptionWriter> logger)
{
    public async Task ApplyAsync(BillingChange change, Guid? userId, CancellationToken cancellationToken = default)
    {
        var existing = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.ProviderSubscriptionId == change.SubscriptionId, cancellationToken);

        if (existing is not null)
        {
            existing.Status = change.Status;
            existing.ProviderCustomerId = change.CustomerId ?? existing.ProviderCustomerId;
            existing.CurrentPeriodEnd = change.CurrentPeriodEnd ?? existing.CurrentPeriodEnd;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (userId is null)
        {
            // No correlation and no local row: storing it would create a subscription we
            // cannot attribute to anyone. Acknowledged and dropped, never guessed at.
            logger.LogWarning("subscription {Id} has no local user; acknowledged without storing", change.SubscriptionId);
            return;
        }

        db.Subscriptions.Add(new Subscription
        {
            UserId = userId.Value,
            ProviderSubscriptionId = change.SubscriptionId,
            ProviderCustomerId = change.CustomerId,
            Status = change.Status,
            CurrentPeriodEnd = change.CurrentPeriodEnd,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
