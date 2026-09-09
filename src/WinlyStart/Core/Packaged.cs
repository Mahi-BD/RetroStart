using System.Runtime.InteropServices;
using System.Text;

namespace WinlyStart.Core;

/// <summary>
/// Whether this process is running with MSIX package identity.
///
/// The Store rejects an unsigned .exe (policy 10.2.9) but code-signs MSIX packages for free, so the
/// same binary has to work both ways: installed by the Inno Setup installer, and installed from the
/// Store as an MSIX. The two differ in exactly one place — autostart. A packaged app must not write
/// <c>HKCU\…\CurrentVersion\Run</c> (the write is redirected into the package's private hive, so it
/// silently does nothing); startup is declared by the <c>windows.startupTask</c> extension in the
/// manifest and owned by the user through Windows Settings.
/// </summary>
internal static class Packaged
{
    // GetCurrentPackageFullName exists from Windows 8; APPMODEL_ERROR_NO_PACKAGE (15700) is the
    // documented answer for an unpackaged process.
    private const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref int length, StringBuilder? fullName);

    private static bool? _is;

    /// <summary>True when the app was launched from an installed MSIX package.</summary>
    public static bool Is => _is ??= Detect();

    private static bool Detect()
    {
        try
        {
            int len = 0;
            return GetCurrentPackageFullName(ref len, null) != AppModelErrorNoPackage;
        }
        catch
        {
            // Ancient Windows without the export: treat as unpackaged, which is the safe default
            // (the Run key is written, exactly as it always was).
            return false;
        }
    }
}
