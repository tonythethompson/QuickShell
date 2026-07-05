using System.Text.Json.Nodes;

namespace QuickShell.Services;

internal static class FormPayloadMerge
{
    public static string Merge(string inputs, string? data)
    {
        var merged = new JsonObject();
        MergeObject(merged, ParseObject(inputs));
        MergeObject(merged, ParseObject(data));
        return merged.ToJsonString();
    }

    private static void MergeObject(JsonObject target, JsonObject? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var property in source)
        {
            target[property.Key] = property.Value?.DeepClone();
        }
    }

    private static JsonObject? ParseObject(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json)?.AsObject();
}
