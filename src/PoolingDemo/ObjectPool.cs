using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PoolingDemo
{

    /// <summary>A bounded-retention pool for reference objects.</summary>
    /// <remarks>
    /// This type manages object reuse; it does not promise zero allocations or thread safety.
    /// Use it from the owning game thread unless the factory and callbacks provide their own safety.
    /// </remarks>
    public sealed class ObjectPool<T> : IDisposable where T : class
    {
        private readonly Stack<T> _available = new();
        private readonly HashSet<T> _leased = new(ReferenceComparer.Instance);
        private readonly HashSet<T> _owned = new(ReferenceComparer.Instance);
        private readonly Func<T> _factory;
        private readonly Action<T>? _onAcquire;
        private readonly Action<T>? _onRelease;
        private readonly Action<T>? _onDiscard;
        private readonly int _maxRetained;
        private bool _disposed;

        public ObjectPool(
            Func<T> factory,
            int maxRetained,
            int prewarm = 0,
            Action<T>? onAcquire = null,
            Action<T>? onRelease = null,
            Action<T>? onDiscard = null)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            if (maxRetained < 0) throw new ArgumentOutOfRangeException(nameof(maxRetained));
            if (prewarm < 0 || prewarm > maxRetained)
                throw new ArgumentOutOfRangeException(nameof(prewarm), "Prewarm must be between zero and the retention limit.");

            _factory = factory;
            _maxRetained = maxRetained;
            _onAcquire = onAcquire;
            _onRelease = onRelease;
            _onDiscard = onDiscard;

            for (var i = 0; i < prewarm; i++)
            {
                var item = Create();
                _available.Push(item);
            }
        }

        public int AvailableCount => _available.Count;
        public int LeasedCount => _leased.Count;
        public int MaxRetained => _maxRetained;

        public T Get()
        {
            ThrowIfDisposed();

            T item;
            if (_available.Count > 0)
            {
                item = _available.Pop();
            }
            else
            {
                item = Create();
            }

            _leased.Add(item);
            try
            {
                if (_onAcquire is not null) _onAcquire(item);
                return item;
            }
            catch (Exception acquireError)
            {
                _leased.Remove(item);
                _owned.Remove(item);
                try
                {
                    if (_onDiscard is not null) _onDiscard(item);
                }
                catch (Exception discardError)
                {
                    throw new AggregateException("Acquire and discard callbacks both failed.", acquireError, discardError);
                }

                throw;
            }
        }

        public void Release(T item)
        {
            ThrowIfDisposed();
            if (item is null) throw new ArgumentNullException(nameof(item));
            if (!_leased.Contains(item))
                throw new InvalidOperationException("The object is not currently leased by this pool.");

            // Keep the lease if user reset logic fails, so the caller can inspect or retry it.
            if (_onRelease is not null) _onRelease(item);
            _leased.Remove(item);

            if (_available.Count < _maxRetained)
            {
                _available.Push(item);
                return;
            }

            Discard(item);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            var remaining = new List<T>(_owned);
            _owned.Clear();
            _leased.Clear();
            _available.Clear();

            List<Exception>? failures = null;
            foreach (var item in remaining)
            {
                try
                {
                    if (_onDiscard is not null) _onDiscard(item);
                }
                catch (Exception error)
                {
                    if (failures is null) failures = new List<Exception>();
                    failures.Add(error);
                }
            }

            if (failures is not null)
                throw new AggregateException("One or more discard callbacks failed during disposal.", failures);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ObjectPool<T>));
        }

        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            public static readonly ReferenceComparer Instance = new();
            public bool Equals(T? left, T? right) => ReferenceEquals(left, right);
            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }

        private T Create()
        {
            var item = _factory();
            if (item is null) throw new InvalidOperationException("The pool factory returned null.");
            if (!_owned.Add(item))
                throw new InvalidOperationException("The pool factory returned an object already owned by this pool.");
            return item;
        }

        private void Discard(T item)
        {
            _owned.Remove(item);
            if (_onDiscard is not null) _onDiscard(item);
        }
    }
}
