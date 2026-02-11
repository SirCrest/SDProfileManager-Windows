namespace SDProfileManager.Models;

public record ProfileTemplate(
    string Id,
    string Label,
    string DeviceModel,
    string ProfileRootName,
    string DefaultPageId,
    string WorkingPageId,
    int Columns,
    int Rows,
    int Dials,
    ControllerKind[] ControllerOrder
);
