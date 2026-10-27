using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Harvest.Tests.Integration.Capabilities;

/// <summary>A tiny local HTTP host on loopback that answers whatever a test tells it to — a page, a 304, a 429 with a
/// <c>Retry-After</c>, a robots file, a challenge page — and records the requests it received.</summary>
/// <remarks>It lets the fetch lane be driven against a real socket without reaching the open internet.</remarks>
internal sealed class FixtureHost : IAsyncDisposable
{
    private readonly HttpListener listener;
    private readonly Func<FixtureRequest, FixtureReply> respond;
    private readonly List<FixtureRequest> received = [];
    private readonly Lock gate = new();
    private readonly Task loop;

    private FixtureHost(HttpListener listener, int port, Func<FixtureRequest, FixtureReply> respond)
    {
        this.listener = listener;
        this.respond = respond;
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        loop = Task.Run(ServeAsync);
    }

    /// <summary>Gets the host's base URL, on loopback.</summary>
    public Uri BaseUri { get; }

    /// <summary>Gets the requests the host received, oldest first.</summary>
    public IReadOnlyList<FixtureRequest> Received
    {
        get { lock (gate) return [.. received]; }
    }

    /// <summary>Starts a host that answers through <paramref name="respond"/>.</summary>
    /// <param name="respond">The reply for each request.</param>
    public static FixtureHost Start(Func<FixtureRequest, FixtureReply> respond)
    {
        var port = FreePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        return new FixtureHost(listener, port, respond);
    }

    private async Task ServeAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            var request = new FixtureRequest(context.Request.HttpMethod, context.Request.Url!.AbsolutePath, context.Request.Url!.PathAndQuery, context.Request.Headers);
            lock (gate)
                received.Add(request);

            var reply = respond(request);
            var response = context.Response;
            response.StatusCode = reply.Status;
            foreach (var (name, value) in reply.Headers)
                response.AddHeader(name, value);

            if (reply.Body.Length > 0)
            {
                response.ContentLength64 = reply.Body.Length;
                await response.OutputStream.WriteAsync(reply.Body);
            }
            else
            {
                response.ContentLength64 = 0;
            }

            response.Close();
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        listener.Stop();
        listener.Close();
        try
        {
            await loop;
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // The serve loop ends when the listener stops; nothing more to clean up.
        }
    }
}

/// <summary>One request the fixture host received.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path, without the query.</param>
/// <param name="PathAndQuery">The path with its query.</param>
/// <param name="Headers">The request headers.</param>
internal sealed record FixtureRequest(string Method, string Path, string PathAndQuery, System.Collections.Specialized.NameValueCollection Headers)
{
    /// <summary>Reads a request header, or null.</summary>
    public string? Header(string name) => Headers[name];
}

/// <summary>One reply the fixture host sends.</summary>
internal sealed record FixtureReply
{
    /// <summary>Gets the status code.</summary>
    public int Status { get; init; } = 200;

    /// <summary>Gets the response headers.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>Gets the body bytes.</summary>
    public byte[] Body { get; init; } = [];

    /// <summary>A reply with an HTML body and the given status.</summary>
    /// <param name="html">The body.</param>
    /// <param name="status">The status.</param>
    /// <param name="headers">Extra headers.</param>
    public static FixtureReply Html(string html, int status = 200, params (string Name, string Value)[] headers) => new()
    {
        Status = status,
        Headers = [new("Content-Type", "text/html; charset=utf-8"), .. headers.Select(header => new KeyValuePair<string, string>(header.Name, header.Value))],
        Body = Encoding.UTF8.GetBytes(html),
    };

    /// <summary>A reply with a plain-text body, such as a <c>robots.txt</c>.</summary>
    /// <param name="text">The body.</param>
    /// <param name="status">The status.</param>
    public static FixtureReply Text(string text, int status = 200) => new()
    {
        Status = status,
        Headers = [new("Content-Type", "text/plain; charset=utf-8")],
        Body = Encoding.UTF8.GetBytes(text),
    };

    /// <summary>A reply with a status and headers but no body, such as a 304 or a 429.</summary>
    /// <param name="status">The status.</param>
    /// <param name="headers">The headers.</param>
    public static FixtureReply Status_(int status, params (string Name, string Value)[] headers) => new()
    {
        Status = status,
        Headers = [.. headers.Select(header => new KeyValuePair<string, string>(header.Name, header.Value))],
    };
}
