using System.Runtime.InteropServices;

// Load native system libraries only from System32 (defence against DLL search-order hijacking).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
