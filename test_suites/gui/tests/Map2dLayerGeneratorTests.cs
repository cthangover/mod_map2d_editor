namespace Cthangover.Tools.Map2d.Tests.Gui
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Rpg;
    using Godot;

    /// <summary>Actual isolated export frames the exact grid, keeps authored base scenery and excludes every actor.</summary>
    public sealed class Map2dLayerGeneratorTests
    {
        [ModTest("map_generate/exact_grid_png_round_trip_no_actor_or_game_mutation", TimeoutSeconds = 60)]
        private static async Task Generate(ModTestContext context)
        {
            var map = RpgWorldHost.MapOf("map_layer_probe");
            var source = JsonSerializer.Serialize(map);
            var memory = JsonSerializer.Serialize(ExplorationMemory.Snapshot());
            using var prepared = await Map2dLayerGenerator.Generate(map, 8, CancellationToken.None);
            ModAssert.Equal(32, prepared.Width, "export uses cells times requested scale, ignoring gameplay zoom");
            ModAssert.Equal(24, prepared.Height, "export covers the exact grid height");
            ModAssert.Equal(32, prepared.Texture.GetWidth(), "preview texture matches PNG metadata");
            ModAssert.True(!prepared.Png.IsEmpty, "explicit export retains runtime-ready PNG bytes");
            using var restored = new Image();
            ModAssert.True(restored.LoadPngFromBuffer(prepared.Png.ToArray()) == Error.Ok, "complete PNG bytes decode after export");
            ModAssert.Equal(32, restored.GetWidth(), "PNG round trip retains width");
            ModAssert.Equal(24, restored.GetHeight(), "PNG round trip retains height");
            Green(restored.GetPixel(4, 4), "authored terrain fills the first cell");
            Green(restored.GetPixel(12, 12), "red actor is omitted from the prepared layer");
            Green(restored.GetPixel(20, 12), "dynamic scenery uses the authored green base, not its red future state");
            Green(restored.GetPixel(28, 20), "full grid includes its last cell without fit margins");
            ModAssert.Equal(source, JsonSerializer.Serialize(map), "generation never rewrites source map zoom, effects or grids");
            ModAssert.Equal(memory, JsonSerializer.Serialize(ExplorationMemory.Snapshot()), "generation never opens game exploration");
            prepared.Dispose();
            ModAssert.True(prepared.Texture == null, "preview owner releases its texture idempotently");
        }

        [ModTest("map_generate/cancel_before_and_during_owned_viewport", TimeoutSeconds = 60)]
        private static async Task Cancel(ModTestContext context)
        {
            var map = RpgWorldHost.MapOf("map_layer_probe");
            using var before = new CancellationTokenSource();
            before.Cancel();
            var cancelled = false;
            try
            {
                using var result = await Map2dLayerGenerator.Generate(map, 8, before.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            ModAssert.True(cancelled, "pre-cancelled generation creates no result");
            using var during = new CancellationTokenSource();
            var task = Map2dLayerGenerator.Generate(map, 8, during.Token);
            during.Cancel();
            cancelled = false;
            try
            {
                using var result = await task;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            ModAssert.True(cancelled, "in-flight cancellation drains the owned viewport without returning a layer");
        }

        private static void Green(Color color, string message)
        {
            ModAssert.True(Math.Abs(color.R - 80f / 255) < 0.01f && Math.Abs(color.G - 180f / 255) < 0.01f
                && Math.Abs(color.B - 80f / 255) < 0.01f, message + ": " + color);
        }
    }
}
