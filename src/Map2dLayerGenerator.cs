namespace Cthangover.Tools.Map2d
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Interface.View;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Rpg.Play;
    using Cthangover.Mods.Rpg.Render;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>
    /// Explicit author export through an isolated ordinary top-down renderer. It frames the complete grid and
    /// authored base scenery, omits actors, and owns every temporary native resource without touching a played world.
    /// </summary>
    public static class Map2dLayerGenerator
    {
        /// <summary>
        /// Returns an owned prepared layer. Call on the main thread and await completion before shutdown; cancel
        /// when its author window closes. Normal HDR and ViewportCapture provide the same colour conversion as gameplay.
        /// </summary>
        public static async Task<Map2dPreparedLayer> Generate(RpgMap map, int pixelsPerCell, CancellationToken cancellation)
        {
            ArgumentNullException.ThrowIfNull(map);
            ExplorationRecord.ValidateDimensions(map.Width, map.Height);
            if (!GameEnvironment.IsRuntime || pixelsPerCell < 1 || pixelsPerCell > 32
                || map.Width * pixelsPerCell > 4096 || map.Height * pixelsPerCell > 4096)
            {
                throw new ArgumentException("Prepared layer needs the native renderer and a bounded pixel scale");
            }

            cancellation.ThrowIfCancellationRequested();
            if (ApplicationExit.IsQuitting)
            {
                throw new OperationCanceledException("Prepared layer cannot start during shutdown", cancellation);
            }

            using var textures = new RpgTextures();
            using var builder = new RpgRenderSceneBuilder(textures.Image, textures.Charset);
            SubViewport viewport = null;
            ITileWorldPresentation presentation = null;
            TileRenderScene scene = null;
            try
            {
                var tree = (SceneTree)Godot.Engine.GetMainLoop();
                viewport = new SubViewport
                {
                    Size = new Vector2I(map.Width * pixelsPerCell, map.Height * pixelsPerCell),
                    Disable3D = true,
                    TransparentBg = false,
                    UseHdr2D = tree.Root.UseHdr2D,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always
                };
                tree.Root.AddChild(viewport);
                var session = new RpgWorldSession(map, new RpgObjectLookup().Find, RpgWorldHost.PlacementsOf(map.ID), Unset);
                scene = builder.Build(map, session.Cells, session.Statics);
                scene.Zoom = (float)pixelsPerCell / scene.TileSize;
                scene.Atmosphere = null;
                scene.Effects = new();
                presentation = TileRendererRegistry.Instance.Resolve("topdown").Create();
                viewport.AddChild(presentation.Root);
                presentation.Mount(scene);
                presentation.Resize(viewport.Size);
                var frame = new TileFrame
                {
                    Focus = new Vector2(map.Width / 2f, map.Height / 2f),
                    Ambient = Colors.White,
                    CameraOnArtwork = false
                };
                foreach (var instance in session.Instances)
                {
                    if (instance.Effective.Form != RpgObjectForm.Sprite || instance.Effective.Hidden)
                    {
                        continue;
                    }

                    var looks = builder.PartLooks(instance.Effective, scene.TileSize);
                    for (var i = 0; i < looks.Count; i++)
                    {
                        frame.Actors.Add(new TileActorPose
                        {
                            Id = "prepared/" + instance.Id + "#" + i,
                            Position = instance.Actor.Position,
                            Look = looks[i],
                            Visible = true
                        });
                    }
                }

                presentation.Present(frame, 0);
                cancellation.ThrowIfCancellationRequested();
                await viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                cancellation.ThrowIfCancellationRequested();
                await viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                cancellation.ThrowIfCancellationRequested();
                using var image = ViewportCapture.Take(viewport)
                    ?? throw new InvalidOperationException("Prepared map viewport has no rendered frame");
                return new Map2dPreparedLayer(image);
            }
            finally
            {
                presentation?.Dispose();
                scene?.Release();
                viewport?.Free();
            }
        }

        private static bool Unset(string condition, string scope)
        {
            return false;
        }
    }
}
