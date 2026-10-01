namespace HadalStream.Domain.Videos;

public enum Platform { Abyss, Dood }

public sealed record VideoRef(Platform Platform, string Id, string Url, string? Referer);

public sealed record Variant(string Label, long? Size);
