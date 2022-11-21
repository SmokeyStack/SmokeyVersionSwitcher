using System;
using System.Runtime.InteropServices;

namespace SmokeyVersionSwitcher
{
    class WUTokenHelper
    {
        public static string GetWUToken()
        {
            int status = GetWUToken(out string token);
            if (status >= WU_ERRORS_START && status <= WU_ERRORS_END)
                throw new WUTokenException(status);
            else if (status != 0)
                Marshal.ThrowExceptionForHR(status);
            return token;
        }

        private const int WU_ERRORS_START = 0x7ffc0200;
        private const int WU_NO_ACCOUNT = 0x7ffc0200;
        private const int WU_ERRORS_END = 0x7ffc0200;

        [DllImport("WUTokenHelper.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int GetWUToken([MarshalAs(UnmanagedType.LPWStr)] out string token);

        public class WUTokenException : Exception
        {
            public WUTokenException(int exception) : base(GetExceptionText(exception))
            {
                HResult = exception;
            }
            private static String GetExceptionText(int e)
            {
                switch (e)
                {
                    case WU_NO_ACCOUNT: return "No account";
                    default: return "Unknown " + e;
                }
            }
        }

    }
}
