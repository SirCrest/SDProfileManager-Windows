using System.Text.Json.Nodes;

namespace SDProfileManager.Models;

public class DragContext
{
    public PaneSide SourceSide { get; }
    public string SourcePageId { get; }
    public ControllerKind Controller { get; }
    public string Coordinate { get; }
    public JsonNode Action { get; }

    public DragContext(PaneSide sourceSide, string sourcePageId, ControllerKind controller, string coordinate, JsonNode action)
    {
        SourceSide = sourceSide;
        SourcePageId = sourcePageId;
        Controller = controller;
        Coordinate = coordinate;
        Action = action;
    }
}
