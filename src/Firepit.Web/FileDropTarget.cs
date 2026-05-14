using System.Runtime.InteropServices;
using System.Text;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Firepit.Web;

/// <summary>
/// Native OLE drop target for the WebView2's host HWND. The WebView2 is an
/// <c>HwndHost</c> — WPF's managed drag-drop events never fire over its
/// airspace, and with <c>AllowExternalDrop = false</c> the browser layer
/// registers no drop target of its own. So Firepit registers this
/// <see cref="IDropTarget"/> directly on the host window via
/// <c>RegisterDragDrop</c>; it pulls the full file-system paths out of the
/// <c>CF_HDROP</c> payload (which the HTML5 <c>drop</c> event deliberately
/// can't expose) and hands them to a callback.
/// </summary>
internal sealed class FileDropTarget : IDropTarget
{
    private const int   DropEffectNone = 0;   // DROPEFFECT_NONE
    private const int   DropEffectCopy = 1;   // DROPEFFECT_COPY
    private const short CfHdrop        = 15;  // CF_HDROP
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
    {
        var fmt = MakeHdropFormat();
        try { return data.QueryGetData(ref fmt) == SOk; }
        catch { return false; }
    }

    private static IReadOnlyList<string> ExtractFilePaths(ComTypes.IDataObject data)
    {
        var fmt = MakeHdropFormat();
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

    private static ComTypes.FORMATETC MakeHdropFormat() => new()
    {
        cfFormat = CfHdrop,
        ptd      = IntPtr.Zero,
        dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
        lindex   = -1,
        tymed    = ComTypes.TYMED.TYMED_HGLOBAL,
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM pmedium);
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
