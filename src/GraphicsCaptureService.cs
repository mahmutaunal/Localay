using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using WinRT;

namespace AlpWareStudio.Localay
{
    /// <summary>
    /// Captures a window using the Windows Graphics Capture API (Windows 10 2004+).
    /// GPU-friendly alternative to PrintWindow that avoids flickering in games.
    /// </summary>
    internal sealed class GraphicsCaptureService : IDisposable
    {
        private Direct3D11Helper? _d3dHelper;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private GraphicsCaptureItem? _captureItem;
        private SizeInt32 _lastSize;
        private bool _disposed;
        private bool _itemClosed;
        private Bitmap? _lastFrame;
        private const int FramePoolCapacity = 2;
        private readonly object _frameGate = new();
        private readonly LatestCaptureFrame<Direct3D11CaptureFrame> _latestFrame = new();

        /// <summary>
        /// Check whether Windows Graphics Capture is available on this system.
        /// </summary>
        public static bool IsSupported()
        {
            try
            {
                return GraphicsCaptureSession.IsSupported();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Start capturing the specified window.
        /// Returns true if capture started successfully.
        /// </summary>
        public bool StartCapture(IntPtr hwnd)
        {
            StopCapture();
            try
            {
                lock (_frameGate)
                {
                    if (_disposed) return false;
                    _d3dHelper = Direct3D11Helper.Create();
                    _captureItem = CreateCaptureItemForWindow(hwnd);
                    if (_captureItem == null) throw new InvalidOperationException("No capture item was created.");
                    _captureItem.Closed += OnCaptureItemClosed;
                    _lastSize = _captureItem.Size;

                    // Native frame arrival replaces the one pending surface. Pixel readback
                    // and OCR happen only when the consumer is ready, not for every event.
                    _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                        _d3dHelper.WinRTDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        FramePoolCapacity, _captureItem.Size);
                    _framePool.FrameArrived += OnFrameArrived;
                    _session = _framePool.CreateCaptureSession(_captureItem);
                    try { _session.IsCursorCaptureEnabled = false; } catch { }
                    _session.StartCapture();
                    Console.WriteLine("Windows Graphics Capture started successfully");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start Windows Graphics Capture: {ex.Message}");
                // Native shutdown can wait for event callbacks: never hold their gate here.
                StopCapture();
                return false;
            }
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            lock (_frameGate)
            {
                if (_disposed || _itemClosed || !ReferenceEquals(sender, _framePool)) return;
                try { _latestFrame.Drain(sender.TryGetNextFrame, FramePoolCapacity); }
                catch (Exception ex) { Console.WriteLine($"WGC frame arrival failed: {ex.GetType().Name}"); }
            }
        }

        private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args)
        {
            Direct3D11CaptureFrame? pending;
            Bitmap? cached;
            lock (_frameGate)
            {
                if (!ReferenceEquals(sender, _captureItem)) return;
                _itemClosed = true;
                pending = _latestFrame.Take();
                cached = _lastFrame;
                _lastFrame = null;
            }
            try { pending?.Dispose(); } catch { }
            try { cached?.Dispose(); } catch { }
        }

        /// <summary>
        /// Grab the latest frame synchronously.
        /// Returns null when no frame is available or the target window is gone.
        /// </summary>
        public Bitmap? CaptureFrame(bool allowCachedFrame = true)
        {
            lock (_frameGate)
            {
                if (_disposed || _itemClosed || _framePool == null || _d3dHelper == null)
                    return null;

                try
                {
                    // Also drain here in case the UI runs before an already queued event callback.
                    _latestFrame.Drain(_framePool.TryGetNextFrame, FramePoolCapacity);
                    var frame = _latestFrame.Take();
                    if (frame == null)
                    {
                        // No new frame since last call — return a copy of the last captured frame.
                        if (allowCachedFrame && _lastFrame != null)
                            return (Bitmap)_lastFrame.Clone();
                        return null;
                    }

                    SizeInt32 contentSize;
                    Bitmap bitmap;
                    try
                    {
                        contentSize = frame.ContentSize;
                        if (contentSize.Width <= 0 || contentSize.Height <= 0) return null;
                        bitmap = _d3dHelper.SurfaceToBitmap(frame.Surface, contentSize.Width, contentSize.Height);
                    }
                    finally { frame.Dispose(); }

                    // Release the acquired frame before recreating its pool.
                    if (contentSize.Width != _lastSize.Width || contentSize.Height != _lastSize.Height)
                    {
                        _latestFrame.Clear();
                        try
                        {
                            _framePool.Recreate(_d3dHelper.WinRTDevice,
                                DirectXPixelFormat.B8G8R8A8UIntNormalized, FramePoolCapacity, contentSize);
                            _lastSize = contentSize;
                        }
                        catch { bitmap.Dispose(); throw; }
                    }

                    // Cache the latest frame for reuse when TryGetNextFrame returns null.
                    Bitmap cached;
                    try { cached = (Bitmap)bitmap.Clone(); }
                    catch { bitmap.Dispose(); throw; }
                    _lastFrame?.Dispose();
                    _lastFrame = cached;
                    return bitmap;
                }
                catch (Exception ex)
                {
                    PipelineDiagnostics.Current?.Finish(PipelineOutcome.Failed);
                    _lastFrame?.Dispose();
                    _lastFrame = null;
                    Console.WriteLine($"WGC CaptureFrame error: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Stop the current capture session and release resources.
        /// </summary>
        public void StopCapture()
        {
            GraphicsCaptureSession? session;
            Direct3D11CaptureFramePool? pool;
            Direct3D11Helper? helper;
            Bitmap? cached;
            Direct3D11CaptureFrame? pending;
            lock (_frameGate)
            {
                session = _session;
                pool = _framePool;
                helper = _d3dHelper;
                cached = _lastFrame;
                pending = _latestFrame.Take();
                try { if (pool != null) pool.FrameArrived -= OnFrameArrived; } catch { }
                try { if (_captureItem != null) _captureItem.Closed -= OnCaptureItemClosed; } catch { }
                _session = null;
                _framePool = null;
                _captureItem = null;
                _d3dHelper = null;
                _lastFrame = null;
                _itemClosed = false;
            }

            // A queued callback can now observe the detached pool and return immediately.
            // Closing the pool/session while holding _frameGate could deadlock that callback.
            try { pending?.Dispose(); } catch { }
            try { session?.Dispose(); } catch { }
            try { pool?.Dispose(); } catch { }
            try { helper?.Dispose(); } catch { }
            try { cached?.Dispose(); } catch { }
            Console.WriteLine("Windows Graphics Capture stopped");
        }

        public void Dispose()
        {
            lock (_frameGate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            StopCapture();
            _latestFrame.Dispose();
        }

        // ═════════ HWND → GraphicsCaptureItem interop ═════════

        private static GraphicsCaptureItem? CreateCaptureItemForWindow(IntPtr hwnd)
        {
            string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
            int hr = WindowsCreateString(className, className.Length, out IntPtr hClassName);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                Guid interopGuid = typeof(IGraphicsCaptureItemInterop).GUID;
                hr = RoGetActivationFactory(hClassName, ref interopGuid, out IntPtr factoryPtr);
                Marshal.ThrowExceptionForHR(hr);

                try
                {
                    var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);

                    // IGraphicsCaptureItem interface GUID.
                    Guid itemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                    IntPtr itemPtr = interop.CreateForWindow(hwnd, ref itemIid);

                    try
                    {
                        return (GraphicsCaptureItem)MarshalInspectable<object>.FromAbi(itemPtr);
                    }
                    finally
                    {
                        Marshal.Release(itemPtr);
                    }
                }
                finally
                {
                    Marshal.Release(factoryPtr);
                }
            }
            finally
            {
                WindowsDeleteString(hClassName);
            }
        }

        // ═════════ COM interfaces ═════════

        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        }

        // ═════════ P/Invoke ═════════

        [DllImport("combase.dll")]
        private static extern int WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
            int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(
            IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
    }
}
