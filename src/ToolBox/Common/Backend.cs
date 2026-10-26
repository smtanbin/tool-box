using System.Text.Json;
using ToolBox.Engine;

namespace ToolBox;

/// <summary>Entry point the pages call. Everything runs in-process through the C# engine (see Engine/).</summary>
public static class Backend
{
    public static async Task<JsonElement> GetAsync(string path)
    {
        var (handled, result) = await Router.TryAsync(path);
        if (!handled) throw new InvalidOperationException("Unknown request: " + path);
        return result;
    }

    public static async Task<JsonElement> PostAsync(string path, object body)
    {
        var (handled, result) = await Router.TryAsync(path, JsonSerializer.SerializeToElement(body));
        if (!handled) throw new InvalidOperationException("Unknown request: " + path);
        return result;
    }

}

public static class Json
{
    public static string Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";

    public static double Num(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    public static JsonElement Get(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static IEnumerable<JsonElement> Items(this JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : e.ValueKind == JsonValueKind.Object ? new[] { e } : Array.Empty<JsonElement>();

    public static string Bytes(double b) =>
        b >= 1L << 40 ? $"{b / (1L << 40):0.##} TB" : b >= 1L << 30 ? $"{b / (1L << 30):0.##} GB" : b >= 1L << 20 ? $"{b / (1L << 20):0.#} MB" : b >= 1024 ? $"{b / 1024:0} KB" : $"{b:0} B";
}
