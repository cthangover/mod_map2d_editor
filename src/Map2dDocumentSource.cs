namespace Cthangover.Tools.Map2d
{
    using System.Collections.Generic;
    using System.Text.Json.Nodes;
    using Cthangover.Mods.Map2d;
    using Cthangover.Tools.EntityEditor;
    using Cthangover.Tools.Services;

    /// <summary>Retains the edited mod and excluded patch for named dependency lookups; no native or source-file ownership.</summary>
    internal sealed class Map2dDocumentSource
    {
        private readonly string mod;

        private readonly string patch;

        internal Map2dDocumentSource(string mod)
        {
            this.mod = mod;
            patch = ForeignEntities.PatchFile(Map2dDefinition.ResourceKind, mod);
        }

        internal Map2dDocument Load(out List<string> skipped)
        {
            var sources = new List<(string Path, string Text)>();
            foreach (var file in ModFileService.ListFiles(mod, "", ".json"))
            {
                if (!file.StartsWith("patches/", System.StringComparison.OrdinalIgnoreCase))
                {
                    sources.Add((file, ModFileService.ReadText(mod, file)));
                }
            }

            var data = EntityDocument.Load(Map2dDefinition.ResourceKind, "maps/terrain_maps.json", sources, out skipped);
            data.AttachForeign(patch, ModFileService.ReadText(mod, patch), typeof(Map2dDefinition),
                ForeignEntities.Scan(Map2dDefinition.ResourceKind, mod), Resolve);
            return new Map2dDocument(data);
        }

        private JsonObject Resolve(string id)
        {
            return ForeignEntities.BaseWithout(Map2dDefinition.ResourceKind, id, mod, patch);
        }
    }
}
