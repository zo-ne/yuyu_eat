using Microsoft.AspNetCore.Http;

namespace Yustore.Tests.TestHelpers
{
    // CartService 直接操作 HttpContext.Session，單元測試不需要真的跑一個 ASP.NET Core
    // pipeline，用這個最小的記憶體版 ISession 就夠了。
    internal sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _store.Keys;

        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
