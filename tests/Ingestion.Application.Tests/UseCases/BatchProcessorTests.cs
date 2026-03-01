using FluentAssertions;
using Ingestion.Application.UseCases;

namespace Ingestion.Application.Tests.UseCases;

public sealed class BatchProcessorTests
{
    private static BatchProcessor<int> CreateProcessor(
        int batchSize = 3,
        int flushIntervalSeconds = 1,
        Func<IReadOnlyList<int>, CancellationToken, Task>? flushDelegate = null)
    {
        flushDelegate ??= (_, _) => Task.CompletedTask;
        return new BatchProcessor<int>(batchSize, flushIntervalSeconds, flushDelegate);
    }

    // -------------------------------------------------------------------------
    // Constructor validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidBatchSize_ShouldThrow(int batchSize)
    {
        var act = () => new BatchProcessor<int>(batchSize, 5, (_, _) => Task.CompletedTask);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("batchSize");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidFlushInterval_ShouldThrow(int flushInterval)
    {
        var act = () => new BatchProcessor<int>(10, flushInterval, (_, _) => Task.CompletedTask);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("flushIntervalSeconds");
    }

    [Fact]
    public void Constructor_NullFlushDelegate_ShouldThrow()
    {
        var act = () => new BatchProcessor<int>(10, 5, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("flushDelegate");
    }

    // -------------------------------------------------------------------------
    // Flush by batch size
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FlushBySize_WhenBatchFull_ShouldFlushAllItems()
    {
        var flushed = new List<int>();

        var processor = CreateProcessor(
            batchSize: 3,
            flushIntervalSeconds: 30,
            flushDelegate: (batch, _) =>
            {
                flushed.AddRange(batch);
                return Task.CompletedTask;
            });

        using var cts = new CancellationTokenSource();

        var loop = Task.Run(() => processor.StartProcessingAsync(cts.Token));

        var ack1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ack2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ack3 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await processor.EnqueueAsync(1, ack1, CancellationToken.None);
        await processor.EnqueueAsync(2, ack2, CancellationToken.None);
        await processor.EnqueueAsync(3, ack3, CancellationToken.None);

        await Task.WhenAll(ack1.Task, ack2.Task, ack3.Task).WaitAsync(TimeSpan.FromSeconds(5));

        flushed.Should().BeEquivalentTo(new[] { 1, 2, 3 });

        await cts.CancelAsync();
    }

    // -------------------------------------------------------------------------
    // Flush by timer
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FlushByTimer_WhenIntervalExpires_ShouldFlushPartialBatch()
    {
        var flushed = new List<int>();

        var processor = CreateProcessor(
            batchSize: 100,
            flushIntervalSeconds: 1,
            flushDelegate: (batch, _) =>
            {
                flushed.AddRange(batch);
                return Task.CompletedTask;
            });

        using var cts = new CancellationTokenSource();
        _ = Task.Run(() => processor.StartProcessingAsync(cts.Token));

        var ack1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ack2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await processor.EnqueueAsync(10, ack1, CancellationToken.None);
        await processor.EnqueueAsync(20, ack2, CancellationToken.None);

        await Task.WhenAll(ack1.Task, ack2.Task).WaitAsync(TimeSpan.FromSeconds(5));

        flushed.Should().BeEquivalentTo(new[] { 10, 20 });

        await cts.CancelAsync();
    }

    // -------------------------------------------------------------------------
    // ACK propagation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AckTask_ShouldCompleteAfterFlush()
    {
        var tcs = new TaskCompletionSource();

        var processor = CreateProcessor(
            batchSize: 1,
            flushIntervalSeconds: 30,
            flushDelegate: async (_, _) =>
            {
                await Task.Delay(50); // simulate work
                tcs.TrySetResult();
            });

        using var cts = new CancellationTokenSource();
        _ = Task.Run(() => processor.StartProcessingAsync(cts.Token));

        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await processor.EnqueueAsync(99, ack, CancellationToken.None);

        ack.Task.IsCompleted.Should().BeFalse("flush hasn't completed yet before awaiting");

        await ack.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ack.Task.IsCompletedSuccessfully.Should().BeTrue();

        await cts.CancelAsync();
    }

    // -------------------------------------------------------------------------
    // Exception propagation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AckTask_WhenFlushThrows_ShouldFaultWithException()
    {
        var expectedException = new InvalidOperationException("flush failed");

        var processor = CreateProcessor(
            batchSize: 1,
            flushIntervalSeconds: 30,
            flushDelegate: (_, _) => throw expectedException);

        using var cts = new CancellationTokenSource();
        _ = Task.Run(() => processor.StartProcessingAsync(cts.Token));

        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await processor.EnqueueAsync(42, ack, CancellationToken.None);

        var act = async () => await ack.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("flush failed");

        await cts.CancelAsync();
    }

    // -------------------------------------------------------------------------
    // Multiple batches
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MultipleBatches_ShouldFlushIndependently()
    {
        var flushCount = 0;
        var allItems = new List<int>();

        var processor = CreateProcessor(
            batchSize: 2,
            flushIntervalSeconds: 30,
            flushDelegate: (batch, _) =>
            {
                flushCount++;
                allItems.AddRange(batch);
                return Task.CompletedTask;
            });

        using var cts = new CancellationTokenSource();
        _ = Task.Run(() => processor.StartProcessingAsync(cts.Token));

        var acks = Enumerable.Range(0, 4)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToList();

        for (var i = 0; i < 4; i++)
            await processor.EnqueueAsync(i + 1, acks[i], CancellationToken.None);

        await Task.WhenAll(acks.Select(a => a.Task)).WaitAsync(TimeSpan.FromSeconds(5));

        flushCount.Should().Be(2);
        allItems.Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });

        await cts.CancelAsync();
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EnqueueAsync_WhenCancelled_ShouldThrowOperationCancelledException()
    {
        var processor = new BatchProcessor<int>(
            batchSize: 1,
            flushIntervalSeconds: 30,
            flushDelegate: (_, _) => Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var act = () => processor.EnqueueAsync(1, ack, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StartProcessingAsync_WhenCancelled_ShouldCompleteGracefully()
    {
        var processor = CreateProcessor(batchSize: 100, flushIntervalSeconds: 30);
        using var cts = new CancellationTokenSource();

        var loopTask = Task.Run(() => processor.StartProcessingAsync(cts.Token));

        await cts.CancelAsync();

        await loopTask.WaitAsync(TimeSpan.FromSeconds(3));
        loopTask.IsCompletedSuccessfully.Should().BeTrue();
    }
}
