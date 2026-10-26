using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NullMenagerie
{
    // A dedicated STA owns the native dialog; the arena remains responsive underneath it.
    public static class NativeFilePicker
    {
        public static Task<string> Pick()
        {
            var result = new TaskCompletionSource<string>();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var thread = new Thread(() =>
            {
                IntPtr fileBuffer = IntPtr.Zero;
                try
                {
                    const int pathCapacity = 32768;
                    // StringBuilder fields are not portable across the CLR and Unity's native marshaller.
                    // OPENFILENAME owns no buffer; keep this UTF-16 allocation alive for the entire call.
                    fileBuffer = Marshal.AllocHGlobal(pathCapacity * sizeof(char));
                    Marshal.WriteInt16(fileBuffer, 0);
                    var data = new OpenFileName();
                    data.size = Marshal.SizeOf(typeof(OpenFileName));
                    data.file = fileBuffer;
                    data.maxFile = pathCapacity;
                    data.filter = "Every file contains a creature\0*.*\0\0";
                    data.title = "NULL MENAGERIE / Discover a specimen";
                    // Explorer, file/path must exist, keep CWD, don't add to Recent, don't follow shortcuts.
                    data.flags = 0x80000 | 0x1000 | 0x800 | 0x8 | 0x2000000 | 0x100000;
                    if (GetOpenFileNameW(data)) result.TrySetResult(Marshal.PtrToStringUni(fileBuffer));
                    else
                    {
                        uint error = CommDlgExtendedError();
                        if (error == 0) result.TrySetResult(null);
                        else result.TrySetException(new InvalidOperationException("Windows picker error " + error + ". Paste a file path instead."));
                    }
                }
                catch (Exception e) { result.TrySetException(e); }
                finally { if (fileBuffer != IntPtr.Zero) Marshal.FreeHGlobal(fileBuffer); }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
#else
            result.SetException(new PlatformNotSupportedException("Use the file-path field on this platform."));
#endif
            return result.Task;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        sealed class OpenFileName
        {
            public int size;
            public IntPtr owner, instance;
            public string filter, customFilter;
            public int maxCustomFilter, filterIndex;
            public IntPtr file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDirectory, title;
            public int flags;
            public short fileOffset, extensionOffset;
            public string defaultExtension;
            public IntPtr customData, hook;
            public string template;
            public IntPtr reserved;
            public int reservedInt, flagsEx;
        }
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetOpenFileNameW([In, Out] OpenFileName data);
        [DllImport("comdlg32.dll")] static extern uint CommDlgExtendedError();
    }
}
