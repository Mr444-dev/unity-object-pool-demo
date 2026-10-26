using PoolingDemo;
using Xunit;

namespace PoolingDemo.Tests;

public sealed class ObjectPoolTests
{
    [Fact]
    public void PrewarmsAndReusesTheSameInstance()
    {
        var creations = 0;
        using var pool = new ObjectPool<Marker>(() => new Marker(++creations), maxRetained: 2, prewarm: 2);

        var first = pool.Get();
        Assert.Equal(2, creations);
        Assert.Equal(1, pool.AvailableCount);
        Assert.Equal(1, pool.LeasedCount);

        pool.Release(first);
        var reused = pool.Get();

        Assert.Same(first, reused);
        Assert.Equal(2, creations);
        Assert.Equal(1, pool.AvailableCount);
    }

    [Fact]
    public void RunsAcquireAndResetCallbacksAtTheLifecycleBoundaries()
    {
        var acquired = 0;
        var released = 0;
        var item = new Marker(1);
        using var pool = new ObjectPool<Marker>(
            () => item,
            maxRetained: 1,
            onAcquire: value => { value.Value += 10; acquired++; },
            onRelease: value => { value.Value = 0; released++; });

        var rented = pool.Get();
        Assert.Equal(11, rented.Value);
        pool.Release(rented);

        Assert.Equal(0, rented.Value);
        Assert.Equal(1, acquired);
        Assert.Equal(1, released);
    }

    [Fact]
    public void RejectsForeignAndDoubleReleasedObjects()
    {
        using var pool = new ObjectPool<Marker>(() => new Marker(1), maxRetained: 1);
        var item = pool.Get();
        pool.Release(item);

        Assert.Throws<InvalidOperationException>(() => pool.Release(item));
        Assert.Throws<InvalidOperationException>(() => pool.Release(new Marker(2)));
    }

    [Fact]
    public void DiscardsObjectsWhenTheRetentionLimitIsFull()
    {
        var discarded = new List<Marker>();
        using var pool = new ObjectPool<Marker>(
            () => new Marker(1),
            maxRetained: 1,
            onDiscard: discarded.Add);

        var first = pool.Get();
        var second = pool.Get();
        pool.Release(first);
        pool.Release(second);

        Assert.Equal(1, pool.AvailableCount);
        Assert.Equal(new[] { second }, discarded);
    }

    [Fact]
    public void DisposeDiscardsBothLeasedAndAvailableObjectsExactlyOnce()
    {
        var discarded = new List<Marker>();
        var pool = new ObjectPool<Marker>(
            () => new Marker(1),
            maxRetained: 2,
            prewarm: 1,
            onDiscard: discarded.Add);
        var leased = pool.Get();
        var available = pool.Get();
        pool.Release(available);

        pool.Dispose();
        pool.Dispose();

        Assert.Equal(2, discarded.Count);
        Assert.Contains(leased, discarded);
        Assert.Contains(available, discarded);
        Assert.Throws<ObjectDisposedException>(() => pool.Get());
        Assert.Throws<ObjectDisposedException>(() => pool.Release(leased));
    }

    [Fact]
    public void RejectsInvalidCapacityAndPrewarmValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectPool<Marker>(() => new Marker(1), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectPool<Marker>(() => new Marker(1), 1, prewarm: 2));
    }

    private sealed class Marker(int value)
    {
        public int Value { get; set; } = value;
    }
}