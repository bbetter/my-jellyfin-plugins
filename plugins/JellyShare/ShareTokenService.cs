using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace JellyShare;

public record ShareToken(Guid ItemId, DateTime ExpiresAt);

public class ShareTokenService
{
    private readonly ConcurrentDictionary<string, ShareToken> _tokens = new();
    private readonly ILogger<ShareTokenService> _logger;
    private readonly string _tokenFilePath;
    private readonly object _saveLock = new();
    private readonly TimeProvider _timeProvider;

    public ShareTokenService(IApplicationPaths applicationPaths, ILogger<ShareTokenService> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        _tokenFilePath = Path.Combine(applicationPaths.PluginConfigurationsPath, "JellyShare.tokens.json");
        Load();
    }

    public string Create(Guid itemId, int expirationMinutes)
    {
        var token = GenerateToken();
        var entry = new ShareToken(itemId, _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(expirationMinutes));
        _tokens[token] = entry;
        _logger.LogInformation("Created share token for item {ItemId}, expires at {ExpiresAt}", itemId, entry.ExpiresAt);
        Save();
        return token;
    }

    public ShareToken? Validate(string token)
    {
        if (!_tokens.TryGetValue(token, out var entry))
            return null;

        if (_timeProvider.GetUtcNow().UtcDateTime > entry.ExpiresAt)
        {
            if (_tokens.TryRemove(token, out _))
            {
                _logger.LogDebug("Share token for item {ItemId} has expired", entry.ItemId);
                Save();
            }
            return null;
        }

        return entry;
    }

    public IEnumerable<KeyValuePair<string, ShareToken>> GetAll()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        bool changed = false;
        foreach (var key in _tokens.Keys.ToList())
        {
            if (_tokens.TryGetValue(key, out var t) && now > t.ExpiresAt)
            {
                if (_tokens.TryRemove(key, out _))
                    changed = true;
            }
        }

        if (changed) Save();
        return _tokens.ToArray();
    }

    public bool Revoke(string token)
    {
        if (_tokens.TryRemove(token, out _))
        {
            Save();
            return true;
        }
        return false;
    }

    public bool Extend(string token, int additionalMinutes)
    {
        if (!_tokens.TryGetValue(token, out var entry))
            return false;

        if (_timeProvider.GetUtcNow().UtcDateTime > entry.ExpiresAt)
        {
            // Expired tokens must not be resurrected; drop them instead.
            if (_tokens.TryRemove(token, out _))
            {
                _logger.LogDebug("Share token for item {ItemId} has expired", entry.ItemId);
                Save();
            }
            return false;
        }

        var newEntry = entry with { ExpiresAt = entry.ExpiresAt.AddMinutes(additionalMinutes) };
        _tokens[token] = newEntry;
        _logger.LogInformation("Extended share token for item {ItemId}, new expiration: {ExpiresAt}", entry.ItemId, newEntry.ExpiresAt);
        Save();
        return true;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_tokenFilePath)) return;
            var json = File.ReadAllText(_tokenFilePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var data = JsonSerializer.Deserialize<Dictionary<string, ShareToken>>(json, options);
            if (data == null) return;

            foreach (var kvp in data)
                _tokens[kvp.Key] = kvp.Value;

            _logger.LogInformation("Loaded {Count} share tokens from {Path}", _tokens.Count, _tokenFilePath);
        }
        catch (JsonException ex)
        {
            // Preserve the corrupt file so the next Save() does not silently discard it.
            var backupPath = _tokenFilePath + ".bak";
            try
            {
                File.Copy(_tokenFilePath, backupPath, overwrite: true);
                _logger.LogWarning(ex, "Share token file {Path} is corrupt; backed up to {BackupPath}", _tokenFilePath, backupPath);
            }
            catch (Exception backupEx)
            {
                _logger.LogError(backupEx, "Share token file {Path} is corrupt and could not be backed up to {BackupPath}", _tokenFilePath, backupPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load share tokens from {Path}", _tokenFilePath);
        }
    }

    private void Save()
    {
        lock (_saveLock)
        {
            var tempPath = _tokenFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(_tokens, options);

                // Write to a temp file in the same directory, then atomically replace the live file.
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _tokenFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save share tokens to {Path}", _tokenFilePath);
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32]; // 256 bits
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
