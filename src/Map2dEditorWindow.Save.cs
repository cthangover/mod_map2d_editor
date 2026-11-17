namespace Cthangover.Tools.Map2d
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using Cthangover.Core.Events;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Mods.Fog2d;
    using Cthangover.Mods.Map2d;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Rpg;
    using Cthangover.Tools.EntityEditor;
    using Cthangover.Tools.Services;

    /// <summary>
    /// Publication validates the raw recipe, existing fog profile and prepared image before writing. Generated
    /// PNGs use reserved fresh paths; completed outputs survive document undo without overwriting source artwork.
    /// </summary>
    public sealed partial class Map2dEditorWindow
    {
        /// <summary>
        /// Writes prepared outputs, own recipes or sparse foreign patches and the runtime dependency. A running
        /// generation or invalid map prevents publication; successful Save refreshes resources without revealing terrain.
        /// </summary>
        public bool Save()
        {
            if (Document == null || map == null || generationTask != null)
            {
                return false;
            }

            if (!ValidDocument())
            {
                return false;
            }

            var dependency = ModDependencies.Insert(ModFileService.ReadText(ModId, "manifest.json"), "map2d_base",
                DependsOf, out var runtimeManifest);
            if (dependency is ModDependencyOutcome.Cycle or ModDependencyOutcome.NotWritable)
            {
                SetStatus(Tr("map2d/editor/dependency"), ToolStatusKind.Error);
                return false;
            }

            var changed = Document.Data.ChangedFiles();
            var patch = Document.Data.ChangedPatch();
            try
            {
                foreach (var output in generated)
                {
                    var path = ModFileService.GetWritablePath(ModId, Map2dDefinition.ImageGroup + "/" + output.Key + ".png");
                    if (path == null || File.Exists(path) && !File.ReadAllBytes(path).AsSpan().SequenceEqual(output.Value.Png.Span))
                    {
                        return WriteFailed();
                    }
                }

                foreach (var output in generated)
                {
                    var path = ModFileService.GetWritablePath(ModId, Map2dDefinition.ImageGroup + "/" + output.Key + ".png");
                    if (!File.Exists(path))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, output.Value.Png.Span);
                    }
                }

                foreach (var file in changed)
                {
                    if (!ModFileService.WriteText(ModId, file.Path, file.Text))
                    {
                        return WriteFailed();
                    }
                }

                if (patch is { } difference)
                {
                    var written = difference.Text == null ? ModFileService.DeleteFile(ModId, difference.Path)
                        : ModFileService.WriteText(ModId, difference.Path, difference.Text);
                    if (!written)
                    {
                        return WriteFailed();
                    }
                }

                if (dependency == ModDependencyOutcome.Added
                    && !ModFileService.WriteText(ModId, "manifest.json", runtimeManifest))
                {
                    return WriteFailed();
                }

                ModManager.Instance.GetResolver<FileListResolver>()?.ClearCache();
                var added = ModDependencies.EnsureReferenced(ModId, References());
                if (dependency == ModDependencyOutcome.Added)
                {
                    added.Add("map2d_base");
                }

                Document.Data.MarkSaved();
                MarkClean();
                ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache();
                ModManager.Instance.GetResolver<PatchResolver>()?.ClearCache();
                ModManager.Instance.GetResolver<FileListResolver>()?.ClearCache();
                ReleaseGenerated();
                Map2dWorld.RefreshMaps();
                ModEventBus.Publish(new EntitiesSavedEvent
                {
                    Kind = Map2dDefinition.ResourceKind,
                    ModId = ModId
                });
                RefreshForms();
                SetStatus(Tr("map2d/editor/saved") + " " + ModDependencies.Notice(added), ToolStatusKind.Ok);
                return true;
            }
            catch (IOException)
            {
                return WriteFailed();
            }
            catch (UnauthorizedAccessException)
            {
                return WriteFailed();
            }
        }

        private bool ValidDocument()
        {
            if (Document.Data.Validate().Count > 0)
            {
                return Invalid();
            }

            if (Document.Data.FindAny(map.ID) == null && Document.Data.ChangedFiles().Count == 0
                && Document.Data.ChangedPatch() == null)
            {
                SetStatus(Tr("map2d/editor/choose_layer"), ToolStatusKind.Error);
                return false;
            }

            var records = new List<EntityRecord>(Document.Data.Records);
            records.AddRange(Document.Data.Foreign);
            foreach (var record in records)
            {
                if (Document.Data.IsDeleted(record) || record.IsForeign && Document.Data.PatchOf(record.Id) == null)
                {
                    continue;
                }

                if (!ValidRecipe(record.Id))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ValidRecipe(string id)
        {
            try
            {
                var source = RpgWorldHost.MapOf(id);
                if (source == null)
                {
                    return Invalid();
                }

                var recipe = Document.Read(id, source.Width, source.Height);
                recipe.Validate();
                if (recipe.Width != source.Width || recipe.Height != source.Height)
                {
                    return Invalid();
                }

                if (!Entities.Collect<Fog2dProfile>(required: false).TryGetValue(id, out var fog))
                {
                    SetStatus(Tr("map2d/editor/need_fog"), ToolStatusKind.Error);
                    return false;
                }

                fog.Validate(source.Width, source.Height);
                if (generated.TryGetValue(recipe.Image, out var pending))
                {
                    return pending.Width == recipe.PixelsX && pending.Height == recipe.PixelsY || Invalid();
                }

                using var image = Map2dImage.Read(recipe.Image);
                return image.GetWidth() == recipe.PixelsX && image.GetHeight() == recipe.PixelsY || Invalid();
            }
            catch (Exception error) when (error is ArgumentException or IOException or JsonException or InvalidOperationException)
            {
                return Invalid();
            }
        }

        private List<(string Kind, string Id)> References()
        {
            var references = new List<(string Kind, string Id)>();
            var records = new List<EntityRecord>(Document.Data.Records);
            records.AddRange(Document.Data.Foreign);
            foreach (var record in records)
            {
                if (Document.Data.IsDeleted(record) || record.IsForeign && Document.Data.PatchOf(record.Id) == null)
                {
                    continue;
                }

                references.Add(("rpg_map", record.Id));
                references.Add((Fog2dProfile.ResourceKind, record.Id));
                var recipe = Document.Read(record.Id, 1, 1);
                references.Add((ModDependencies.PictureKind + Map2dDefinition.ImageGroup, recipe.Image));
            }

            return references;
        }

        private static IEnumerable<string> DependsOf(string id)
        {
            return ModManager.Instance.GetMod(id) is ModInfo info ? info.Manifest?.DependencyIds ?? new List<string>()
                : Array.Empty<string>();
        }

        private bool Invalid()
        {
            SetStatus(Tr("map2d/editor/invalid"), ToolStatusKind.Error);
            return false;
        }

        private bool WriteFailed()
        {
            SetStatus(Tr("map2d/editor/write_failed"), ToolStatusKind.Error);
            return false;
        }

        private void SavePressed()
        {
            Save();
        }
    }
}
