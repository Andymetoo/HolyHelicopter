using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FileDNALab.Runtime
{
    public static class LocalFilePicker
    {
        public static Task<string> Pick()
        {
#if UNITY_EDITOR
            return Task.FromResult(UnityEditor.EditorUtility.OpenFilePanel("File DNA / read any file as bytes", "", ""));
#elif UNITY_STANDALONE_WIN
            var task=new TaskCompletionSource<string>();
            var thread=new Thread(()=>
            {
                IntPtr buffer=IntPtr.Zero;
                try
                {
                    buffer=Marshal.AllocHGlobal(32768*2);Marshal.WriteInt16(buffer,0);
                    var dialog=new OpenFile { size=Marshal.SizeOf(typeof(OpenFile)),file=buffer,maxFile=32768,
                        filter="All files\0*.*\0\0",title="FILE DNA / choose genetic material",flags=0x80000|0x1000|0x800|0x8|0x2000000|0x100000 };
                    if(GetOpenFileNameW(dialog))task.SetResult(Marshal.PtrToStringUni(buffer));
                    else { uint error=CommDlgExtendedError();if(error==0)task.SetResult(null);else task.SetException(new Exception("File picker error "+error+". Paste a path instead.")); }
                }
                catch(Exception e) {task.TrySetException(e);}finally {if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);}
            });thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();return task.Task;
#else
            return Task.FromException<string>(new PlatformNotSupportedException("Paste an absolute file path on this platform."));
#endif
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        sealed class OpenFile
        {
            public int size;public IntPtr owner,instance;public string filter,customFilter;public int maxCustomFilter,filterIndex;
            public IntPtr file;public int maxFile;public string fileTitle;public int maxFileTitle;public string initialDirectory,title;
            public int flags;public short fileOffset,extensionOffset;public string defaultExtension;public IntPtr customData,hook;
            public string template;public IntPtr reserved;public int reservedInt,flagsEx;
        }
        [DllImport("comdlg32.dll",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool GetOpenFileNameW([In,Out] OpenFile data);
        [DllImport("comdlg32.dll")]static extern uint CommDlgExtendedError();
    }
}
