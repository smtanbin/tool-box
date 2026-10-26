using System.Collections.Specialized;
using System.Text.Json;
using System.Web;

namespace ToolBox.Engine;

/// <summary>One request to an in-process handler: query-string values plus an optional JSON body.</summary>
public sealed class Req
{
    public NameValueCollection Q { get; init; } = new();
    public JsonElement Body { get; init; }

    public string S(string name, string def = "") => Q[name] ?? def;
    public int I(string name, int def, int lo = int.MinValue, int hi = int.MaxValue) =>
        int.TryParse(Q[name], out var v) ? Math.Clamp(v, lo, hi) : def;
    public string B(string name, string def = "") => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;
    public bool BBool(string name) => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>Maps the old /api/... paths to C# handlers. A handler returns an object (serialised to JSON) or null for "no content".
/// Paths nobody has registered yet still fall through to the Go engine, so tools move over one at a time.</summary>
public static class Router
{
    static readonly Dictionary<string, Func<Req, Task<object?>>> Routes = new(StringComparer.OrdinalIgnoreCase);

    public static void Map(string path, Func<Req, Task<object?>> handler) => Routes[path] = req => handler(req) ?? Task.FromResult<object?>(null); // a lambda that just returns null binds here, not to the sync overload
    /// <summary>For handlers that answer "no content" (the old 204).</summary>
    public static void MapVoid(string path, Action<Req> handler) => Routes[path] = req => { handler(req); return Task.FromResult<object?>(null); };
    public static void Map(string path, Func<Req, object?> handler) => Routes[path] = r => Task.FromResult(handler(r));

    static Router()
    {
        NetEngine.Register();
        SysEngine.Register();
        LiveEngine.Register();
        HardwareEngine.Register();
        DiskEngine.Register();
    }

    public static async Task<(bool handled, JsonElement result)> TryAsync(string pathAndQuery, JsonElement body = default)
    {
        var i = pathAndQuery.IndexOf('?');
        var path = i < 0 ? pathAndQuery : pathAndQuery[..i];
        if (!Routes.TryGetValue(path, out var h)) return (false, default);
        var req = new Req { Q = HttpUtility.ParseQueryString(i < 0 ? "" : pathAndQuery[(i + 1)..]), Body = body };
        var obj = await Task.Run(() => h(req));
        return (true, obj == null ? default : JsonSerializer.SerializeToElement(obj));
    }
}
