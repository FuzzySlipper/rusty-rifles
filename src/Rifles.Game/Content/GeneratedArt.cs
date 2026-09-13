using Rusty.Engine;

namespace Rifles.Game.Content;

/// <summary>Owns generated image resources admitted from the product art bundle.</summary>
internal sealed class GeneratedArt : IDisposable
{
    private const string BundleId = "generated-art";
    private const string AuthoredPathPrefix = "art/generated/";
    private readonly IGraphicsService graphics;
    private readonly ProductContentBundle bundle;
    private readonly Dictionary<(string Path, TextureFilter Filter, TextureWrap Wrap), RenderResourceInfo> resources = [];
    private bool disposed;

    internal GeneratedArt(ProductContent content, IGraphicsService graphics,
        IEnumerable<(string Path, TextureFilter Filter, TextureWrap Wrap)> requests)
    {
        ArgumentNullException.ThrowIfNull(content);
        this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        bundle = content.OpenBundle(BundleId);
        try
        {
            foreach (var request in requests)
                Open(request.Path, request.Filter, request.Wrap);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal RenderResourceInfo Texture(string path, TextureFilter filter, TextureWrap wrap)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return resources.TryGetValue((path, filter, wrap), out RenderResourceInfo resource)
            ? resource
            : throw new InvalidOperationException($"Generated art resource was not admitted: {path} ({filter}, {wrap}).");
    }

    private RenderResourceInfo Open(string path, TextureFilter filter, TextureWrap wrap)
    {
        if (resources.TryGetValue((path, filter, wrap), out RenderResourceInfo existing)) return existing;
        if (!path.StartsWith(AuthoredPathPrefix, StringComparison.Ordinal)
            || path.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"Generated art path must be under '{AuthoredPathPrefix}': {path}");

        using ContentReference reference = bundle.OpenReference(path[AuthoredPathPrefix.Length..]);
        RenderResourceInfo resource = graphics.OpenResourceFromContent(
            new RenderResourceContentRequest(reference, filter, wrap));
        if (resource.Kind != RenderResourceKind.Texture || resource.ByteLength == 0 || resource.Handle.Handle.Value == 0)
        {
            resource.Handle.Dispose();
            throw new InvalidDataException($"Generated art '{path}' must open as a non-empty Engine texture resource.");
        }
        resources.Add((path, filter, wrap), resource);
        return resource;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (RenderResourceInfo resource in resources.Values)
            resource.Handle.Dispose();
        resources.Clear();
        bundle.Dispose();
    }
}
