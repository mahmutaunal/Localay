using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AlpWareStudio.Localay
{
    /// <summary>
    /// Tells whether a captured frame shows the same picture as the last frame that was OCR'd, so the
    /// capture loop can skip OCR while the screen stands still (most of the time, between lines).
    /// </summary>
    public sealed class FrameChangeDetector
    {
        private byte[] _ocrFrame = Array.Empty<byte>();
        private byte[] _frame = Array.Empty<byte>();
        private byte[] _previous = Array.Empty<byte>();
        private int _previousWidth, _previousHeight, _previousOriginX, _previousOriginY;
        private bool _hasPrevious;
        internal FrameChangeMap Changes { get; private set; } = FrameChangeMap.All(0, 0);
        private int _ocrWidth, _ocrHeight, _ocrOriginX, _ocrOriginY;
        private int _width, _height, _originX, _originY;
        private bool _hasOcrFrame;
        private bool _hasFrame;

        /// <summary>
        /// Takes a copy of the frame whose top-left pixel is at (originX, originY) on screen and
        /// compares it with the last OCR'd frame. A moved capture area always counts as changed.
        /// </summary>
        public bool IsSameAsOcrFrame(Bitmap frame, int originX, int originY)
        {
            _hasFrame = TryCopyPixels(frame);
            _originX = originX;
            _originY = originY;
            if (!_hasFrame)
            {
                Changes = FrameChangeMap.All(frame.Width, frame.Height);
                _hasPrevious = false;
                return false;
            }

            int length = checked(_width * _height * 4);
            Changes = !_hasPrevious || _width != _previousWidth || _height != _previousHeight ||
                originX != _previousOriginX || originY != _previousOriginY
                ? FrameChangeMap.All(_width, _height)
                : FrameChangeMap.Compare(_frame.AsSpan(0, length), _previous.AsSpan(0, length), _width, _height);
            bool sameAsOcr = _hasOcrFrame && _width == _ocrWidth && _height == _ocrHeight &&
                _originX == _ocrOriginX && _originY == _ocrOriginY &&
                !FrameChangeMap.Compare(_frame.AsSpan(0, length), _ocrFrame.AsSpan(0, length), _width, _height).HasChanges;
            // Rotate the two observation buffers instead of copying a second full image
            // at 30 Hz. The separate OCR baseline is copied only when OCR actually starts.
            (_previous, _frame) = (_frame, _previous);
            _previousWidth = _width; _previousHeight = _height;
            _previousOriginX = originX; _previousOriginY = originY;
            _hasPrevious = true;
            return sameAsOcr;
        }

        /// <summary>The frame last passed to IsSameAsOcrFrame is being OCR'd: compare later frames with it.</summary>
        public void MarkOcrFrame()
        {
            if (!_hasFrame)
            {
                _hasOcrFrame = false;
                return;
            }

            int length = checked(_width * _height * 4);
            if (_ocrFrame.Length < length) _ocrFrame = new byte[length];
            Buffer.BlockCopy(_previous, 0, _ocrFrame, 0, length);
            _ocrWidth = _width;
            _ocrHeight = _height;
            _ocrOriginX = _originX;
            _ocrOriginY = _originY;
            _hasOcrFrame = true;
            _hasFrame = false;
        }

        private bool TryCopyPixels(Bitmap bitmap)
        {
            try
            {
                int width = bitmap.Width;
                int height = bitmap.Height;
                int rowBytes = width * 4;
                if (_frame.Length < rowBytes * height)
                    _frame = new byte[rowBytes * height];

                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, _frame, y * rowBytes, rowBytes);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                _width = width;
                _height = height;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Frame change check failed, running OCR anyway: {ex.Message}");
                return false;
            }
        }
    }
}
