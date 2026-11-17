namespace Cthangover.Tools.Map2d
{
    using Cthangover.Core.UI.Tool;

    /// <summary>Optional toolbox entry; the shared tool system owns its icon and window lifetime.</summary>
    public sealed class Map2dEditorButton : IToolBoxButton
    {
        /// <summary>Matches the provider's registry identity.</summary>
        public string ToolId => "map2d_editor";

        /// <summary>Empty selects the common tool icon, with no editor-owned texture.</summary>
        public string IconPath => "";

        /// <summary>Localized label also used for the toolbox tooltip.</summary>
        public string LocaleKey => "map2d/editor/title";

        /// <summary>Authoring remains available outside gameplay while the editor is loaded.</summary>
        public bool IsVisible()
        {
            return true;
        }
    }
}
