using SDProfileManager.Models;

namespace SDProfileManager.Tests.TestData;

internal static class ProfileFactory
{
    public static ProfileArchive Create(ProfileTemplate? template = null, params string[] pageIds)
    {
        var preset = template ?? ProfileTemplates.ById["sdplusxl"];
        var normalizedPageIds = (pageIds.Length > 0 ? pageIds : [preset.DefaultPageId])
            .Select(ProfileArchive.NormalizePageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedPageIds.Count == 0)
            normalizedPageIds.Add(ProfileArchive.NormalizePageId(preset.DefaultPageId));

        var firstPageId = normalizedPageIds[0];
        var pageStates = normalizedPageIds.ToDictionary(
            id => id,
            id => new ProfilePageState
            {
                Id = id,
                Manifest = new PageManifest { Name = $"Page {id}" }
            });

        var packageManifest = new PackageManifest
        {
            DeviceModel = preset.DeviceModel,
            RequiredPlugins = []
        };

        var profileManifest = new RootProfileManifest
        {
            Name = "Test Profile",
            Version = "1.0",
            Device = new DeviceManifest
            {
                Model = preset.DeviceModel,
                UUID = ProfileTemplates.ZeroUuid
            },
            Pages = new PagesManifest
            {
                Current = firstPageId,
                Default = firstPageId,
                Pages = [.. normalizedPageIds]
            }
        };

        return new ProfileArchive(
            sourcePath: null,
            extractedRootPath: Path.Combine(Path.GetTempPath(), "sdpm-tests", Guid.NewGuid().ToString("N")),
            preset: preset,
            name: "Test Profile",
            profileRootName: preset.ProfileRootName,
            activePageId: firstPageId,
            pageOrder: normalizedPageIds,
            pageStates: pageStates,
            packageManifest: packageManifest,
            profileManifest: profileManifest);
    }
}
