using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace SwarmApp.Api.Infrastructure.Email;

/// <summary>
/// The one thing this template commits to about email: a single seam every
/// consumer uses, so changing provider is configuration rather than a refactor.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes each message to a file. The default provider: it needs no account, no
/// key and no network, so a fresh clone can run the whole signup flow and its
/// tests, and a developer can see exactly what a customer would have received.
/// </summary>
public sealed class FileEmailSender(
    IOptions<EmailOptions> options,
    IHostEnvironment environment,
    ILogger<FileEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var configured = options.Value.File.Directory;
        var directory = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
        Directory.CreateDirectory(directory);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var name = $"{stamp}-{Guid.NewGuid():N}.eml.txt";
        var path = Path.Combine(directory, name);

        var body = new StringBuilder()
            .Append("To: ").Append(message.To).Append('\n')
            .Append("Subject: ").Append(message.Subject).Append('\n')
            .Append("Reply-To: ").Append(message.ReplyTo ?? options.Value.FromAddress).Append('\n')
            .Append("Date: ").Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append('\n')
            .Append("--- text/plain ---\n").Append(message.TextBody).Append('\n')
            .Append("--- text/html ---\n").Append(message.HtmlBody).Append('\n')
            .ToString();

        await File.WriteAllTextAsync(path, body, Encoding.UTF8, cancellationToken);
        logger.LogInformation("email written to {Path} (to {To})", path, message.To);
    }
}

/// <summary>
/// Keeps messages in memory. Registered as a singleton so a test can resolve it and
/// assert on what the application *would* have sent, without touching the disk or
/// reaching a network.
/// </summary>
public sealed class CaptureEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyCollection<EmailMessage> Messages => _messages.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        while (_messages.TryDequeue(out _))
        {
            // drain
        }
    }
}

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registers the email seam, its options and the provider the configuration
    /// selects. The selection is read from configuration at resolution time, so a
    /// test can override <c>Email:Provider</c> without rebuilding the container.
    /// </summary>
    public static IServiceCollection AddSwarmEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();

        // Both senders are singletons: the file sink holds no state, and the capture
        // sink must be the same instance the test resolves.
        services.AddSingleton<FileEmailSender>();
        services.AddSingleton<CaptureEmailSender>();

        services.AddSingleton<IEmailSender>(provider =>
        {
            var configured = provider.GetRequiredService<IOptions<EmailOptions>>().Value;
            return configured.Provider.ToLowerInvariant() switch
            {
                "capture" => provider.GetRequiredService<CaptureEmailSender>(),
                "file" => provider.GetRequiredService<FileEmailSender>(),
                // Unreachable in a started host: the validator fails the boot first.
                _ => throw new InvalidOperationException(
                    $"Email:Provider '{configured.Provider}' is not registered. " +
                    $"Known providers: {string.Join(", ", EmailOptionsValidator.KnownProviders)}."),
            };
        });

        return services;
    }
}
