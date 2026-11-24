using System.Collections.Concurrent;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.Extensions.Options;

namespace AIQueryGenerator.Infrastructure.Security;

/// <summary>Limits concurrent target-database queries per user and across the API instance.</summary>
public sealed class QueryConcurrencyLimiter : IQueryConcurrencyLimiter
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _perUser = new();
    private readonly SemaphoreSlim _global;
    private readonly int _perUserLimit;

    public QueryConcurrencyLimiter(IOptions<ExecutionOptions> options)
    {
        _perUserLimit = Math.Max(1, options.Value.MaxConcurrentQueriesPerUser);
        _global = new SemaphoreSlim(Math.Max(1, options.Value.MaxConcurrentQueriesGlobal));
    }

    public async Task<IDisposable> AcquireAsync(int userId, CancellationToken cancellationToken)
    {
        var user = _perUser.GetOrAdd(userId, _ => new SemaphoreSlim(_perUserLimit));
        if (!await user.WaitAsync(0, cancellationToken))
            throw new ExternalServiceException($"You already have {_perUserLimit} queries running. Wait for them to finish.", ErrorCodes.ConcurrencyLimit, 429);

        try
        {
            if (!await _global.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken))
                throw new ExternalServiceException("The server is busy running other queries. Please retry shortly.", ErrorCodes.ConcurrencyLimit, 429);
        }
        catch
        {
            user.Release();
            throw;
        }

        return new Releaser(() => { _global.Release(); user.Release(); });
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) release();
        }
    }
}
