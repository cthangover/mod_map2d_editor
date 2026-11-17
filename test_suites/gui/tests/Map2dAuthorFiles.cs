namespace Cthangover.Tools.Map2d.Tests.Gui
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Mods.Fog2d;
    using Cthangover.Mods.Map2d;
    using Cthangover.Tools.Services;

    /// <summary>Owns exact private-copy authoring file restoration and deletes only newly generated layer outputs.</summary>
    internal sealed class Map2dAuthorFiles : IDisposable
    {
        private const string Owner = "rpg_plus_game";

        private readonly Dictionary<string, string> originals = new();

        private readonly HashSet<string> pictures = new(StringComparer.OrdinalIgnoreCase);

        internal Map2dAuthorFiles()
        {
            var known = new[]
            {
                "manifest.json",
                "maps/terrain_maps.json",
                "patches/map2d/rpg_plus_game.json",
                "profiles/visibility.json",
                "patches/fog2d_profile/rpg_plus_game.json"
            };
            foreach (var path in known)
            {
                originals[path] = ModFileService.ReadText(Owner, path);
            }

            foreach (var path in ModFileService.ListFiles(Owner, "", ".json"))
            {
                var text = ModFileService.ReadText(Owner, path);
                if (text != null && (text.Contains("\"map2d\"") || text.Contains("\"fog2d_profile\"")))
                {
                    originals[path] = text;
                }
            }

            foreach (var path in ModFileService.ListFiles(Owner, Map2dDefinition.ImageGroup, ".png"))
            {
                pictures.Add(path);
            }
        }

        internal void Prepare()
        {
            Restore();
            foreach (var pair in originals)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                var root = JsonNode.Parse(pair.Value) as JsonObject;
                var kind = root?["Kind"]?.ToString();
                if (kind != Map2dDefinition.ResourceKind && kind != Fog2dProfile.ResourceKind)
                {
                    continue;
                }

                if (root["Items"] is JsonArray items)
                {
                    for (var i = items.Count - 1; i >= 0; i--)
                    {
                        if (items[i]?["Id"]?.ToString() == "rpg_village")
                        {
                            items.RemoveAt(i);
                        }
                    }

                    ModFileService.WriteText(Owner, pair.Key, root.ToJsonString());
                }
            }

            ClearCaches();
        }

        internal void Restore()
        {
            foreach (var pair in originals)
            {
                if (pair.Value == null)
                {
                    ModFileService.DeleteFile(Owner, pair.Key);
                }
                else
                {
                    ModFileService.WriteText(Owner, pair.Key, pair.Value);
                }
            }

            foreach (var path in ModFileService.ListFiles(Owner, Map2dDefinition.ImageGroup, ".png"))
            {
                if (!pictures.Contains(path))
                {
                    ModFileService.DeleteFile(Owner, path);
                }
            }

            ClearCaches();
            Fog2dWorld.RefreshProfiles();
            Map2dWorld.RefreshMaps();
        }

        /// <summary>Restores source text byte-for-byte and the absence of newly authored files on every exit path.</summary>
        public void Dispose()
        {
            Restore();
        }

        private static void ClearCaches()
        {
            ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<PatchResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<FileListResolver>()?.ClearCache();
        }
    }
}
