namespace Cthangover.Tools.Map2d.Tests
{
    using System.Collections.Generic;
    using System.Text.Json.Nodes;
    using Cthangover.Mods.Map2d;
    using Cthangover.Tools.EntityEditor;

    /// <summary>Detached dependency data with root and point metadata, used to verify the same sparse patch contract as runtime.</summary>
    internal sealed class Map2dDocumentFixture
    {
        internal JsonObject Written { get; } = JsonNode.Parse("{\"Id\":\"map_document\",\"Image\":\"probe/layer\","
            + "\"Width\":4,\"Height\":3,\"PixelsX\":64,\"PixelsY\":48,\"Provider\":{\"keep\":42},"
            + "\"Markers\":[{\"Id\":\"entry\",\"X\":1.5,\"Y\":1.5,\"Annotation\":{\"keep\":13}}]}").AsObject();

        internal IEnumerable<(string Id, string OwnerMod, string File, JsonObject Written)> Entities()
        {
            return new[] { ("map_document", "probe_pack", "maps/terrain.json", Written) };
        }

        internal JsonObject Resolve(string id)
        {
            return id == "map_document" ? EntityJson.Normalize(Written, typeof(Map2dDefinition)) : null;
        }
    }
}
