using System.Windows;

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

// Resolve native application libraries beside the executable or from Windows System32.
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(
    System.Runtime.InteropServices.DllImportSearchPath.AssemblyDirectory |
    System.Runtime.InteropServices.DllImportSearchPath.System32)]

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Localay.SecurityChecks")]

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Localay.UiWindowsChecks")]
