using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;
using ComTypes = System.Runtime.InteropServices.ComTypes;
using Serilog;

namespace Firepit.Web;

/// <summary>
/// Native OLE drop target for the WebView2's host HWND. The WebView2 is an
/// <c>HwndHost</c> — WPF's managed drag-drop events never fire over its
/// airspace, and with <c>AllowExternalDrop = false</c> the browser layer
/// registers no drop target of its own. So Firepit registers this
/// <see cref="IDropTarget"/> directly on the host window via
/// <c>RegisterDragDrop</c>; it accepts two payload shapes:
///
/// 1. <c>CF_HDROP</c> — Explorer file drops. The full file-system paths are
///    pulled directly out of the HDROP (the HTML5 <c>drop</c> event
///    deliberately can't expose them).
/// 2. <c>CF_DIB</c> — in-memory bitmaps from Snipping Tool, Slack image
///    paste, browser image drags, etc. The DIB is persisted to a temp PNG
///    under <c>%LOCALAPPDATA%\Firepit\dragdrop\</c> and the resulting path
///    is handed to the callback exactly like a file drop, so Claude Code
///    sees it as a real file path it can read.
/// </summary>
internal sealed class FileDropTarget : IDropTarget
{
    private const int   DropEffectNone = 0;   // DROPEFFECT_NONE
    private const int   DropEffectCopy = 1;   // DROPEFFECT_COPY
    private const short CfHdrop        = 15;  // CF_HDROP
    private const short CfDib          = 8;   // CF_DIB
    private const int   SOk            = 0;   // S_OK

    private readonly Action<IReadOnlyList<string>> _onDrop;
    private bool _payloadHasFiles;

    public FileDropTarget(Action<IReadOnlyList<string>> onDrop)
    {
        _onDrop = onDrop;
    }

    public int DragEnter(ComTypes.IDataObject pDataObj, int grfKeyState, POINT pt, ref int pdwEffect)
    {
        _payloadHasFiles = HasFileDrop(pDataObj);
        pdwEffect = _payloadHasFiles ? DropEffectCopy : DropEffectNone;
        return SOk;
    }

    public int DragOver(int grfKeyState, POINT pt, ref int pdwEffect)
    {
        pdwEffect = _payloadHasFiles ? DropEffectCopy : DropEffectNone;
        return SOk;
    }

    public int DragLeave()
    {
        _payloadHasFiles = false;
        return SOk;
    }

    public int Drop(ComTypes.IDataObject pDataObj, int grfKeyState, POINT pt, ref int pdwEffect)
    {
        try
        {
            var paths = ExtractFilePaths(pDataObj);
            if (paths.Count > 0)
            {
                pdwEffect = DropEffectCopy;
                _onDrop(paths);
            }
            else
            {
                pdwEffect = DropEffectNone;
            }
        }
        catch
        {
            // A malformed payload must not throw back across the COM boundary.
            pdwEffect = DropEffectNone;
        }
        finally
        {
            _payloadHasFiles = false;
        }
        return SOk;
    }

    private static bool HasFileDrop(ComTypes.IDataObject data)
        => HasFormat(data, CfHdrop) || HasFormat(data, CfDib);

    private static bool HasFormat(ComTypes.IDataObject data, short cfFormat)
    {
        var fmt = MakeFormat(cfFormat);
        try { return data.QueryGetData(ref fmt) == SOk; }
        catch { return false; }
    }

    private static IReadOnlyList<string> ExtractFilePaths(ComTypes.IDataObject data)
    {
        if (HasFormat(data, CfHdrop)) return ExtractHdropPaths(data);
        if (HasFormat(data, CfDib))
        {
            var path = PersistDibAsTempPng(data);
            return path is null ? Array.Empty<string>() : new[] { path };
        }
        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> ExtractHdropPaths(ComTypes.IDataObject data)
    {
        var fmt = MakeFormat(CfHdrop);
        data.GetData(ref fmt, out var medium);
        try
        {
            // For CF_HDROP the HGLOBAL handle is itself a usable HDROP.
            var hDrop = medium.unionmember;
            if (hDrop == IntPtr.Zero) return Array.Empty<string>();

            uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            var paths = new string[count];
            for (uint i = 0; i < count; i++)
            {
                uint len = DragQueryFile(hDrop, i, null, 0);
                var buffer = new StringBuilder((int)len + 1);
                DragQueryFile(hDrop, i, buffer, (uint)buffer.Capacity);
                paths[i] = buffer.ToString();
            }
            return paths;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    /// <summary>
    /// Pull a CF_DIB payload (Snipping Tool, browser image drag, Slack paste)
    /// out of the data object, wrap it as a valid BMP, decode through WPF's
    /// imaging pipeline, and persist as a PNG in
    /// <c>%LOCALAPPDATA%\Firepit\dragdrop\</c>. Returns the path so the
    /// caller can paste it like a regular file drop.
    ///
    /// CF_DIB is just the DIB part of a BMP (BITMAPINFOHEADER + optional
    /// color table + pixels) with the 14-byte BITMAPFILEHEADER chopped off.
    /// Computing the pixel offset is the fiddly bit: for &lt;=8bpp it's
    /// determined by biClrUsed/2^biBitCount, for 16/32bpp with BI_BITFIELDS
    /// compression it includes a 12-byte mask table.
    /// </summary>
    private static string? PersistDibAsTempPng(ComTypes.IDataObject data)
    {
        var fmt = MakeFormat(CfDib);
        ComTypes.STGMEDIUM medium = default;
        try
        {
            data.GetData(ref fmt, out medium);
            var hGlobal = medium.unionmember;
            if (hGlobal == IntPtr.Zero) return null;

            var ptr = GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                var size = (int)GlobalSize(hGlobal).ToUInt32();
                if (size < 40) return null; // smaller than a BITMAPINFOHEADER → garbage

                var dib = new byte[size];
                Marshal.Copy(ptr, dib, 0, size);

                var bmp = WrapDibAsBmp(dib);
                if (bmp is null) return null;

                using var ms = new MemoryStream(bmp);
                var decoder = new BmpBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) return null;

                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Firepit", "dragdrop");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"image-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(decoder.Frames[0]);
                using (var fs = File.Create(path))
                {
                    encoder.Save(fs);
                }
                Log.Information("DnD: persisted CF_DIB ({Bytes} B) as {Path}", size, path);
                return path;
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DnD: CF_DIB persistence failed");
            return null;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    /// <summary>
    /// Prepend a 14-byte BITMAPFILEHEADER to a CF_DIB byte buffer so the
    /// result is a complete BMP file that BmpBitmapDecoder can parse.
    /// Returns null for malformed DIBs (header smaller than BITMAPINFOHEADER,
    /// unsupported header version).
    /// </summary>
    private static byte[]? WrapDibAsBmp(byte[] dib)
    {
        if (dib.Length < 40) return null;
        var headerSize = BitConverter.ToInt32(dib, 0);
        if (headerSize < 40 || headerSize > dib.Length) return null;

        short bitCount   = BitConverter.ToInt16(dib, 14);
        int   compression = BitConverter.ToInt32(dib, 16);
        int   colorsUsed  = BitConverter.ToInt32(dib, 32);

        int paletteSize = 0;
        if (bitCount <= 8)
        {
            var paletteEntries = colorsUsed == 0 ? (1 << bitCount) : colorsUsed;
            paletteSize = paletteEntries * 4;  // RGBQUAD
        }
        else if ((bitCount == 16 || bitCount == 32) && compression == 3 /* BI_BITFIELDS */)
        {
            paletteSize = 12;  // three DWORD masks (R, G, B)
        }

        var pixelOffset = 14 + headerSize + paletteSize;
        var fileSize    = 14 + dib.Length;

        var bmp = new byte[fileSize];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
        // bytes 6..9 = reserved (already zero)
        BitConverter.GetBytes(pixelOffset).CopyTo(bmp, 10);
        Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
        return bmp;
    }

    private static ComTypes.FORMATETC MakeFormat(short cfFormat) => new()
    {
        cfFormat = cfFormat,
        ptd      = IntPtr.Zero,
        dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
        lindex   = -1,
        tymed    = ComTypes.TYMED.TYMED_HGLOBAL,
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM pmedium);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);
}

/// <summary>POINTL — passed by value into the <see cref="IDropTarget"/> methods.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

/// <summary>
/// Managed projection of the COM <c>IDropTarget</c> interface
/// (IID <c>00000122-0000-0000-C000-000000000046</c>). A managed class can
/// implement this and be handed to <c>RegisterDragDrop</c> as a CCW.
/// </summary>
[ComImport]
[Guid("00000122-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTarget
{
    [PreserveSig] int DragEnter(ComTypes.IDataObject pDataObj, int grfKeyState, POINT pt, ref int pdwEffect);
    [PreserveSig] int DragOver(int grfKeyState, POINT pt, ref int pdwEffect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(ComTypes.IDataObject pDataObj, int grfKeyState, POINT pt, ref int pdwEffect);
}

internal static class NativeDragDrop
{
    [DllImport("ole32.dll")]
    public static extern int RegisterDragDrop(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] IDropTarget pDropTarget);

    [DllImport("ole32.dll")]
    public static extern int RevokeDragDrop(IntPtr hwnd);
}
