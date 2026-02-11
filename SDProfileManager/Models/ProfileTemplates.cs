namespace SDProfileManager.Models;

public static class ProfileTemplates
{
    public const string ZeroUuid = "00000000-0000-0000-0000-000000000000";

    public static readonly IReadOnlyList<ProfileTemplate> All =
    [
        new(
            Id: "mini",
            Label: "Stream Deck Mini",
            DeviceModel: "20GAI9902",
            ProfileRootName: "3987917B-DACD-477A-BABC-8EEC9D5D94F6.sdProfile",
            DefaultPageId: "5e312b8f-eb08-4050-9d5f-3b76704c19bf",
            WorkingPageId: "7b823eed-b407-4549-877b-ba71b61b90a0",
            Columns: 3, Rows: 2, Dials: 0,
            ControllerOrder: [ControllerKind.Keypad]
        ),
        new(
            Id: "neo",
            Label: "Stream Deck Neo",
            DeviceModel: "20GBJ9901",
            ProfileRootName: "4B6D966C-8037-4CC2-8F2C-A3D0CE41053C.sdProfile",
            DefaultPageId: "e8b3aee1-c58c-4092-b1b1-9bfb36bf18e0",
            WorkingPageId: "1f2ee4e4-4496-4df0-8da3-8b564ad0df26",
            Columns: 4, Rows: 2, Dials: 0,
            ControllerOrder: [ControllerKind.Keypad, ControllerKind.Neo]
        ),
        new(
            Id: "sd15",
            Label: "Stream Deck",
            DeviceModel: "20GBL9901",
            ProfileRootName: "47BA4A1D-B876-4DEF-9AD7-1D966A64D341.sdProfile",
            DefaultPageId: "eee1dbe5-2ab8-45df-96f5-80caa89b1ceb",
            WorkingPageId: "99962573-27ce-4d35-96e6-f9d8b9e0451b",
            Columns: 5, Rows: 3, Dials: 0,
            ControllerOrder: [ControllerKind.Keypad]
        ),
        new(
            Id: "sdxl",
            Label: "Stream Deck XL",
            DeviceModel: "20GAT9902",
            ProfileRootName: "673C3A5E-B30C-4AD5-B8B7-03BF1686E9F9.sdProfile",
            DefaultPageId: "0d62177f-fa52-4dac-91bb-f4773d646ec9",
            WorkingPageId: "6239c2c6-0ad6-47b9-9e1f-70c254ddc7e6",
            Columns: 8, Rows: 4, Dials: 0,
            ControllerOrder: [ControllerKind.Keypad]
        ),
        new(
            Id: "sdplus",
            Label: "Stream Deck +",
            DeviceModel: "20GBD9901",
            ProfileRootName: "848EB342-A9D6-4388-BF1F-E9C53C9E7482.sdProfile",
            DefaultPageId: "d90b6cf1-70eb-4737-b2e8-eb821666a8d0",
            WorkingPageId: "502dc114-3541-49f4-9c29-6618ec59b8bc",
            Columns: 4, Rows: 2, Dials: 4,
            ControllerOrder: [ControllerKind.Encoder, ControllerKind.Keypad]
        ),
        new(
            Id: "sdplusxl",
            Label: "Stream Deck + XL",
            DeviceModel: "20GBX9901",
            ProfileRootName: "AD21D867-BE2D-4B6B-B358-5A5E74CF7280.sdProfile",
            DefaultPageId: "5c038231-2e92-45ea-a0a8-ba60e3799cf1",
            WorkingPageId: "f2fcf47e-e496-49e3-9f78-6affc2f8ce87",
            Columns: 9, Rows: 4, Dials: 6,
            ControllerOrder: [ControllerKind.Keypad, ControllerKind.Encoder]
        ),
        new(
            Id: "sdstudio",
            Label: "Stream Deck Studio",
            DeviceModel: "20GBO9901",
            ProfileRootName: "E837C4E1-6260-463E-95F9-D5974BB675FD.sdProfile",
            DefaultPageId: "890e7c01-8ca0-432e-a213-6372d4baed9a",
            WorkingPageId: "b888f2f4-8bf0-424a-acd6-fb5e3fb5a307",
            Columns: 16, Rows: 2, Dials: 2,
            ControllerOrder: [ControllerKind.Encoder, ControllerKind.Keypad]
        ),
        new(
            Id: "g100sd",
            Label: "Galleon 100 SD",
            DeviceModel: "GRETSCH",
            ProfileRootName: "89F30D12-7A76-4D0B-9462-65B815E812B9.sdProfile",
            DefaultPageId: "f8832b67-cd24-449d-9000-b17c1dac0e73",
            WorkingPageId: "b13a1727-8271-4408-85c7-c94b61861b0f",
            Columns: 3, Rows: 4, Dials: 2,
            ControllerOrder: [ControllerKind.Encoder, ControllerKind.Keypad]
        )
    ];

    public static readonly IReadOnlyDictionary<string, ProfileTemplate> ById =
        All.ToDictionary(t => t.Id);

    public static readonly IReadOnlyDictionary<string, ProfileTemplate> ByDeviceModel =
        All.ToDictionary(t => t.DeviceModel);

    public static ProfileTemplate GetTemplate(string? deviceModel)
    {
        if (deviceModel is not null && ByDeviceModel.TryGetValue(deviceModel, out var template))
            return template;
        return ById["sdplusxl"];
    }
}
