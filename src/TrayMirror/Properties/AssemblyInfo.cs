using System.Runtime.InteropServices;

// Every P/Invoke in this assembly resolves against a Windows system library, so the search path is
// pinned to System32. Without this, the loader also probes the application directory, which turns a
// DLL dropped next to the executable into a hijack of user32, shell32 or dwmapi.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
