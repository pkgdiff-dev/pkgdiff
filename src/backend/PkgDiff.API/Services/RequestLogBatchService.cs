using System.Threading.Channels;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Data;

namespace PkgDiff.API.Services;

public class RequestLogBatchService : BackgroundService
{
    private readonly Channel<RequestLog> _channel;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RequestLogBatchService> _logger;
    private readonly int _batchSize = 100;
    private readonly TimeSpan _flushInterval = TimeSpan.FromSeconds(5);

    public RequestLogBatchService(
        Channel<RequestLog> channel,
        IServiceProvider serviceProvider,
        ILogger<RequestLogBatchService> logger)
    {
        _channel = channel;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RequestLogBatchService is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = new List<RequestLog>();

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(_flushInterval);

                try
                {
                    while (batch.Count < _batchSize)
                    {
                        var log = await _channel.Reader.ReadAsync(cts.Token);
                        batch.Add(log);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Timeout reached, flush what we have
                }

                if (batch.Count > 0)
                {
                    await ProcessBatchAsync(batch, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing log batch");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("RequestLogBatchService is stopping.");
    }

    private async Task ProcessBatchAsync(List<RequestLog> batch, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.RequestLogs.AddRange(batch);
        
        try
        {
            await dbContext.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save batch of {Count} logs to database.", batch.Count);
        }
    }
}
