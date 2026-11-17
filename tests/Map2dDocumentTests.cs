namespace Cthangover.Tools.Map2d.Tests
{
    using System;
    using System.Text.Json;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Map2d;
    using Cthangover.Tools.EntityEditor;

    /// <summary>Forms preserve data through files, point moves are undoable, and foreign changes remain narrow keyed patches.</summary>
    public sealed class Map2dDocumentTests
    {
        private const string Map = "map_document";

        [ModTest("map_document/forms_points_layers_undo_and_complete_file_round_trip")]
        private static void Forms(ModTestContext context)
        {
            var data = Empty();
            var document = new Map2dDocument(data);
            var memory = JsonSerializer.Serialize(ExplorationMemory.Snapshot());
            ModAssert.True(document.Read(Map, 4, 3).Image == null, "new map never invents a prepared layer");
            ModAssert.Equal(0, data.ChangedFiles().Count, "inspecting a new map writes nothing");
            ModAssert.True(document.Layer(Map, 4, 3, "probe/layer", 64, 48), "prepared layer configures a map");
            ModAssert.False(document.Layer(Map, 4, 3, "probe/layer", 64, 48), "same prepared metadata is a no-op");
            document.Titles(Map, 4, 3, "Берег", "Shore");
            var first = document.AddPoint(Map, 4, 3);
            var second = document.AddPoint(Map, 4, 3);
            ModAssert.False(first == second, "point identities are reserved before insertion");
            document.Point(Map, 4, 3, first, "Вход", "Entry", 1.25f, 2.5f, true);
            ModAssert.True(data.Undo(), "one point form update is one undo step");
            ModAssert.True(document.Read(Map, 4, 3).Markers[0].X == 0.5f, "undo restores its previous location");
            data.Redo();
            var expected = document.Read(Map, 4, 3);
            expected.Validate();
            var files = data.ChangedFiles();
            var reload = EntityDocument.Load(Map2dDefinition.ResourceKind, "maps/terrain_maps.json", files, out var skipped);
            var restored = new Map2dDocument(reload).Read(Map, 4, 3);
            restored.Validate();
            ModAssert.Equal(0, skipped.Count, "saved map document is readable");
            ModAssert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(restored),
                "all titles, layer metadata and complete point fields survive the JSON file cycle");
            ModAssert.True(restored.Markers[0].AlwaysVisible && restored.Markers[0].X == 1.25f
                && restored.Markers[0].Y == 2.5f, "authored public point retains fractional cell coordinates");
            var before = data.ChangedFiles()[0].Text;
            var rejected = false;
            try
            {
                document.Point(Map, 4, 3, first, "Вход", "Entry", 4, 2, false);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            ModAssert.True(rejected, "far-edge point fails before mutation");
            ModAssert.Equal(before, data.ChangedFiles()[0].Text, "invalid form preserves the exact prior document");
            ModAssert.True(document.RemovePoint(Map, second), "selected point can be removed");
            ModAssert.False(document.RemovePoint(Map, second), "missing point is a no-op");
            ModAssert.Equal(1, document.Read(Map, 4, 3).Markers.Count, "removal preserves its neighbour");
            document.Remove(Map);
            ModAssert.True(data.FindAny(Map) == null, "own map recipe is removable");
            data.Undo();
            ModAssert.Equal(1, document.Read(Map, 4, 3).Markers.Count, "undo map removal retains previous points");
            ModAssert.Equal(memory, JsonSerializer.Serialize(ExplorationMemory.Snapshot()), "authoring never explores terrain");
        }

        [ModTest("map_document/foreign_keyed_point_diff_unknown_metadata_and_patch_round_trip")]
        private static void Foreign(ModTestContext context)
        {
            var source = new Map2dDocumentFixture();
            var original = source.Written.ToJsonString();
            var data = Empty();
            data.AttachForeign("patches/map2d/probe.json", null, typeof(Map2dDefinition), source.Entities(), source.Resolve);
            var document = new Map2dDocument(data);
            document.Point(Map, 4, 3, "entry", "", "", 2.25f, 1.75f, false);
            var patch = data.PatchOf(Map);
            ModAssert.Equal(2, patch.Count, "point movement changes only identity and keyed marker operation");
            var entry = patch["Markers$add"][0].AsObject();
            ModAssert.Equal(3, entry.Count, "keyed point diff includes only identity and changed coordinates");
            ModAssert.True(entry["X"].GetValue<float>() == 2.25f && entry["Y"].GetValue<float>() == 1.75f,
                "sparse movement records the exact authored coordinates");
            ModAssert.Equal(0, data.ChangedFiles().Count, "foreign owner never joins own file writes");
            data.Undo();
            ModAssert.True(data.PatchOf(Map) == null, "undo removes the complete point movement patch");
            data.Redo();
            var wire = data.ChangedPatch().Value.Text;
            var reload = Empty();
            reload.AttachForeign("patches/map2d/probe.json", wire, typeof(Map2dDefinition), source.Entities(), source.Resolve);
            var restored = new Map2dDocument(reload).Read(Map, 4, 3);
            ModAssert.Equal(JsonSerializer.Serialize(document.Read(Map, 4, 3)), JsonSerializer.Serialize(restored),
                "full sparse patch round trip preserves the effective recipe");
            ModAssert.Equal(42, restored.Extra["Provider"].GetProperty("keep").GetInt32(), "root provider metadata remains");
            ModAssert.Equal(13, restored.Markers[0].Extra["Annotation"].GetProperty("keep").GetInt32(), "point metadata remains");
            ModAssert.Equal(original, source.Written.ToJsonString(), "foreign authored source remains unchanged");
            document.Remove(Map);
            ModAssert.True(data.PatchOf(Map)["$delete"].GetValue<bool>(), "foreign removal is an explicit patch");
            data.Undo();
            ModAssert.True(document.Read(Map, 4, 3).Markers[0].X == 2.25f, "undo deletion restores the previous edited point");
        }

        private static EntityDocument Empty()
        {
            return EntityDocument.Load(Map2dDefinition.ResourceKind, "maps/terrain_maps.json", Array.Empty<(string, string)>(), out _);
        }
    }
}
