using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations;

/// <summary>Bounds provider diagnostics before parsing JSON; response text never becomes an error message.</summary>
internal static class ProviderJson
{
    internal static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        const int limit = 64 * 1024;
        if (response.Content.Headers.ContentLength > limit)
            throw new JsonException("Provider response exceeded its limit.");
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[4096];
        while (true)
        {
            int read = await stream.ReadAsync(chunk, ct);
            if (read == 0)
                break;
            if (buffer.Length + read > limit)
                throw new JsonException("Provider response exceeded its limit.");
            buffer.Write(chunk, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
    }
}
