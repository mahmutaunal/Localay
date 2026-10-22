using System;

namespace AlpWareStudio.Localay;

// Capture-local pixel coordinates. Tiles bound bookkeeping, not pixel sampling:
// every RGB pixel is checked, including small punctuation and edge tiles.
internal sealed class FrameChangeMap
{
    private const int TileSize = 32;
    private readonly bool[] _tiles;
    private readonly int _columns;
    internal int Width { get; }
    internal int Height { get; }
    internal bool WholeFrame { get; private set; }
    internal bool HasChanges { get; private set; }

    private FrameChangeMap(int width, int height, bool wholeFrame)
    {
        Width = width;
        Height = height;
        WholeFrame = wholeFrame;
        HasChanges = wholeFrame;
        _columns = (width + TileSize - 1) / TileSize;
        _tiles = wholeFrame ? Array.Empty<bool>() : new bool[_columns * ((height + TileSize - 1) / TileSize)];
    }

    internal static FrameChangeMap All(int width, int height) => new(width, height, true);
    internal static FrameChangeMap Empty(int width, int height) => new(width, height, false);

    // Called on the UI thread. Once a tile changed during OCR it remains invalid,
    // even when a later capture happens to show its original pixels again.
    internal void Include(FrameChangeMap changes)
    {
        if (!changes.HasChanges) return;
        HasChanges = true;
        if (WholeFrame) return;
        if (changes.WholeFrame || Width != changes.Width || Height != changes.Height)
        {
            WholeFrame = true;
            return;
        }
        for (int i = 0; i < _tiles.Length; i++) _tiles[i] |= changes._tiles[i];
    }

    internal static FrameChangeMap Compare(ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous,
        int width, int height)
    {
        int length = checked(width * height * 4);
        if (width < 1 || height < 1 || current.Length < length || previous.Length < length)
            throw new ArgumentException("Invalid BGRA frame dimensions.");
        var result = new FrameChangeMap(width, height, false);
        if (current[..length].SequenceEqual(previous[..length])) return result;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                // Alpha is not screen content. Preserve RGB changes even below the old
                // channel threshold: a low-contrast character must invalidate its patch.
                if (current[i] == previous[i] && current[i + 1] == previous[i + 1] && current[i + 2] == previous[i + 2]) continue;
                result._tiles[(y / TileSize) * result._columns + x / TileSize] = true;
                result.HasChanges = true;
            }
        }
        return result;
    }

    internal bool Intersects(double x, double y, double width, double height)
    {
        if (!HasChanges) return false;
        if (WholeFrame) return true;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)) return true;
        // Include the edge pixel and a small margin for OCR/eraser rounding.
        int left = (int)Math.Clamp(Math.Floor(x - 2), 0, Width);
        int top = (int)Math.Clamp(Math.Floor(y - 2), 0, Height);
        int right = (int)Math.Clamp(Math.Ceiling(x + width + 2), 0, Width);
        int bottom = (int)Math.Clamp(Math.Ceiling(y + height + 2), 0, Height);
        if (right <= left || bottom <= top) return false;
        for (int row = top / TileSize; row <= (bottom - 1) / TileSize; row++)
            for (int column = left / TileSize; column <= (right - 1) / TileSize; column++)
                if (_tiles[row * _columns + column]) return true;
        return false;
    }
}
