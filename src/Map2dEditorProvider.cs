namespace Cthangover.Tools.Map2d
{
    using Cthangover.Core.UI.Tool;
    using Godot;

    /// <summary>Registers the optional authoring tool without making runtime fog depend on any editor.</summary>
    public sealed class Map2dEditorProvider : IToolProvider
    {
        /// <summary>Stable registry key shared with the toolbar entry.</summary>
        public string Id => "map2d_editor";

        /// <summary>Title key resolved through the ordinary ru/en tool translations.</summary>
        public string LocaleKey => "map2d/editor/title";

        /// <summary>Transfers a fresh native window to the tool manager's ownership.</summary>
        public Window CreateWindow()
        {
            return new Map2dEditorWindow();
        }
    }
}
