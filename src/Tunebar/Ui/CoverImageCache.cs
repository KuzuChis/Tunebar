using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tunebar.Core;

namespace Tunebar.Ui;

/// <summary>
/// Turns the cover of a source (bytes or URL) into an image the popup can show.
/// The last image per source is kept, so the 1-second refresh does not decode or download again.
/// </summary>
public static class CoverImageCache
{
    private const int DecodeWidth = 128;
    private const int MaxEntries = 32;

    private sealed record Entry(byte[]? Bytes, string? Url, ImageSource? Image);

    private static readonly Dictionary<string, Entry> Cache = new();

    /// <summary>The cover for this source, or null when it has none (the view shows a placeholder).</summary>
    public static ImageSource? For(MediaSource? source)
    {
        if (source is null) return null;

        if (Cache.TryGetValue(source.Id, out var entry) && IsSame(entry, source))
            return entry.Image;

        var image = Create(source);
        if (Cache.Count >= MaxEntries) Cache.Clear();
        Cache[source.Id] = new Entry(source.CoverBytes, source.CoverUrl, image);
        return image;
    }

    private static bool IsSame(Entry entry, MediaSource source)
    {
        if (entry.Url != source.CoverUrl) return false;
        if (ReferenceEquals(entry.Bytes, source.CoverBytes)) return true;
        return entry.Bytes is not null && source.CoverBytes is not null
            && entry.Bytes.AsSpan().SequenceEqual(source.CoverBytes);
    }

    private static ImageSource? Create(MediaSource source)
    {
        try
        {
            if (source.CoverBytes is { Length: > 0 } bytes) return FromBytes(bytes);
            if (source.CoverUrl is { } url && IsAllowedUrl(url, out var uri)) return FromUrl(uri);
        }
        catch (Exception)
        {
            // A broken image just means no cover.
        }
        return null;
    }

    private static BitmapImage FromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // read everything now, so the stream can be closed
        image.DecodePixelWidth = DecodeWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapImage FromUrl(Uri uri)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.DecodePixelWidth = DecodeWidth;
        image.UriSource = uri;
        image.EndInit(); // downloads in the background; not frozen because it may still be loading
        return image;
    }

    /// <summary>Only https and only known YouTube image hosts (the one place we touch the network).</summary>
    private static bool IsAllowedUrl(string url, out Uri uri)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri!) || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host.ToLowerInvariant();
        return host == "ytimg.com"
            || host.EndsWith(".ytimg.com", StringComparison.Ordinal)
            || host == "lh3.googleusercontent.com"
            || host == "yt3.ggpht.com";
    }
}
