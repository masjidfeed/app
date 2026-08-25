namespace MasjidApp.Core.Http;

/// <summary>A remote image body and its declared media type, retained for offline rendering.</summary>
public sealed record CachedImage(byte[] Bytes, string? MediaType);