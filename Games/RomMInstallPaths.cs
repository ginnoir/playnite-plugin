using System.IO;

namespace RomM.Games
{
    // Derives a ROM's install directory and playable path. These MUST come from the actual ROM file
    // name (what gets downloaded), not the display name: using the display name drops the extension
    // and can include characters that don't match the installed file, breaking IsInstalled detection
    // and the play path.
    internal static class RomMInstallPaths
    {
        // <root>/<file name without extension>
        public static string InstallDir(string rootInstallDir, string fileName)
            => Path.Combine(rootInstallDir, Path.GetFileNameWithoutExtension(fileName));

        // <root>/<file name without extension>/<file name>
        public static string GamePath(string rootInstallDir, string fileName)
            => Path.Combine(InstallDir(rootInstallDir, fileName), fileName);
    }
}
