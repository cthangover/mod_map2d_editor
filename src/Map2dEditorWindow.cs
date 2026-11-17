namespace Cthangover.Tools.Map2d
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Games;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Mods.Map2d;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.Fog2d;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>
    /// Terrain-map forms use one document across selected RPG maps. The ModWidget canvas is detached from gameplay;
    /// generated images remain window-owned until explicit save or closure, and point placement changes one undo step.
    /// </summary>
    public sealed partial class Map2dEditorWindow : ToolWindow
    {
        private readonly Map2dEditorContent content;

        private readonly List<string> mods = new();

        private readonly List<string> maps = new();

        private readonly List<string> images = new();

        private readonly Dictionary<string, Map2dPreparedLayer> generated = new(StringComparer.OrdinalIgnoreCase);

        private RpgMap map;

        private string selectedPoint;

        private string requestedMod;

        private string pendingMod;

        private string pendingMap;

        private bool filling;

        private bool closed;

        private CancellationTokenSource cancellation;

        private Task<Map2dPreparedLayer> generationTask;

        /// <summary>
        /// Borrowed editable recipe document; Save validates referenced maps, fog settings and layers before publication.
        /// </summary>
        public Map2dDocument Document { get; private set; }

        /// <summary>Explicit destination for own recipes, foreign patches and newly generated PNGs.</summary>
        public string ModId { get; private set; }

        /// <summary>Selected RPG map, which may differ from the played scene and from its source mod.</summary>
        public string SelectedMap => map?.ID;

        /// <summary>Builds the utility layout; selection and resource reads wait for native parenting.</summary>
        public Map2dEditorWindow() : base("map2d/editor/title")
        {
            Size = new Vector2I(1100, 760);
            content = new Map2dEditorContent();
            AddChild(content);
            content.EnsureConstructed();
            content.Mod.ItemSelected += ChooseMod;
            content.Map.ItemSelected += ChooseMap;
            content.TitleRu.TextChanged += TitleChanged;
            content.TitleEn.TextChanged += TitleChanged;
            content.Image.ItemSelected += ChooseImage;
            content.Generate.Pressed += Generate;
            content.Fog.Pressed += OpenFog;
            content.Points.ItemSelected += PointSelected;
            content.AddPoint.Pressed += AddPoint;
            content.RemovePoint.Pressed += RemovePoint;
            content.PointRu.TextChanged += PointTextChanged;
            content.PointEn.TextChanged += PointTextChanged;
            content.PointX.ValueChanged += PointNumberChanged;
            content.PointY.ValueChanged += PointNumberChanged;
            content.Public.Toggled += PointPublicChanged;
            content.Canvas.MapClicked += PlacePoint;
            content.Save.Pressed += SavePressed;
            content.Undo.Pressed += Undo;
            content.Redo.Pressed += Redo;
            content.Remove.Pressed += Remove;
            ApplicationExit.Preparing += PrepareExit;
        }

        /// <summary>Defers provider selection until the native window has a valid scene tree and canvas size.</summary>
        public override void _Ready()
        {
            filling = true;
            mods.AddRange(ModFileService.ListWritableMods());
            foreach (var id in mods)
            {
                content.Mod.AddItem(ModManager.Instance.GetMod(id)?.DisplayTitle ?? id);
            }

            maps.AddRange(Entities.Collect<RpgMap>().Keys);
            maps.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var id in maps)
            {
                var definition = RpgWorldHost.MapOf(id);
                var title = string.IsNullOrEmpty(definition.Name) ? id : Tr(definition.Name).ToString();
                content.Map.AddItem(title + " — " + id);
            }

            filling = false;
            Open(pendingMod ?? PreferredMod(), pendingMap);
            SetHint(Tr("map2d/editor/hint"));
        }

        /// <summary>Opens an explicit writable destination and map; callers may prepare the selection before showing this window.</summary>
        public void Open(string mod, string mapId = null)
        {
            if (!IsInsideTree())
            {
                pendingMod = mod;
                pendingMap = mapId;
                return;
            }

            if (!ModFileService.IsWritable(mod))
            {
                SetStatus(Tr("map2d/editor/no_mod"), ToolStatusKind.Error);
                return;
            }

            cancellation?.Cancel();
            ReleaseGenerated();
            ModId = mod;
            Document = new Map2dDocumentSource(mod).Load(out var skipped);
            MarkClean();
            filling = true;
            content.Mod.Select(mods.IndexOf(mod));
            var index = mapId == null ? Math.Max(0, content.Map.Selected) : maps.IndexOf(mapId);
            content.Map.Select(index);
            filling = false;
            LoadMap(index);
            if (skipped.Count > 0)
            {
                SetStatus(string.Format(Tr("map2d/editor/skipped"), string.Join(", ", skipped)), ToolStatusKind.Error);
            }
        }

        protected override void OnSaveShortcut()
        {
            Save();
        }

        protected override bool HandleKey(InputEventKey key)
        {
            if (!key.CtrlPressed || key.Keycode is not (Key.Z or Key.Y))
            {
                return false;
            }

            if (key.Keycode == Key.Y || key.ShiftPressed)
            {
                Redo();
            }
            else
            {
                Undo();
            }

            return true;
        }

        private string PreferredMod()
        {
            var preferred = ActiveGame.Id + "_game";
            if (mods.Contains(preferred))
            {
                return preferred;
            }

            foreach (var id in mods)
            {
                if (ModManager.Instance.GetMod(id) is ModInfo info && info.Manifest.Categories.Contains("story"))
                {
                    return id;
                }
            }

            return mods.Count > 0 ? mods[0] : null;
        }

        private void ChooseMod(long index)
        {
            if (!filling && index >= 0 && index < mods.Count)
            {
                requestedMod = mods[(int)index];
                RunDiscardingChanges(ApplyMod, RestoreMod);
            }
        }

        private void ApplyMod()
        {
            Open(requestedMod, SelectedMap);
        }

        private void RestoreMod()
        {
            filling = true;
            content.Mod.Select(mods.IndexOf(ModId));
            filling = false;
        }

        private void ChooseMap(long index)
        {
            if (!filling)
            {
                cancellation?.Cancel();
                LoadMap((int)index);
            }
        }

        private void LoadMap(int index)
        {
            content.Canvas.Clear();
            map = index >= 0 && index < maps.Count ? RpgWorldHost.MapOf(maps[index]) : null;
            selectedPoint = null;
            RefreshForms();
        }

        private void RefreshForms()
        {
            filling = true;
            try
            {
                if (map == null || Document == null)
                {
                    return;
                }

                var definition = Document.Read(map.ID, map.Width, map.Height);
                content.TitleRu.Text = definition.TitleRu;
                content.TitleEn.Text = definition.TitleEn;
                FillImages(definition.Image);
                content.Points.Clear();
                var selected = -1;
                foreach (var point in definition.Markers)
                {
                    var english = TranslationServer.GetLocale().StartsWith("en", StringComparison.OrdinalIgnoreCase);
                    var preferred = english ? point.TitleEn : point.TitleRu;
                    var fallback = english ? point.TitleRu : point.TitleEn;
                    var title = !string.IsNullOrEmpty(preferred) ? preferred
                        : !string.IsNullOrEmpty(fallback) ? fallback : Tr("map2d/editor/unnamed").ToString();
                    content.Points.AddItem(title);
                    if (point.Id == selectedPoint)
                    {
                        selected = content.Points.ItemCount - 1;
                    }
                }

                if (selected >= 0)
                {
                    content.Points.Select(selected);
                }
                else
                {
                    selectedPoint = null;
                }

                content.PointX.MaxValue = Math.Max(0, map.Width - 0.01);
                content.PointY.MaxValue = Math.Max(0, map.Height - 0.01);
                RefreshPoint(definition);
            }
            catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException)
            {
                SetStatus(Tr("map2d/editor/invalid"), ToolStatusKind.Error);
            }
            finally
            {
                filling = false;
            }

            ShowPreview();
            UpdateHistory();
        }

        private void RefreshPoint(Map2dDefinition definition)
        {
            Map2dMarker chosen = null;
            foreach (var point in definition.Markers)
            {
                if (point.Id == selectedPoint)
                {
                    chosen = point;
                }
            }

            content.PointRu.Editable = chosen != null;
            content.PointEn.Editable = chosen != null;
            content.PointX.Editable = chosen != null;
            content.PointY.Editable = chosen != null;
            content.Public.Disabled = chosen == null;
            content.RemovePoint.Disabled = chosen == null;
            content.PointRu.Text = chosen?.TitleRu ?? "";
            content.PointEn.Text = chosen?.TitleEn ?? "";
            content.PointX.Value = chosen?.X ?? 0;
            content.PointY.Value = chosen?.Y ?? 0;
            content.Public.ButtonPressed = chosen?.AlwaysVisible == true;
        }

        private void TitleChanged(string text)
        {
            if (!filling && map != null)
            {
                Edited(Document.Titles(map.ID, map.Width, map.Height, content.TitleRu.Text, content.TitleEn.Text));
            }
        }

        private void PointSelected(long index)
        {
            if (!filling && map != null)
            {
                selectedPoint = Document.Read(map.ID, map.Width, map.Height).Markers[(int)index].Id;
                RefreshForms();
            }
        }

        private void AddPoint()
        {
            if (map != null)
            {
                selectedPoint = Document.AddPoint(map.ID, map.Width, map.Height);
                Edited(true);
                RefreshForms();
            }
        }

        private void RemovePoint()
        {
            if (map != null && selectedPoint != null)
            {
                Edited(Document.RemovePoint(map.ID, selectedPoint));
                selectedPoint = null;
                RefreshForms();
            }
        }

        private void PointTextChanged(string value)
        {
            EditPoint();
        }

        private void PointNumberChanged(double value)
        {
            EditPoint();
        }

        private void PointPublicChanged(bool value)
        {
            EditPoint();
        }

        private void EditPoint()
        {
            if (!filling && map != null && selectedPoint != null)
            {
                Edited(Document.Point(map.ID, map.Width, map.Height, selectedPoint, content.PointRu.Text,
                    content.PointEn.Text, (float)content.PointX.Value, (float)content.PointY.Value, content.Public.ButtonPressed));
                UpdatePointCaption();
            }
        }

        private void UpdatePointCaption()
        {
            var selected = content.Points.GetSelectedItems();
            if (selected.Length > 0)
            {
                var title = !string.IsNullOrEmpty(content.PointRu.Text) ? content.PointRu.Text : content.PointEn.Text;
                content.Points.SetItemText(selected[0], !string.IsNullOrEmpty(title) ? title : Tr("map2d/editor/unnamed"));
            }
        }

        private void PlacePoint(Vector2 cell)
        {
            if (map != null && selectedPoint != null)
            {
                Edited(Document.Point(map.ID, map.Width, map.Height, selectedPoint, content.PointRu.Text,
                    content.PointEn.Text, cell.X, cell.Y, content.Public.ButtonPressed));
                RefreshForms();
            }
        }

        private void OpenFog()
        {
            if (map != null)
            {
                var window = ToolWindow.Open<Fog2dEditorWindow>();
                window.Open(ModId, map.ID);
            }
        }

        private void Remove()
        {
            if (map != null)
            {
                Edited(Document.Remove(map.ID));
                selectedPoint = null;
                RefreshForms();
            }
        }

        private void Undo()
        {
            if (Document?.Data.Undo() == true)
            {
                RefreshForms();
                UpdateDirty();
            }
        }

        private void Redo()
        {
            if (Document?.Data.Redo() == true)
            {
                RefreshForms();
                UpdateDirty();
            }
        }

        private void Edited(bool changed)
        {
            if (changed)
            {
                SetDirty();
                ShowPreview();
                UpdateHistory();
            }
        }

        private void UpdateHistory()
        {
            content.Undo.Disabled = Document?.Data.CanUndo != true;
            content.Redo.Disabled = Document?.Data.CanRedo != true;
        }

        private void UpdateDirty()
        {
            if (Document.Data.ChangedFiles().Count > 0 || Document.Data.ChangedPatch() != null)
            {
                SetDirty();
            }
            else
            {
                MarkClean();
            }
        }
    }
}
