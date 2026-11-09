using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace JellyShare.Tests;

/// <summary>
/// Controllable clock so expiry checks are deterministic.
/// </summary>
internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan delta) => Now = Now.Add(delta);
}

public sealed class ShareTokenServiceTests : IDisposable
{
    private const string TokenFileName = "JellyShare.tokens.json";

    private readonly string _dir;
    private readonly IApplicationPaths _paths;
    private readonly TestClock _clock = new();

    public ShareTokenServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "jellyshare-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        _paths = Substitute.For<IApplicationPaths>();
        _paths.PluginConfigurationsPath.Returns(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of the temp directory.
        }
    }

    private ShareTokenService CreateService() =>
        new(_paths, NullLogger<ShareTokenService>.Instance, _clock);

    private string TokenFilePath => Path.Combine(_dir, TokenFileName);

    [Fact]
    public void Create_ThenValidate_ReturnsStoredItemAndToken()
    {
        var service = CreateService();
        var itemId = Guid.NewGuid();

        var token = service.Create(itemId, 30);
        var result = service.Validate(token);

        Assert.NotNull(result);
        Assert.Equal(itemId, result!.ItemId);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(30), result.ExpiresAt);
    }

    [Fact]
    public void Validate_AfterExpiry_ReturnsNullAndRemovesToken()
    {
        var service = CreateService();
        var token = service.Create(Guid.NewGuid(), 10);

        _clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        Assert.Null(service.Validate(token));
        Assert.Empty(service.GetAll());
        Assert.False(service.Revoke(token));
    }

    [Fact]
    public void Extend_NonExpiredToken_AddsMinutesAndReturnsTrue()
    {
        var service = CreateService();
        var itemId = Guid.NewGuid();
        var token = service.Create(itemId, 10);

        _clock.Advance(TimeSpan.FromMinutes(5));
        var extended = service.Extend(token, 60);

        Assert.True(extended);
        var result = service.Validate(token);
        Assert.NotNull(result);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(5 + 60), result!.ExpiresAt);
    }

    [Fact]
    public void Extend_ExpiredToken_ReturnsFalseAndDoesNotRevive()
    {
        var service = CreateService();
        var token = service.Create(Guid.NewGuid(), 10);

        _clock.Advance(TimeSpan.FromMinutes(11));

        Assert.False(service.Extend(token, 60));
        Assert.Null(service.Validate(token));
    }

    [Fact]
    public void Revoke_ReturnsTrueOnce_ThenFalse()
    {
        var service = CreateService();
        var token = service.Create(Guid.NewGuid(), 10);

        Assert.True(service.Revoke(token));
        Assert.False(service.Revoke(token));
        Assert.Null(service.Validate(token));
    }

    [Fact]
    public void GetAll_ExcludesAndRemovesExpiredTokens()
    {
        var service = CreateService();
        var liveItem = Guid.NewGuid();
        var liveToken = service.Create(liveItem, 120);
        var expiredToken = service.Create(Guid.NewGuid(), 5);

        _clock.Advance(TimeSpan.FromMinutes(6));

        var all = service.GetAll().ToList();

        Assert.Single(all);
        Assert.Equal(liveToken, all[0].Key);
        Assert.Equal(liveItem, all[0].Value.ItemId);
        Assert.False(service.Revoke(expiredToken));
    }

    [Fact]
    public void Persistence_NewInstanceWithSamePaths_ValidatesToken()
    {
        var itemId = Guid.NewGuid();
        var token = CreateService().Create(itemId, 30);

        var reloaded = CreateService();
        var result = reloaded.Validate(token);

        Assert.NotNull(result);
        Assert.Equal(itemId, result!.ItemId);
    }

    [Fact]
    public void CorruptFile_IsBackedUpAndServiceStillCreatesTokens()
    {
        const string corruptContent = "{ this is not valid json";
        File.WriteAllText(TokenFilePath, corruptContent);

        var service = CreateService();

        var backupPath = TokenFilePath + ".bak";
        Assert.True(File.Exists(backupPath));
        Assert.Equal(corruptContent, File.ReadAllText(backupPath));

        var token = service.Create(Guid.NewGuid(), 30);
        Assert.NotNull(service.Validate(token));

        // The live file should now be valid JSON again.
        using var doc = JsonDocument.Parse(File.ReadAllText(TokenFilePath));
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Create_LeavesNoTempFilesBehind()
    {
        var service = CreateService();
        service.Create(Guid.NewGuid(), 30);
        service.Create(Guid.NewGuid(), 30);

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.True(File.Exists(TokenFilePath));
    }
}
