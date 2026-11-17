namespace Cthangover.Tools.Map2d.Tests.Gui
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using Cthangover.Core.Localization;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Settings;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.UI;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Fog2d;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Interface.View;
    using Cthangover.Mods.Map2d;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Interface.Shell;
    using Cthangover.Tools.Fog2d;
    using Cthangover.Tools.RpgEditor;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>Authoring uses ordinary forms and clicks; the generated map is then played in both projections and saved to disk.</summary>
    public sealed class Map2dAuthorGuiTests
    {
        private const string Slot = "slot_9924";

        [ModTest("map_author/close_cancels_window_owned_generation", TimeoutSeconds = 60)]
        private static async Task CloseDuringGeneration(ModTestContext context)
        {
            var root = ((SceneTree)Godot.Engine.GetMainLoop()).Root;
            var before = ViewportCount(root);
            var outputs = ModFileService.ListFiles("rpg_plus_game", "terrain_maps", ".png").Count;
            var window = ToolWindow.Open<Map2dEditorWindow>();
            window.Open("rpg_plus_game", "map_layer_probe");
            await Frames(2);
            Press(Find<Button>(window, "TerrainGenerate"));
            window.EmitSignal(Window.SignalName.CloseRequested);
            await Frames(5);
            ModAssert.False(GodotObject.IsInstanceValid(window), "ordinary close retires a window during generation");
            ModAssert.Equal(before, ViewportCount(root), "cancelled window generation releases its isolated viewport");
            ModAssert.Equal(outputs, ModFileService.ListFiles("rpg_plus_game", "terrain_maps", ".png").Count,
                "closing an unsaved generated preview publishes no PNG output");
        }

        [ModTest("map_author/forms_to_gameplay_both_locales_projections_and_real_save", TimeoutSeconds = 180)]
        private static async Task Full(ModTestContext context)
        {
            var locale = TranslationServer.GetLocale();
            var memory = ExplorationMemory.Snapshot();
            var history = Fog2dHistory.Snapshot();
            var source = ModFileService.ReadText("rpg_plus_game", "maps/rpg_village.json");
            var tree = (SceneTree)Godot.Engine.GetMainLoop();
            var runnerScene = tree.CurrentScene;
            using var files = new Map2dAuthorFiles();
            var locales = new[] { "ru", "en" };
            try
            {
                await Frames(5);
                foreach (var language in locales)
                {
                    files.Prepare();
                    LocaleLoader.LoadLocale(language);
                    await Author(language);
                }

                ModAssert.Equal(JsonSerializer.Serialize(memory), JsonSerializer.Serialize(ExplorationMemory.Snapshot()),
                    "authoring forms, generation and fog painting do not explore the running game");
                ModAssert.Equal(JsonSerializer.Serialize(history), JsonSerializer.Serialize(Fog2dHistory.Snapshot()),
                    "authoring never observes the live environment");
                ModAssert.Equal(source, ModFileService.ReadText("rpg_plus_game", "maps/rpg_village.json"),
                    "authoring preserves the original RPG map bytes");
                foreach (var language in locales)
                {
                    LocaleLoader.LoadLocale(language);
                    if (tree.CurrentScene == runnerScene)
                    {
                        // Keep the launcher-owned test cancellation scope alive during the first real shell load.
                        // Later transitions replace only gameplay shells; the original runner stays owned by the root.
                        runnerScene.ProcessMode = Node.ProcessModeEnum.Disabled;
                        if (runnerScene is CanvasItem canvas)
                        {
                            canvas.Hide();
                        }

                        tree.CurrentScene = null;
                    }

                    await EnterWorld();
                    await CaptureEditorInGame(language);
                    var renderers = new[] { "topdown", "isometric" };
                    foreach (var renderer in renderers)
                    {
                        await Play(language, renderer);
                    }
                }
            }
            finally
            {
                SendKey(Key.D, false);
                SendKey(Key.Right, false);
                SendKey(Key.Down, false);
                SendKey(Key.Left, false);
                SendKey(Key.Up, false);
                if (UiHost.Instance != null && UiHost.Instance.TryGetInstance("terrain_map2d", out var panel)
                    && panel.IsVisibleInTree())
                {
                    UiHost.Instance.Toggle("terrain_map2d");
                }

                SaveService.DeleteSave(Slot);
                files.Restore();
                ExplorationMemory.Restore(memory);
                Fog2dHistory.Restore(history);
                LocaleLoader.LoadLocale(locale);
            }
        }

        private static async Task Author(string locale)
        {
            var window = ToolWindow.Open<Map2dEditorWindow>();
            Fog2dEditorWindow fog = null;
            try
            {
                window.Size = new Vector2I(1500, 900);
                window.Position = (window.GetTree().Root.Size - window.Size) / 2;
                window.Open("rpg_plus_game", "rpg_village");
                await Frames(3);
                SetText(Find<LineEdit>(window, "TerrainTitleRu"), "Деревня");
                SetText(Find<LineEdit>(window, "TerrainTitleEn"), "Village");
                Press(Find<Button>(window, "TerrainGenerate"));
                for (var i = 0; i < 300 && Find<Button>(window, "TerrainGenerate").Disabled; i++)
                {
                    await Frames(1);
                }

                var recipe = window.Document.Read("rpg_village", 64, 42);
                ModAssert.True(recipe.PixelsX == 512 && recipe.PixelsY == 336 && !string.IsNullOrEmpty(recipe.Image),
                    "Generate form prepares the actual map at eight pixels per cell");
                Press(Find<Button>(window, "TerrainAddPoint"));
                SetText(Find<LineEdit>(window, "TerrainPointRu"), "Вход");
                SetText(Find<LineEdit>(window, "TerrainPointEn"), "Entry");
                Find<CheckBox>(window, "TerrainPointPublic").ButtonPressed = true;
                var before = JsonSerializer.Serialize(window.Document.Read("rpg_village", 64, 42));
                var canvas = Find<Map2dCanvas>(window, "TerrainCanvas");
                using (var input = new InputEventMouseButton
                {
                    Position = canvas.MapPoint(2.5f, 2.5f),
                    ButtonIndex = MouseButton.Left,
                    Pressed = true
                })
                {
                    canvas.EmitSignal(Control.SignalName.GuiInput, input);
                }

                var after = JsonSerializer.Serialize(window.Document.Read("rpg_village", 64, 42));
                ModAssert.False(before == after, "a real canvas click moves the selected point");
                Press(Find<Button>(window, "TerrainUndo"));
                ModAssert.Equal(before, JsonSerializer.Serialize(window.Document.Read("rpg_village", 64, 42)),
                    "one undo restores the exact point placement");
                Press(Find<Button>(window, "TerrainRedo"));
                ModAssert.Equal(after, JsonSerializer.Serialize(window.Document.Read("rpg_village", 64, 42)),
                    "one redo restores the exact placement and titles");
                var destinationBefore = ModFileService.ReadText("rpg_plus_game", "maps/terrain_maps.json");
                Press(Find<Button>(window, "TerrainSave"));
                ModAssert.Equal(destinationBefore, ModFileService.ReadText("rpg_plus_game", "maps/terrain_maps.json"),
                    "missing fog profile prevents publication before any recipe write");
                Press(Find<Button>(window, "TerrainFog"));
                await Frames(3);
                fog = FindFog();
                ModAssert.NotNull(fog, "Configure exploration opens the ordinary fog editor");
                ModAssert.Equal("rpg_village", fog.SelectedMap, "the linked form selects the same map");
                Find<SpinBox>(fog, "FogRadius").Value = 5;
                var brush = Find<OptionButton>(fog, "FogBrush");
                brush.Select(1);
                brush.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
                var fogCanvas = Find<RpgEditorCanvas>(fog, "FogCanvas");
                FogPaint(fogCanvas, new Vector2I(0, 0));
                FogPaint(fogCanvas, new Vector2I(1, 0));
                Press(Find<Button>(fog, "FogSave"));
                ModAssert.Equal(2, fog.Document.Read("rpg_village").InitialExplored.Count,
                    "initial exploration is authored through the linked brush form");
                fog.EmitSignal(Window.SignalName.CloseRequested);
                await Frames(3);
                fog = null;
                Press(Find<Button>(window, "TerrainSave"));
                await Frames(3);
                recipe = window.Document.Read("rpg_village", 64, 42);
                var expected = JsonSerializer.Serialize(recipe);
                ModAssert.Equal(0, window.Document.Data.ChangedFiles().Count, "successful Save establishes a clean file baseline");
                var manifest = JsonNode.Parse(ModFileService.ReadText("rpg_plus_game", "manifest.json"));
                ModAssert.True(manifest["depends"].ToJsonString().Contains("map2d_base"), "ordinary gameplay gets its runtime dependency");
                var imagePath = ModFileService.GetWritablePath("rpg_plus_game", "terrain_maps/" + recipe.Image + ".png");
                ModAssert.True(File.Exists(imagePath), "explicit Save publishes the prepared PNG");
                window.Open("rpg_plus_game", "rpg_village");
                await Frames(3);
                ModAssert.Equal(expected, JsonSerializer.Serialize(window.Document.Read("rpg_village", 64, 42)),
                    "complete form-authored recipe survives file reopen");
                var point = window.Document.Read("rpg_village", 64, 42).Markers[0];
                ModAssert.True(Math.Abs(point.X - 2.5f) < 0.00001f && Math.Abs(point.Y - 2.5f) < 0.00001f
                    && point.TitleRu == "Вход" && point.TitleEn == "Entry" && point.AlwaysVisible,
                    "point names, exact clicked coordinates and explicit disclosure survive");
                ModAssert.Equal(locale == "en" ? "Entry" : "Вход", Find<ItemList>(window, "TerrainPoints").GetItemText(0),
                    "point list chooses the active locale while retaining both authored titles");
                Capture("author_" + locale + "_overview");
                Find<ItemList>(window, "TerrainPoints").Select(0);
                Find<ItemList>(window, "TerrainPoints").EmitSignal(ItemList.SignalName.ItemSelected, 0L);
                Scroll(Find<CheckBox>(window, "TerrainPointPublic"));
                await Frames(2);
                Capture("author_" + locale + "_points");
                Press(Find<Button>(window, "TerrainRemove"));
                Press(Find<Button>(window, "TerrainSave"));
                ModAssert.True(window.Document.Data.FindAny("rpg_village") == null,
                    "deleting and saving a map recipe never creates it again");
                Press(Find<Button>(window, "TerrainUndo"));
                Press(Find<Button>(window, "TerrainSave"));
                ModAssert.Equal(expected, JsonSerializer.Serialize(window.Document.Read("rpg_village", 64, 42)),
                    "undo after save restores the recipe and its retained generated image");
            }
            finally
            {
                fog?.QueueFree();
                window.QueueFree();
                await Frames(3);
            }
        }

        private static async Task EnterWorld()
        {
            var root = ((SceneTree)Godot.Engine.GetMainLoop()).Root;
            root.GetNode<SceneManager>("/root/SceneManager").PendingSceneName = "rpg_village";
            root.GetNode<GodotSceneService>("/root/GodotSceneService").LoadShell();
            for (var i = 0; i < 600; i++)
            {
                if (RpgWorldHost.Current?.View?.Map?.ID == "rpg_village" && !GodotSceneService.IsTransitioning
                    && ShellSession.Current?.Ready == true)
                {
                    RpgInput.FindDialogBox()?.Abort();
                    await Frames(4);
                    return;
                }

                await Frames(1);
            }

            ModAssert.True(false, "normal 2D shell enters the authored map");
        }

        private static async Task CaptureEditorInGame(string locale)
        {
            var before = JsonSerializer.Serialize(ExplorationMemory.Snapshot());
            var window = ToolWindow.Open<Map2dEditorWindow>();
            try
            {
                window.Size = new Vector2I(1500, 900);
                window.Position = (window.GetTree().Root.Size - window.Size) / 2;
                window.Open("rpg_plus_game", "rpg_village");
                await Frames(3);
                Capture("author_game_" + locale + "_overview");
                Find<ItemList>(window, "TerrainPoints").Select(0);
                Find<ItemList>(window, "TerrainPoints").EmitSignal(ItemList.SignalName.ItemSelected, 0L);
                Scroll(Find<CheckBox>(window, "TerrainPointPublic"));
                await Frames(2);
                Capture("author_game_" + locale + "_points");
                ModAssert.Equal(before, JsonSerializer.Serialize(ExplorationMemory.Snapshot()),
                    "inspecting the prepared map inside gameplay does not change explored knowledge");
            }
            finally
            {
                window.QueueFree();
                await Frames(3);
            }
        }

        private static async Task Play(string locale, string renderer)
        {
            var host = RpgWorldHost.Current;
            ModAssert.True(host.View.SetRenderer(renderer), "actual gameplay uses " + renderer);
            await Frames(3);
            var before = host.Session.Player.Position;
            var shell = ShellSession.Current;
            using (var other = shell.Acquire(ShellBlock.Simulation))
            {
                var button = TerrainButton(((SceneTree)Godot.Engine.GetMainLoop()).Root);
                ModAssert.NotNull(button, "declarative terrain map entry creates its actual toolbox button");
                Press(button);
                await Frames(3);
                ModAssert.True(UiHost.Instance.TryGetInstance("terrain_map2d", out var panel) && panel is Map2dWidget,
                    "toolbar opens the actual gameplay prefab");
                var canvas = Find<Map2dCanvas>(panel, "Map");
                ModAssert.True(canvas.MapRectangle.Size.X > 0 && canvas.LayerLoads > 0,
                    "runtime panel decodes the authored prepared layer");
                ModAssert.True(shell.SimulationPaused && RpgInput.IsWalkBlocked(host),
                    "map blocks normal world input and simulation policy");
                SendKey(Key.D, true);
                await Frames(5);
                SendKey(Key.D, false);
                ModAssert.True(host.Session.Player.Position == before, "real held key cannot walk through the modal map");
                ModAssert.True(ReferenceEquals(Fog2dWorld.MemoryOf(host.View), Map2dWorld.ModelOf(host.View).Memory),
                    "panel domain reads the exact world fog handle");
                Capture("game_" + locale + "_" + renderer + "_map");
                Press(Find<Button>(panel, "Close"));
                await Frames(2);
                ModAssert.False(panel.IsVisibleInTree(), "ordinary Close hides the map");
                ModAssert.True(shell.SimulationPaused, "closing the map cannot cancel another owner's claim");
            }

            shell.Refresh();
            await Frames(2);
            ModAssert.False(RpgInput.IsWalkBlocked(host), "closing the panel restores ordinary keyboard input");
            Capture("game_" + locale + "_" + renderer + "_world");
            var origin = host.Session.Player.Cell;
            var moves = new[]
            {
                (Key.Right, new Vector2I(1, 0)),
                (Key.Down, new Vector2I(0, 1)),
                (Key.Left, new Vector2I(-1, 0)),
                (Key.Up, new Vector2I(0, -1))
            };
            var chosen = Key.None;
            foreach (var move in moves)
            {
                if (host.Session.CanEnter(origin + move.Item2))
                {
                    chosen = move.Item1;
                    break;
                }
            }

            ModAssert.True(chosen != Key.None, "fixture has a walkable adjacent cell");
            SendKey(chosen, true);
            for (var i = 0; i < 90 && host.Session.Player.Cell == origin; i++)
            {
                await Frames(1);
            }

            SendKey(chosen, false);
            for (var i = 0; i < 90 && host.Session.Player.IsMoving; i++)
            {
                var tree = (SceneTree)Godot.Engine.GetMainLoop();
                await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            }

            ModAssert.False(host.Session.Player.IsMoving, "walking reaches its reserved cell before saving");
            ModAssert.False(host.Session.Player.Cell == origin, "real keyboard movement resumes after closing the map");
            var savedCell = host.Session.Player.Cell;
            var known = JsonSerializer.Serialize(ExplorationMemory.Snapshot());
            SaveService.Save(Slot, rememberSession: false);
            host.PlacePlayer(host.Session.Map.Width - 1, host.Session.Map.Height - 1, RpgDirection.Up);
            ModAssert.True(SaveService.Load(Slot), "actual disk save reloads successfully");
            host.Mount("rpg_village");
            await Frames(4);
            ModAssert.True(host.Session.Player.Cell == savedCell,
                "ordinary remount uses the loaded player cell: saved=" + savedCell + " actual=" + host.Session.Player.Cell
                + " state=" + RpgState.Instance.Data.X + "," + RpgState.Instance.Data.Y
                + " moving=" + host.Session.Player.IsMoving + " held=" + RpgInput.HeldDirection());
            ModAssert.Equal(known, JsonSerializer.Serialize(ExplorationMemory.Snapshot()),
                "disk load restores the exact explored knowledge");
        }

        private static Button TerrainButton(Node node)
        {
            if (node is Button button && button.TooltipText == TranslationServer.Translate("map2d/title"))
            {
                return button;
            }

            foreach (var child in node.GetChildren())
            {
                var found = TerrainButton(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Fog2dEditorWindow FindFog()
        {
            foreach (var child in ((SceneTree)Godot.Engine.GetMainLoop()).Root.GetChildren())
            {
                if (child is Fog2dEditorWindow window)
                {
                    return window;
                }
            }

            return null;
        }

        private static int ViewportCount(Node root)
        {
            var count = 0;
            foreach (var child in root.GetChildren())
            {
                if (child is SubViewport)
                {
                    count++;
                }
            }

            return count;
        }

        private static T Find<T>(Node root, string name) where T : Node
        {
            var value = root.FindChild(name, true, false) as T;
            ModAssert.NotNull(value, "ordinary GUI has node " + name);
            return value;
        }

        private static void SetText(LineEdit control, string text)
        {
            Scroll(control);
            control.Text = text;
            control.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        private static void Press(Button button)
        {
            Scroll(button);
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        private static void Scroll(Control control)
        {
            var parent = control.GetParent();
            while (parent != null)
            {
                if (parent is ScrollContainer scroll)
                {
                    scroll.EnsureControlVisible(control);
                    return;
                }

                parent = parent.GetParent();
            }
        }

        private static void FogPaint(RpgEditorCanvas canvas, Vector2I cell)
        {
            using var press = new InputEventMouseButton
            {
                Position = canvas.CanvasPointOf(cell),
                ButtonIndex = MouseButton.Left,
                Pressed = true
            };
            canvas._GuiInput(press);
            press.Pressed = false;
            canvas._GuiInput(press);
        }

        private static void SendKey(Key key, bool pressed)
        {
            using var input = new InputEventKey
            {
                PhysicalKeycode = key,
                Keycode = key,
                Pressed = pressed
            };
            Input.ParseInputEvent(input);
        }

        private static void Capture(string name)
        {
            var folder = System.Environment.GetEnvironmentVariable("MAP2D_EDITOR_SHOTS");
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                ModAssert.True(ViewportCapture.SavePng(((SceneTree)Godot.Engine.GetMainLoop()).Root,
                    Path.Combine(folder, name + ".png")), "capture preserves the normal HDR pipeline");
            }
        }

        private static async Task Frames(int count)
        {
            var tree = (SceneTree)Godot.Engine.GetMainLoop();
            for (var i = 0; i < count; i++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
    }
}
