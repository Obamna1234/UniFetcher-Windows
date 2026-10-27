using Microsoft.Extensions.Logging;
using Nickvision.Desktop.Application;
using Nickvision.Parabolic.Shared.Helpers;
using Nickvision.Parabolic.Shared.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Services;

/// <summary>A download waiting for you (login, "verify you are human", or a site blocking the VPN).</summary>
public record WaitingLink(string Url, string Site, FailureReason Reason, DateTime Added, DownloadOptions Options);

/// <summary>
/// UniFetcher "Action needed" list. Keeps only the links (and download choices), never cookies,
/// so nothing expires while waiting; a fresh session is made when you press Done. Survives closing UniFetcher.
/// </summary>
public interface IActionNeededService
{
    event EventHandler? Changed;

    Task AddAsync(DownloadOptions options, FailureReason reason);

    Task RemoveAsync(string url);

    Task RemoveSiteAsync(string site);

    Task ClearAsync();

    Task<IReadOnlyList<WaitingLink>> GetAllAsync();
}

public class ActionNeededService : IActionNeededService
{
    private const string TableName = "unifetcher_action_needed";

    private readonly ILogger<ActionNeededService> _logger;
    private readonly IDatabaseService _databaseService;
    private readonly SemaphoreSlim _lock;
    private bool _tableEnsured;

    public event EventHandler? Changed;

    public ActionNeededService(ILogger<ActionNeededService> logger, IDatabaseService databaseService)
    {
        _logger = logger;
        _databaseService = databaseService;
        _lock = new SemaphoreSlim(1, 1);
    }

    public async Task AddAsync(DownloadOptions options, FailureReason reason)
    {
        await RunAsync(async () => await _databaseService.ReplaceIntoTableAsync(TableName, new Dictionary<string, object>
        {
            { "url", options.Url.ToString() },
            { "site", SiteSessionService.SiteOf(options.Url) },
            { "reason", (int)reason },
            { "added", DateTime.Now.ToString("o", CultureInfo.InvariantCulture) },
            { "options", JsonSerializer.Serialize(options, ApplicationJsonContext.Default.DownloadOptions) }
        }));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveAsync(string url)
    {
        await RunAsync(async () => await _databaseService.DeleteFromTableAsync(TableName, "url", url));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveSiteAsync(string site)
    {
        await RunAsync(async () => await _databaseService.DeleteFromTableAsync(TableName, "site", site));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearAsync()
    {
        await RunAsync(async () => await _databaseService.ClearTableAsync(TableName));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<WaitingLink>> GetAllAsync()
    {
        var list = new List<WaitingLink>();
        await RunAsync(async () =>
        {
            using var command = await _databaseService.SelectAllFromTableAsync(TableName);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                try
                {
                    var options = JsonSerializer.Deserialize(reader.GetString(4), ApplicationJsonContext.Default.DownloadOptions);
                    if (options is not null)
                    {
                        list.Add(new WaitingLink(reader.GetString(0), reader.GetString(1), (FailureReason)reader.GetInt32(2), DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture), options));
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning($"Skipped an unreadable waiting link: {e.Message}");
                }
            }
            return true;
        });
        list.Sort((a, b) => a.Added.CompareTo(b.Added));
        return list;
    }

    private async Task RunAsync(Func<Task<bool>> action)
    {
        await _lock.WaitAsync();
        try
        {
            if (!_tableEnsured)
            {
                await _databaseService.EnsureTableExistsAsync(TableName, "url TEXT PRIMARY KEY, site TEXT, reason INTEGER, added TEXT, options TEXT");
                _tableEnsured = true;
            }
            await action();
        }
        catch (Exception e)
        {
            _logger.LogError($"Action needed list error: {e.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }
}
