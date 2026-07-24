using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Netvium.Helpers;

namespace Netvium.Native
{
    public static class NativeMethods
    {
        #region Advapi32
        public const int LOGON32_LOGON_NEW_CREDENTIALS = 9;
        public const int LOGON32_PROVIDER_WINNT50 = 3;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool LogonUser(
            string lpszUsername,
            string? lpszDomain,
            string lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out SafeAccessTokenHandle phToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool InitiateSystemShutdownEx(
            string? lpMachineName,
            string? lpMessage,
            uint dwTimeout,
            bool bForceAppsClosed,
            bool bRebootAfterShutdown,
            uint dwReason);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        public static extern int WNetAddConnection2(
            ref NETRESOURCE lpNetResource,
            string? lpPassword,
            string? lpUserName,
            int dwFlags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        public static extern int WNetCancelConnection2(
            string lpName,
            int dwFlags,
            bool fForce);

        public const int RESOURCE_GLOBALNET = 0x00000002;
        public const int RESOURCETYPE_ANY = 0x00000000;
        public const int CONNECT_TEMPORARY = 0x00000004;
        public const int NO_ERROR = 0;
        public const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string? lpLocalName;
            public string? lpRemoteName;
            public string? lpComment;
            public string? lpProvider;
        }
        #endregion

        #region Wtsapi32
        public enum WTS_INFO_CLASS
        {
            WTSInitialProgram, WTSApplicationName, WTSWorkingDirectory, WTSOEMId, WTSSessionId, WTSUserName,
            WTSWinStationName, WTSDomainName, WTSConnectState, WTSClientBuildNumber, WTSClientName,
            WTSClientDirectory, WTSClientProductId, WTSClientHardwareId, WTSClientAddress, WTSClientDisplay,
            WTSClientProtocolType, WTSIdleTime, WTSLogonTime, WTSIncomingBytes, WTSOutgoingBytes,
            WTSIncomingFrames, WTSOutgoingFrames, WTSClientInfo, WTSSessionInfo, WTSSessionInfoEx,
            WTSConfigInfo, WTSValidationInfo, WTSSessionAddressV4, WTSIsRemoteSession
        }

        public enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive, WTSConnected, WTSConnectQuery, WTSShadow, WTSDisconnected, WTSIdle, WTSListen, WTSReset, WTSDown, WTSInit
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WTS_SESSION_INFO
        {
            public int SessionID;
            public IntPtr pWinStationName;
            public WTS_CONNECTSTATE_CLASS State;
        }

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr WTSOpenServer(string pServerName);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSCloseServer(IntPtr hServer);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSEnumerateSessions(IntPtr hServer, int Reserved, int Version, out IntPtr ppSessionInfo, out int pCount);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSFreeMemory(IntPtr pMemory);

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out int pBytesReturned);

        public static string QueryWtsString(IntPtr hServer, int sessionId, WTS_INFO_CLASS cls)
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                if (WTSQuerySessionInformation(hServer, sessionId, cls, out buffer, out var bytes) && buffer != IntPtr.Zero && bytes > 2)
                {
                    return Marshal.PtrToStringUni(buffer) ?? string.Empty;
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            finally
            {
                if (buffer != IntPtr.Zero) WTSFreeMemory(buffer);
            }
            return string.Empty;
        }

        public static string QueryWtsTime(IntPtr server, int sessionId, WTS_INFO_CLASS infoClass, bool isDuration)
        {
            IntPtr buffer = IntPtr.Zero;
            int bytesReturned;
            try
            {
                if (WTSQuerySessionInformation(server, sessionId, infoClass, out buffer, out bytesReturned) && buffer != IntPtr.Zero)
                {
                    long fileTime = Marshal.ReadInt64(buffer);
                    if (fileTime <= 0) return "-";

                    if (isDuration)
                    {
                        var ts = TimeSpan.FromTicks(fileTime);
                        if (ts.TotalSeconds < 1) return ".";
                        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h";
                        if (ts.TotalHours >= 1) return $"{ts.Hours}h {ts.Minutes}m";
                        return $"{ts.Minutes}m {ts.Seconds}s";
                    }
                    else
                    {
                        var dt = DateTime.FromFileTime(fileTime);
                        return dt.ToString("dd.MM HH:mm");
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error("Operation failed.", ex); }
            finally
            {
                if (buffer != IntPtr.Zero) WTSFreeMemory(buffer);
            }
            return "-";
        }
        #endregion

        #region User32
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        public const uint PW_CLIENTONLY = 0x00000001;
        public const uint PW_RENDERFULLCONTENT = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        public static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        public static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        public static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        public static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const long WS_CHILD = 0x40000000L;
        public const long WS_VISIBLE = 0x10000000L;
        public const long WS_CAPTION = 0x00C00000L;
        public const long WS_THICKFRAME = 0x00040000L;
        public const long WS_MINIMIZE = 0x20000000L;
        public const long WS_MAXIMIZE = 0x01000000L;
        public const long WS_SYSMENU = 0x00080000L;
        public const long WS_POPUP = unchecked((long)0x80000000);
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const long WS_EX_APPWINDOW = 0x00040000L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_TOPMOST = 0x00000008L;
        public const long WS_EX_CLIENTEDGE = 0x00000200L;
        public const long WS_EX_STATICEDGE = 0x00020000L;
        public const long WS_EX_DLGMODALFRAME = 0x00000001L;
        #endregion

        #region RDP COM
        public static readonly Guid IID_IMsRdpExtendedSettings = new("302D8188-0052-4807-806A-362B628F9AC5");
        public static readonly Guid IID_IMsTscNonScriptable = new("C1E6743A-41C1-4A74-832A-0DD06C1C7A0E");
        public static readonly Guid IID_IMsRdpClientNonScriptable3 = new("b354ca71-e970-49f3-9d41-923f63004354");
        public static readonly Guid IID_IMsRdpClientNonScriptable5 = new("4f6996d5-d7b1-412c-b0ff-063718566907");

        [ComImport]
        [Guid("302D8188-0052-4807-806A-362B628F9AC5")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMsRdpExtendedSettings
        {
            void put_Property([MarshalAs(UnmanagedType.BStr)] string bstrPropertyName,
                [MarshalAs(UnmanagedType.Struct)] object pValue);

            [return: MarshalAs(UnmanagedType.Struct)]
            object get_Property([MarshalAs(UnmanagedType.BStr)] string bstrPropertyName);
        }

        [ComImport]
        [Guid("2f3a624a-73d7-4931-97f2-11d0e1b9b244")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMsRdpClientNonScriptable
        {
            void SendKeys(int numKeys, ref bool pbKeyInterrupt, ref object pVKeys);
        }

        // IMsTscNonScriptable - The classic and correct password injection interface.
        // IUnknown-based (NOT IDispatch). Correct IID confirmed from mstscax.dll typelib.
        [ComImport]
        [Guid("C1E6743A-41C1-4A74-832A-0DD06C1C7A0E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMsTscNonScriptable
        {
            void NotifyRedirectDeviceChange(int ulDevType, long ulFlags);
            [PreserveSig] int SendKeys(int numKeys, [MarshalAs(UnmanagedType.Bool)] ref bool pbKeyInterrupt, ref int pVKeys);
            string ClearTextPassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string PortablePassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string PortableSalt { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string BinaryPassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string BinarySalt { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            void ResetPassword();
        }

        // IMsRdpClientNonScriptable2 - IID: 17A5E535-4072-4FA4-AF32-C8D0D47345E9
        [ComImport]
        [Guid("17A5E535-4072-4FA4-AF32-C8D0D47345E9")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMsRdpClientNonScriptable2
        {
            // From IMsTscNonScriptable
            void NotifyRedirectDeviceChange(int ulDevType, long ulFlags);
            [PreserveSig] int SendKeys(int numKeys, [MarshalAs(UnmanagedType.Bool)] ref bool pbKeyInterrupt, ref int pVKeys);
            string ClearTextPassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string PortablePassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string PortableSalt { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string BinaryPassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string BinarySalt { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            void ResetPassword();
            // IMsRdpClientNonScriptable adds no new members before NonScriptable2
        }

        // IMsRdpClientNonScriptable3 - extends IMsRdpClientNonScriptable (which extends IMsTscNonScriptable)
        // We need to restate the vtable order from root so QI + vtable dispatch works.
        [ComImport]
        [Guid("b354ca71-e970-49f3-9d41-923f63004354")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMsRdpClientNonScriptable3
        {
            // From IMsTscNonScriptable
            void NotifyRedirectDeviceChange(int ulDevType, long ulFlags);
            [PreserveSig] int SendKeys(int numKeys, [MarshalAs(UnmanagedType.Bool)] ref bool pbKeyInterrupt, ref int pVKeys);
            string ClearTextPassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string PortablePassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string PortableSalt { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string BinaryPassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string BinarySalt { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            void ResetPassword();
        }

        [ComImport]
        [Guid("4f6996d5-d7b1-412c-b0ff-063718566907")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMsRdpClientNonScriptable5
        {
            // From IMsTscNonScriptable
            void NotifyRedirectDeviceChange(int ulDevType, long ulFlags);
            [PreserveSig] int SendKeys(int numKeys, [MarshalAs(UnmanagedType.Bool)] ref bool pbKeyInterrupt, ref int pVKeys);
            string ClearTextPassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string PortablePassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string PortableSalt { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string BinaryPassword { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            string BinarySalt { [return: MarshalAs(UnmanagedType.BStr)] get; [param: MarshalAs(UnmanagedType.BStr)] set; }
            void ResetPassword();

            // From IMsRdpClientNonScriptable (adds prompt)
            void get_UIParent(out IntPtr uiParent);
            void put_UIParent(IntPtr uiParent);

            // From IMsRdpClientNonScriptable2
            bool UseMultimon { get; set; }

            // IMsRdpClientNonScriptable3: no new members in this chain we need
            // From IMsRdpClientNonScriptable4
            bool AllowPromptingForCredentials { get; set; }
        }
        [ComImport]
        [Guid("4773BA16-7E53-4971-8F45-A1A512C87310")]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        public interface IMsTscAxEvents
        {
            [DispId(1)] void OnConnecting();
            [DispId(2)] void OnConnected();
            [DispId(3)] void OnLoginComplete();
            [DispId(4)] void OnDisconnected(int discReason);
            [DispId(10)] void OnReceivedTSPublicKey([MarshalAs(UnmanagedType.BStr)] string publicKey);
            [DispId(12)] void OnAuthenticationWarningDisplayed();
            [DispId(13)] void OnAuthenticationWarningDismissed();
            [DispId(14)] void OnAuthenticationWarning(int code);
            [DispId(15)] void OnRemoteDesktopSizeChange(int width, int height);
            [DispId(17)] void OnWarning(int warningCode);
            [DispId(18)] void OnFatalError(int errorCode);
            [DispId(20)] void OnLogonError(int errorCode);
        }
        #endregion
    }
}
