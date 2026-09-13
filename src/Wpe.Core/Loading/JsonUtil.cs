using System.Text.Json;

namespace Wpe.Core.Loading;

/// <summary>Shared JSON options and value conversion helpers.</summary>
public static class JsonUtil
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Convert a JSON element into plain CLR values (double/bool/string/object[]).</summary>
    public static object? Scalar(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => el.EnumerateArray().Select(Scalar).ToArray(),
            _ => el.GetString()
        };
    }
}
