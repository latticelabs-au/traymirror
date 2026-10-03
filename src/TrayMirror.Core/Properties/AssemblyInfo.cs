using System.Runtime.InteropServices;

// Every P/Invoke in this assembly resolves against a Windows system library, so the search path is
// pinned to System32. Without this, the loader also probes the application directory, which turns
// a DLL dropped next to the executable into a hijack of user32 or shcore.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

// The test project asserts against the default config template that ships inside this assembly.
// Exposing that constant publicly would make it part of the library's API for no reason.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TrayMirror.Core.Tests")]
