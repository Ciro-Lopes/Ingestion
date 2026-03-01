using System.Threading.Channels;

namespace Ingestion.Application.UseCases;

/// <summary>
/// Generic, infrastructure-agnostic batch accumulator.
/// Flushes when the batch reaches <see cref="BatchSize"/> items
/// OR when <see cref="FlushIntervalSeconds"/> elapses, whichever comes first.
/// </summary>
public sealed class BatchProcessor<T>
{
    private readonly int _batchSize;
    private readonly int _flushIntervalSeconds;
    private readonly Func<IReadOnlyList<T>, CancellationToken, Task> _flushDelegate;
    private readonly Channel<(T Item, TaskCompletionSource Ack)> _channel;

    public BatchProcessor(
        int batchSize,
        int flushIntervalSeconds,
        Func<IReadOnlyList<T>, CancellationToken, Task> flushDelegate)
    {
        _batchSize = batchSize > 0 ? batchSize : throw new ArgumentOutOfRangeException(nameof(batchSize));
        _flushIntervalSeconds = flushIntervalSeconds > 0 ? flushIntervalSeconds : throw new ArgumentOutOfRangeException(nameof(flushIntervalSeconds));
        _flushDelegate = flushDelegate ?? throw new ArgumentNullException(nameof(flushDelegate));

        _channel = Channel.CreateBounded<(T, TaskCompletionSource)>(
            new BoundedChannelOptions(batchSize * 4)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true
            });
    }

    /// <summary>
    /// Enqueues an item. The returned task completes when the item's batch has been flushed.
    /// </summary>
    public async Task EnqueueAsync(T item, TaskCompletionSource ack, CancellationToken cancellationToken)
    {
        await _channel.Writer.WriteAsync((item, ack), cancellationToken);
    }

    /// <summary>
    /// Starts the background processing loop. Should be called once per lifetime of the processor
    /// (e.g., from the hosting BackgroundService). Runs until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public async Task StartProcessingAsync(CancellationToken cancellationToken)
    {
        var batch = new List<(T Item, TaskCompletionSource Ack)>(_batchSize);

        while (!cancellationToken.IsCancellationRequested)
        {
            batch.Clear();

            // Build a timeout-linked token to enforce the flush interval.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_flushIntervalSeconds));

            try
            {
                while (batch.Count < _batchSize)
                {
                    var entry = await _channel.Reader.ReadAsync(timeoutCts.Token);
                    batch.Add(entry);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Flush interval expired — flush whatever accumulated so far.
            }

            if (batch.Count == 0)
                continue;

            var items = batch.Select(e => e.Item).ToList().AsReadOnly();

            try
            {
                await _flushDelegate(items, cancellationToken);

                foreach (var entry in batch)
                    entry.Ack.TrySetResult();
            }
            catch (Exception ex)
            {
                foreach (var entry in batch)
                    entry.Ack.TrySetException(ex);
            }
        }

        _channel.Writer.TryComplete();
    }
}
