# Unity Object Pool Demo

A small, independently written reference implementation for explaining bounded object reuse. It is a generic C# library with automated tests; the README also shows how the lifecycle can be adapted to Unity `GameObject`s.

This repository contains **no source, assets, credentials, or build output from Humanity’s Last Stand or any private project**. It is an educational sample, not a copy of a shipped game’s implementation and not evidence that the released game used this exact code. It makes no zero-allocation or performance-improvement claim.

## Run the tests

Requires the .NET 10 SDK.

```sh
dotnet test tests/PoolingDemo.Tests/PoolingDemo.Tests.csproj
```

The tests cover prewarming and reference reuse, acquire/reset callbacks, rejection of foreign and duplicate releases, the retention limit, and disposal.

## What the pool guarantees

- It reuses reference instances returned to it.
- `maxRetained` bounds how many inactive objects it keeps. Additional returned instances are discarded through `onDiscard`.
- It rejects double returns and objects that are not currently leased from this pool.
- `Dispose` discards both active leases and cached objects, then prevents further use.
- The pool is **not thread-safe**. In Unity, call it on the main thread unless you add synchronization appropriate to your project.
- The pool reduces repeated object construction only when reuse actually occurs. It does not by itself guarantee zero managed allocations, low frame time, or better performance. Measure a representative build and workload in Unity Profiler.

## Unity lifecycle sketch

The library has no Unity dependency. Add its `netstandard2.1` source or assembly to the Unity project, then map rent/return/discard callbacks to activation and cleanup:

```csharp
using PoolingDemo;
using UnityEngine;

public sealed class ProjectilePool : System.IDisposable
{
    private readonly Transform inactiveRoot;
    private readonly ObjectPool<GameObject> pool;

    public ProjectilePool(GameObject prefab, Transform inactiveRoot, int capacity = 100)
    {
        this.inactiveRoot = inactiveRoot;
        pool = new ObjectPool<GameObject>(
            factory: () =>
            {
                var instance = UnityEngine.Object.Instantiate(prefab, inactiveRoot);
                instance.SetActive(false);
                return instance;
            },
            maxRetained: capacity,
            prewarm: capacity,
            onAcquire: instance => instance.SetActive(true),
            onRelease: instance =>
            {
                instance.SetActive(false);
                instance.transform.SetParent(this.inactiveRoot, false);
            },
            onDiscard: instance => UnityEngine.Object.Destroy(instance));
    }

    public GameObject Spawn(Vector3 position, Quaternion rotation)
    {
        var instance = pool.Get();
        instance.transform.SetPositionAndRotation(position, rotation);
        return instance;
    }

    public void Despawn(GameObject instance) => pool.Release(instance);

    public void Dispose() => pool.Dispose();
}
```

Before returning a projectile, the gameplay owner should stop its timers, detach event handlers, clear transient state, and ensure it cannot still affect the scene. In a real project, guard against returning an object after its pool has been disposed and decide how scene shutdown should order those calls.

## Design notes

The pool tracks leases by object identity, not by value equality. A release callback runs before the object becomes available again; if reset logic throws, the lease remains active so the caller can diagnose or retry it. Factory callbacks must return a fresh instance. Discard callbacks should clean up the object they receive.

## License

MIT. See [LICENSE](LICENSE).