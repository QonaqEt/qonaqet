using System.Security.Cryptography;
using System.Text.Json;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Push;

namespace Qonaqet.Application.Tests.Push;

public sealed class FcmPushSenderTests
{
    [Fact]
    public void Only_permanently_invalid_tokens_are_reported_for_cleanup()
    {
        string[] tokens = ["ok", "gone", "bad", "quota", "other-project", "server"];
        MessagingErrorCode?[] errors =
        [
            null,
            MessagingErrorCode.Unregistered,
            MessagingErrorCode.InvalidArgument,
            MessagingErrorCode.QuotaExceeded,
            MessagingErrorCode.SenderIdMismatch,
            MessagingErrorCode.Unavailable,
        ];

        Assert.Equal(["gone", "bad", "other-project"], FcmPushSender.InvalidTokens(tokens, errors));
    }

    [Fact]
    public void Without_firebase_credentials_the_noop_sender_is_used()
    {
        using var provider = Build(new Dictionary<string, string?>());

        Assert.IsType<NoopPushSender>(provider.GetRequiredService<IPushSender>());
    }

    [Fact]
    public void With_service_account_credentials_the_fcm_sender_is_used()
    {
        using var rsa = RSA.Create(2048);
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = "qonaqet-test",
            ["private_key_id"] = "test",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "push@qonaqet-test.iam.gserviceaccount.com",
            ["client_id"] = "1",
            ["token_uri"] = "https://oauth2.googleapis.com/token",
        });

        using var provider = Build(new Dictionary<string, string?> { ["Firebase:CredentialsJson"] = json, ["Firebase:ProjectId"] = "qonaqet-test" });

        Assert.IsType<FcmPushSender>(provider.GetRequiredService<IPushSender>());
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddPush(configuration);
        return services.BuildServiceProvider();
    }
}
