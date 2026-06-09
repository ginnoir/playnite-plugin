using System;
using System.IO;
using System.Threading;

namespace RomM.SaveSync
{
    /// <summary>
    /// Crash-safe local file operations for saves: always back up before overwriting, and write via a
    /// temp file + atomic rename so a partial/cancelled write can never leave a half-written save.
    /// </summary>
    internal static class SafeFile
    {
        /// <summary>Atomically write <paramref name="bytes"/> to <paramref name="path"/>, backing up any existing file first.</summary>
        public static void WriteAtomic(string path, byte[] bytes, string backupRoot)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(path) && !string.IsNullOrEmpty(backupRoot))
            {
                Backup(path, backupRoot);
            }

            var tmp = path + ".romm_tmp_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                File.WriteAllBytes(tmp, bytes);
                // File.Replace requires an existing destination; use it when present for true atomicity,
                // otherwise a plain move (the destination doesn't exist yet so there's nothing to clobber).
                if (File.Exists(path))
                {
                    File.Replace(tmp, path, null);
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            finally
            {
                TryDelete(tmp);
            }
        }

        /// <summary>Copy an existing file into the backup store as "<name>.<UTC timestamp>.bak". Returns the backup path or null.</summary>
        public static string Backup(string sourcePath, string backupRoot)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return null;
                }
                Directory.CreateDirectory(backupRoot);
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var backupPath = Path.Combine(backupRoot, $"{Path.GetFileName(sourcePath)}.{stamp}.bak");
                File.Copy(sourcePath, backupPath, overwrite: true);
                return backupPath;
            }
            catch
            {
                return null;
            }
        }

        public static byte[] ReadAllBytesShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// <summary>True if a save file is currently locked for writing (emulator still running). Best-effort.</summary>
        public static bool IsLocked(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                    }
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
                catch
                {
                    return;
                }
            }
        }
    }
}
