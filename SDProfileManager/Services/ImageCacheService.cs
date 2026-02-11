using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Media.Imaging;
using SDProfileManager.Models;

namespace SDProfileManager.Services;

public class ImageCacheService
{
    private readonly Dictionary<string, BitmapImage?> _cache = [];

    public BitmapImage? GetImage(ProfileArchive profile, JsonNode action, string? pageId = null)
    {
        var presentation = profile.GetActionPresentation(action);
        var imageRef = presentation.ImageReference;
        if (string.IsNullOrEmpty(imageRef)) return null;
        return GetImage(profile, imageRef, pageId);
    }

    public BitmapImage? GetImage(ProfileArchive profile, string imageRef, string? pageId = null)
    {
        if (string.IsNullOrEmpty(imageRef)) return null;

        var resolvedPageId = pageId ?? profile.ActivePageId;
        var cacheKey = $"{profile.Id}::{resolvedPageId}::{imageRef.ToLowerInvariant()}";

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var resolvedPath = profile.ResolveImagePath(imageRef, resolvedPageId);
        if (resolvedPath is null || !File.Exists(resolvedPath))
        {
            _cache[cacheKey] = null;
            return null;
        }

        try
        {
            var bitmap = new BitmapImage(new Uri(resolvedPath));
            _cache[cacheKey] = bitmap;
            return bitmap;
        }
        catch
        {
            _cache[cacheKey] = null;
            return null;
        }
    }

    public void Clear() => _cache.Clear();
}
