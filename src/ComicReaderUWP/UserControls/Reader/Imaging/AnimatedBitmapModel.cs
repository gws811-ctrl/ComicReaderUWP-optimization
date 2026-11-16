// Copyright (c) aicd0. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;

using SkiaSharp;

namespace ComicReaderUWP.UserControls.Reader.Imaging;

internal sealed partial class AnimatedBitmapModel : IDisposable
{
    /// <summary>
    /// Upper bound of frames that will ever be decoded for one image, to keep pathological files
    /// from eating unlimited memory.
    /// </summary>
    private const int MAX_DECODED_FRAMES = 400;

    public static AnimatedBitmapModel FromCanvasBitmap(CanvasBitmap bitmap)
    {
        Size size = new((int)bitmap.SizeInPixels.Width, (int)bitmap.SizeInPixels.Height);
        return new AnimatedBitmapModel(size, [bitmap], [1L], null, null, null);
    }

    /// <summary>
    /// Reads the frame count from the image header without decoding pixel data.
    /// <para>
    /// The stream is copied first: <c>SKCodec</c> takes ownership of the stream it is created
    /// from and closes it on dispose, so it must never be handed the caller's stream (doing so
    /// used to close the page file and crash with ObjectDisposedException).
    /// The caller's stream position is restored afterwards.
    /// </para>
    /// </summary>
    public static int GetFrameCount(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return 1;
        }

        long position = stream.Position;
        try
        {
            byte[] data;
            int length;
            using (MemoryStream memory = new())
            {
                stream.CopyTo(memory);
                if (!memory.TryGetBuffer(out ArraySegment<byte> segment))
                {
                    return 1;
                }

                data = segment.Array!;
                length = segment.Count > 0 ? segment.Offset + segment.Count : (int)memory.Length;
            }

            using MemoryStream copy = new(data, 0, length, writable: false);
            using SKCodec? codec = SKCodec.Create(copy);
            return codec?.FrameCount ?? 1;
        }
        catch (Exception)
        {
            return 1;
        }
        finally
        {
            try
            {
                stream.Position = position;
            }
            catch (Exception)
            {
                // Stream could have been closed by the caller meanwhile; nothing to restore.
            }
        }
    }

    /// <summary>
    /// Decodes an animated image progressively.
    /// <para>
    /// Two things made the original implementation extremely slow on multi-frame WebP files:
    /// 1) it decoded every frame before the page could be displayed (123 frames ≈ 70 s);
    /// 2) <c>SKCodec.GetPixels</c> with a random frame index re-decodes all preceding frames
    ///    (O(n²)). Reusing one pixel buffer and passing the previous frame index as
    ///    <c>priorFrame</c> turns the same work into a single linear pass (≈1.1 s for 123 frames).
    /// </para>
    /// Frame 0 is decoded synchronously so the page shows up immediately; the rest is decoded in
    /// the background while the animation is already playing.
    /// </summary>
    public static AnimatedBitmapModel? FromStream(ICanvasResourceCreator canvasDevice, Stream stream)
    {
        byte[] data;
        int dataLength;
        try
        {
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            if (!memory.TryGetBuffer(out ArraySegment<byte> segment))
            {
                return null;
            }

            data = segment.Array!;
            dataLength = segment.Count > 0 ? segment.Offset + segment.Count : (int)memory.Length;
        }
        catch (Exception)
        {
            return null;
        }

        MemoryStream buffer = new(data, 0, dataLength, writable: false);
        SKCodec? codec = null;
        SKBitmap? decodeBuffer = null;
        try
        {
            codec = SKCodec.Create(buffer);
            if (codec is null)
            {
                buffer.Dispose();
                return null;
            }

            SKImageInfo info = codec.Info;
            SKCodecFrameInfo[]? frameInfo = codec.FrameInfo;
            int frameCount = Math.Min(codec.FrameCount, MAX_DECODED_FRAMES);
            if (frameCount <= 0)
            {
                codec.Dispose();
                buffer.Dispose();
                return null;
            }

            long[] durations = new long[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                int duration = frameInfo is not null && i < frameInfo.Length ? frameInfo[i].Duration : 100;
                durations[i] = duration > 0 ? duration : 100;
            }

            decodeBuffer = new SKBitmap(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            if (!DecodeInto(codec, decodeBuffer, 0, -1))
            {
                codec.Dispose();
                buffer.Dispose();
                decodeBuffer.Dispose();
                return null;
            }

            CanvasBitmap? firstFrame = CreateCanvasBitmap(canvasDevice, decodeBuffer, info);
            if (firstFrame is null)
            {
                codec.Dispose();
                buffer.Dispose();
                decodeBuffer.Dispose();
                return null;
            }

            AnimatedBitmapModel model = new(
                new Size(info.Width, info.Height),
                new CanvasBitmap?[frameCount],
                durations,
                codec,
                buffer,
                decodeBuffer);

            model._bitmaps[0] = firstFrame;
            // The decode buffer currently holds frame 0, so the background pass can continue
            // sequentially from frame 1 with priorFrame = i - 1.
            model.StartBackgroundDecoding(canvasDevice, info);
            return model;
        }
        catch (Exception)
        {
            codec?.Dispose();
            buffer.Dispose();
            decodeBuffer?.Dispose();
            return null;
        }
    }

    private static bool DecodeInto(SKCodec codec, SKBitmap bitmap, int index, int priorFrame)
    {
        try
        {
            SKCodecOptions options = priorFrame >= 0
                ? new SKCodecOptions(index, priorFrame)
                : new SKCodecOptions(index);

            SKCodecResult result = codec.GetPixels(bitmap.Info, bitmap.GetPixels(), options);
            return result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static CanvasBitmap? CreateCanvasBitmap(ICanvasResourceCreator canvasDevice, SKBitmap bitmap, SKImageInfo info)
    {
        int bytes = bitmap.RowBytes * bitmap.Height;
        if (bytes <= 0)
        {
            return null;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(bytes);
        try
        {
            Marshal.Copy(bitmap.GetPixels(), buffer, 0, bytes);
            return CanvasBitmap.CreateFromBytes(
                canvasDevice,
                buffer,
                info.Width,
                info.Height,
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                96);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private readonly Size _size;
    private readonly CanvasBitmap?[] _bitmaps;
    private readonly long[] _durations;
    private readonly long[] _startTimes;
    private readonly long _duration;
    private readonly object _lock = new();

    private SKCodec? _codec;
    private MemoryStream? _buffer;
    private SKBitmap? _decodeBuffer;
    private bool _disposed;

    private AnimatedBitmapModel(Size size, CanvasBitmap?[] bitmaps, long[] durations,
        SKCodec? codec, MemoryStream? buffer, SKBitmap? decodeBuffer)
    {
        _size = size;
        _bitmaps = bitmaps;
        _durations = durations;
        _codec = codec;
        _buffer = buffer;
        _decodeBuffer = decodeBuffer;
        _startTimes = new long[durations.Length];

        long elapsed = 0;
        for (int i = 0; i < durations.Length; i++)
        {
            _startTimes[i] = elapsed;
            elapsed += durations[i];
        }

        _duration = elapsed;
    }

    public Size SizeInPixels => _size;
    public int FrameCount => _bitmaps.Length;
    public long Duration => _duration;

    public int GetFrameIndexAtTime(long elapsedMs)
    {
        if (_startTimes.Length <= 1)
        {
            return 0;
        }

        long t = (_duration == 0) ? 0 : ((elapsedMs % _duration) + _duration) % _duration;

        int lo = 0;
        int hi = _startTimes.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            long start = _startTimes[mid];

            if (start <= t)
            {
                if (mid == _startTimes.Length - 1 || _startTimes[mid + 1] > t)
                {
                    return mid;
                }

                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return 0;
    }

    public CanvasBitmap GetFrameBitmap(int frameIndex)
    {
        if (frameIndex < 0)
        {
            frameIndex = 0;
        }
        else if (frameIndex >= _bitmaps.Length)
        {
            frameIndex = _bitmaps.Length - 1;
        }

        CanvasBitmap? bitmap = Volatile.Read(ref _bitmaps[frameIndex]);
        if (bitmap is not null)
        {
            return bitmap;
        }

        // Requested frame is not decoded yet: fall back to the closest decoded frame before it,
        // so drawing never blocks on the decoder.
        for (int i = frameIndex - 1; i >= 0; i--)
        {
            bitmap = Volatile.Read(ref _bitmaps[i]);
            if (bitmap is not null)
            {
                return bitmap;
            }
        }

        return Volatile.Read(ref _bitmaps[0])!;
    }

    public long GetFrameStartTime(int frameIndex)
    {
        return _startTimes[frameIndex];
    }

    private void StartBackgroundDecoding(ICanvasResourceCreator canvasDevice, SKImageInfo info)
    {
        if (_bitmaps.Length <= 1)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            for (int i = 1; i < _bitmaps.Length; i++)
            {
                lock (_lock)
                {
                    if (_disposed || _codec is null || _decodeBuffer is null)
                    {
                        return;
                    }

                    if (!DecodeInto(_codec, _decodeBuffer, i, i - 1))
                    {
                        return;
                    }

                    CanvasBitmap? bitmap = CreateCanvasBitmap(canvasDevice, _decodeBuffer, info);
                    if (bitmap is null)
                    {
                        return;
                    }

                    _bitmaps[i] = bitmap;
                }

                // Yield so the UI thread stays responsive while the animation decodes.
                Thread.Sleep(1);
            }
        });
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            for (int i = 0; i < _bitmaps.Length; i++)
            {
                _bitmaps[i]?.Dispose();
                _bitmaps[i] = null;
            }

            _codec?.Dispose();
            _codec = null;
            _buffer?.Dispose();
            _buffer = null;
            _decodeBuffer?.Dispose();
            _decodeBuffer = null;
        }
    }
}
