namespace Cthangover.Tools.Map2d
{
    using System;
    using Godot;

    /// <summary>Owns one generated preview and its runtime-ready PNG; drawing consumers must detach before disposal.</summary>
    public sealed class Map2dPreparedLayer : IDisposable
    {
        /// <summary>Borrowed generated texture, retained by the authoring window until publication or closure.</summary>
        public ImageTexture Texture { get; private set; }

        /// <summary>Encoded display-space PNG bytes, ready for an explicit save without another world render.</summary>
        public ReadOnlyMemory<byte> Png { get; }

        /// <summary>Baked pixel width, independent of the source world's gameplay zoom.</summary>
        public int Width { get; }

        /// <summary>Baked pixel height; the generator frames the exact RPG grid rectangle.</summary>
        public int Height { get; }

        internal Map2dPreparedLayer(Image image)
        {
            Width = image.GetWidth();
            Height = image.GetHeight();
            Png = image.SavePngToBuffer();
            if (Png.IsEmpty)
            {
                throw new InvalidOperationException("Prepared map could not be encoded");
            }

            Texture = ImageTexture.CreateFromImage(image);
        }

        /// <summary>Releases only this generated GPU image; PNG bytes remain managed and disposal is idempotent.</summary>
        public void Dispose()
        {
            Texture?.Dispose();
            Texture = null;
        }
    }
}
