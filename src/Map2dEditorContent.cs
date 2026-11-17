namespace Cthangover.Tools.Map2d
{
    using Cthangover.Mods.Interface.Base.Lists;
    using Cthangover.Mods.Map2d;
    using Godot;

    /// <summary>Utility layout with full-width labels; all normal map and point authoring is available as forms.</summary>
    internal sealed partial class Map2dEditorContent : ModWidget
    {
        internal OptionButton Mod;

        internal OptionButton Map;

        internal LineEdit TitleRu;

        internal LineEdit TitleEn;

        internal OptionButton Image;

        internal SpinBox PixelsPerCell;

        internal Button Generate;

        internal Button Fog;

        internal ItemList Points;

        internal Button AddPoint;

        internal Button RemovePoint;

        internal LineEdit PointRu;

        internal LineEdit PointEn;

        internal SpinBox PointX;

        internal SpinBox PointY;

        internal CheckBox Public;

        internal Button Save;

        internal Button Undo;

        internal Button Redo;

        internal Button Remove;

        internal Label Summary;

        internal Map2dCanvas Canvas;

        protected override void Construct()
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var root = new VBoxContainer();
            AddChild(root);
            root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var toolbar = new HBoxContainer();
            root.AddChild(toolbar);
            Save = Button(toolbar, "TerrainSave", "save");
            Undo = Button(toolbar, "TerrainUndo", "undo");
            Redo = Button(toolbar, "TerrainRedo", "redo");
            Remove = Button(toolbar, "TerrainRemove", "remove");
            Fog = Button(toolbar, "TerrainFog", "fog");
            var split = new HSplitContainer
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                SplitOffset = 340
            };
            root.AddChild(split);
            var scroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(320, 0),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            split.AddChild(scroll);
            var form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            scroll.AddChild(form);
            Mod = Choice(form, "TerrainMod", "mod");
            Map = Choice(form, "TerrainMap", "map");
            TitleRu = Text(form, "TerrainTitleRu", "title_ru");
            TitleEn = Text(form, "TerrainTitleEn", "title_en");
            Image = Choice(form, "TerrainImage", "image");
            PixelsPerCell = Number(form, "TerrainPixels", "pixels", 1, 16, 1);
            PixelsPerCell.Value = 8;
            Generate = Button(form, "TerrainGenerate", "generate");
            Caption(form, "points");
            Points = new ItemList
            {
                Name = "TerrainPoints",
                CustomMinimumSize = new Vector2(0, 150)
            };
            form.AddChild(Points);
            var pointButtons = new HBoxContainer();
            form.AddChild(pointButtons);
            AddPoint = Button(pointButtons, "TerrainAddPoint", "add_point");
            RemovePoint = Button(pointButtons, "TerrainRemovePoint", "remove_point");
            PointRu = Text(form, "TerrainPointRu", "point_ru");
            PointEn = Text(form, "TerrainPointEn", "point_en");
            PointX = Number(form, "TerrainPointX", "point_x", 0, 0, 0.01);
            PointY = Number(form, "TerrainPointY", "point_y", 0, 0, 0.01);
            Public = new CheckBox
            {
                Name = "TerrainPointPublic",
                Text = Tr("map2d/editor/public")
            };
            form.AddChild(Public);
            Caption(form, "placement_hint");
            Summary = new Label
            {
                Name = "TerrainSummary",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            form.AddChild(Summary);
            Canvas = new Map2dCanvas
            {
                Name = "TerrainCanvas",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                ClipContents = true,
                TextureFilter = TextureFilterEnum.Nearest
            };
            split.AddChild(Canvas);
        }

        private static OptionButton Choice(VBoxContainer parent, string name, string key)
        {
            Caption(parent, key);
            var control = new OptionButton
            {
                Name = name,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            parent.AddChild(control);
            return control;
        }

        private static LineEdit Text(VBoxContainer parent, string name, string key)
        {
            Caption(parent, key);
            var control = new LineEdit
            {
                Name = name,
                MaxLength = 128,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            parent.AddChild(control);
            return control;
        }

        private static SpinBox Number(VBoxContainer parent, string name, string key, double min, double max, double step)
        {
            Caption(parent, key);
            var control = new SpinBox
            {
                Name = name,
                MinValue = min,
                MaxValue = max,
                Step = step,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            parent.AddChild(control);
            return control;
        }

        private static Button Button(Node parent, string name, string key)
        {
            var control = new Button
            {
                Name = name,
                Text = TranslationServer.Translate("map2d/editor/" + key)
            };
            parent.AddChild(control);
            return control;
        }

        private static void Caption(Node parent, string key)
        {
            var text = TranslationServer.Translate("map2d/editor/" + key);
            parent.AddChild(new Label
            {
                Text = text,
                TooltipText = text,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            });
        }
    }
}
