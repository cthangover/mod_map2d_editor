namespace Cthangover.Tools.Map2d
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Mods.Fog2d;
    using Cthangover.Mods.Map2d;
    using Cthangover.Mods.Interface.Tool;
    using Godot;

    /// <summary>
    /// Image selection and explicit generation retain independent native ownership. Generated previews are bounded,
    /// use fresh asset names and remain in memory until Save; closing a window cancels and drains its pending export.
    /// </summary>
    public sealed partial class Map2dEditorWindow
    {
        private void FillImages(string selected)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Files.Collect(Map2dDefinition.ImageGroup).Keys)
            {
                if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    seen.Add(file[..^4]);
                }
            }

            foreach (var id in generated.Keys)
            {
                seen.Add(id);
            }

            if (!string.IsNullOrEmpty(selected))
            {
                seen.Add(selected);
            }

            images.Clear();
            images.AddRange(seen);
            images.Sort(StringComparer.OrdinalIgnoreCase);
            images.Insert(0, "");
            content.Image.Clear();
            content.Image.AddItem(Tr("map2d/editor/choose_layer"));
            for (var i = 1; i < images.Count; i++)
            {
                content.Image.AddItem(images[i]);
            }

            content.Image.Select(Math.Max(0, images.IndexOf(selected ?? "")));
        }

        private void ChooseImage(long index)
        {
            if (filling || map == null || index <= 0 || index >= images.Count)
            {
                return;
            }

            try
            {
                var id = images[(int)index];
                int width;
                int height;
                if (generated.TryGetValue(id, out var pending))
                {
                    width = pending.Width;
                    height = pending.Height;
                }
                else
                {
                    using var image = Map2dImage.Read(id);
                    width = image.GetWidth();
                    height = image.GetHeight();
                }

                Edited(Document.Layer(map.ID, map.Width, map.Height, id, width, height));
            }
            catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException)
            {
                SetStatus(Tr("map2d/editor/layer_failed"), ToolStatusKind.Error);
            }
        }

        private async void Generate()
        {
            if (map == null || generationTask != null)
            {
                return;
            }

            var scale = (int)content.PixelsPerCell.Value;
            long bytes = (long)map.Width * scale * map.Height * scale * 4;
            foreach (var value in generated.Values)
            {
                bytes += (long)value.Width * value.Height * 4;
            }

            if (generated.Count >= 8 || bytes > 67108864)
            {
                SetStatus(Tr("map2d/editor/generation_budget"), ToolStatusKind.Error);
                return;
            }

            var target = map;
            var document = Document;
            var mod = ModId;
            content.Generate.Disabled = true;
            content.Save.Disabled = true;
            SetStatus(Tr("map2d/editor/generating"));
            cancellation = new CancellationTokenSource();
            try
            {
                generationTask = Map2dLayerGenerator.Generate(target, scale, cancellation.Token);
                var result = await generationTask;
                if (closed || ApplicationExit.IsQuitting || Document != document || map != target || ModId != mod)
                {
                    result.Dispose();
                    return;
                }

                var id = FreeImageName();
                generated.Add(id, result);
                try
                {
                    Edited(Document.Layer(map.ID, map.Width, map.Height, id, result.Width, result.Height));
                    RefreshForms();
                    SetStatus(Tr("map2d/editor/generated"), ToolStatusKind.Ok);
                }
                catch
                {
                    content.Canvas.Clear();
                    generated.Remove(id);
                    result.Dispose();
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                if (!closed && !ApplicationExit.IsQuitting)
                {
                    SetStatus(Tr("map2d/editor/generation_cancelled"));
                }
            }
            catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException)
            {
                if (!closed && !ApplicationExit.IsQuitting)
                {
                    SetStatus(Tr("map2d/editor/layer_failed"), ToolStatusKind.Error);
                }
            }
            finally
            {
                cancellation?.Dispose();
                cancellation = null;
                generationTask = null;
                if (closed)
                {
                    ApplicationExit.Preparing -= PrepareExit;
                }
                else if (!ApplicationExit.IsQuitting)
                {
                    content.Generate.Disabled = false;
                    content.Save.Disabled = false;
                }
            }
        }

        private string FreeImageName()
        {
            var files = Files.Collect(Map2dDefinition.ImageGroup);
            var number = 1;
            var prefix = ModId + "/" + map.ID + "_overview_";
            while (files.ContainsKey(prefix + number + ".png") || generated.ContainsKey(prefix + number))
            {
                number++;
            }

            return prefix + number;
        }

        private void ShowPreview()
        {
            if (map == null || Document == null)
            {
                content.Canvas.Clear();
                return;
            }

            try
            {
                var record = Document.Data.FindAny(map.ID);
                if (Document.Data.IsDeleted(record))
                {
                    content.Canvas.Clear();
                    content.Summary.Text = Tr("map2d/editor/removed");
                    return;
                }

                var definition = Document.Read(map.ID, map.Width, map.Height);
                if (string.IsNullOrEmpty(definition.Image))
                {
                    content.Canvas.Clear();
                    content.Summary.Text = Tr("map2d/editor/choose_layer");
                    return;
                }

                generated.TryGetValue(definition.Image, out var pending);
                Entities.Collect<Fog2dProfile>(required: false).TryGetValue(map.ID, out var profile);
                content.Canvas.SetPreview(definition, pending?.Texture, profile?.InitialExplored);
                content.Summary.Text = string.Format(Tr("map2d/editor/summary"), definition.PixelsX, definition.PixelsY,
                    definition.Markers.Count);
            }
            catch (Exception error) when (error is ArgumentException or IOException or System.Text.Json.JsonException
                or InvalidOperationException)
            {
                content.Canvas.Clear();
                content.Summary.Text = Tr("map2d/editor/layer_failed");
            }
        }

        private void ReleaseGenerated()
        {
            if (GodotObject.IsInstanceValid(content.Canvas))
            {
                content.Canvas.Clear();
            }

            foreach (var layer in generated.Values)
            {
                layer.Dispose();
            }

            generated.Clear();
        }

        protected override void Cleanup()
        {
            closed = true;
            cancellation?.Cancel();
            ReleaseGenerated();
            if (generationTask == null)
            {
                ApplicationExit.Preparing -= PrepareExit;
            }
        }

        private async Task PrepareExit(SceneTree tree)
        {
            cancellation?.Cancel();
            ReleaseGenerated();
            var pending = generationTask;
            if (pending != null)
            {
                try
                {
                    await pending;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }
}
