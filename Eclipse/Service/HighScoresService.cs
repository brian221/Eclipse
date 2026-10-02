using Eclipse.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace Eclipse.Service
{
    /// <summary>
    /// Isolates the two BigBox-API-sensitive concerns for the High Scores feature:
    ///   1. determining whether a game actually supports MAME high scores, and
    ///   2. invoking BigBox's built-in "View High Scores" input action (Req 5/7).
    ///
    /// Availability is NOT a platform-name guess. MAME's own high-score plugin uses
    /// hiscore.dat: a ROM supports high score saving if (and only if) its ROM name
    /// (the short name, e.g. "pacman", "dkong") has an entry in hiscore.dat. This
    /// service resolves the game's emulator, confirms it is MAME, locates that MAME
    /// install's plugins\hiscore\hiscore.dat, parses the set of ROM names it declares
    /// (cached), and checks membership. That mirrors exactly how MAME itself decides
    /// whether a game has high score support.
    /// </summary>
    public sealed class HighScoresService
    {
        // Cache of ROM names parsed from a given hiscore.dat path, keyed by the dat
        // file's full path. Keyed by path so multiple MAME installs are handled, and
        // invalidated when the file's last-write time changes.
        private readonly Dictionary<string, CachedHiscoreData> hiscoreCacheByPath =
            new Dictionary<string, CachedHiscoreData>(StringComparer.OrdinalIgnoreCase);

        private readonly object cacheLock = new object();

        private sealed class CachedHiscoreData
        {
            public DateTime LastWriteTimeUtc { get; set; }
            public HashSet<string> RomNames { get; set; }
        }

        /// <summary>
        /// Req 6: a game supports high scores when it is a MAME game whose ROM name is
        /// present in that MAME install's hiscore.dat. A null game, a non-MAME game, a
        /// missing hiscore.dat, or a ROM name not listed are all treated as not available.
        /// Never throws to its caller.
        /// </summary>
        public bool IsHighScoresAvailable(IGame game)
        {
            try
            {
                if (game == null || string.IsNullOrWhiteSpace(game.ApplicationPath))
                {
                    return false;
                }

                IEmulator emulator = ResolveEmulator(game);
                if (!IsMameEmulator(emulator))
                {
                    return false;
                }

                string hiscoreDatPath = GetHiscoreDatPath(emulator);
                if (string.IsNullOrEmpty(hiscoreDatPath) || !File.Exists(hiscoreDatPath))
                {
                    return false;
                }

                string romName = GetRomName(game);
                if (string.IsNullOrWhiteSpace(romName))
                {
                    return false;
                }

                HashSet<string> romNames = GetSupportedRomNames(hiscoreDatPath);
                return romNames != null && romNames.Contains(romName);
            }
            catch (Exception ex)
            {
                LogHelper.LogException(ex, "HighScoresService.IsHighScoresAvailable");
                return false;
            }
        }

        /// <summary>
        /// The ROM name is the application path's file name without extension
        /// (e.g. "...\roms\pacman.zip" -> "pacman"), matching the short name MAME and
        /// hiscore.dat use.
        /// </summary>
        private static string GetRomName(IGame game)
        {
            try
            {
                return Path.GetFileNameWithoutExtension(game.ApplicationPath)?.Trim();
            }
            catch (Exception ex)
            {
                LogHelper.LogException(ex, "HighScoresService.GetRomName");
                return null;
            }
        }

        private static IEmulator ResolveEmulator(IGame game)
        {
            if (string.IsNullOrWhiteSpace(game.EmulatorId))
            {
                return null;
            }

            return PluginHelper.DataManager?.GetEmulatorById(game.EmulatorId);
        }

        private static bool IsMameEmulator(IEmulator emulator)
        {
            if (emulator == null)
            {
                return false;
            }

            // Identify MAME by the emulator executable name or title. This covers the
            // common "mame", "mame64", "mamed" variants without being tied to a single
            // configured title.
            string exeName = null;
            try
            {
                exeName = Path.GetFileNameWithoutExtension(emulator.ApplicationPath);
            }
            catch
            {
                // ignore - fall back to title check
            }

            if (!string.IsNullOrWhiteSpace(exeName) &&
                exeName.IndexOf("mame", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(emulator.Title) &&
                   emulator.Title.IndexOf("mame", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// hiscore.dat lives at &lt;mame-root&gt;\plugins\hiscore\hiscore.dat, where the
        /// MAME root is the directory containing the MAME executable.
        /// </summary>
        private static string GetHiscoreDatPath(IEmulator emulator)
        {
            try
            {
                string emulatorExePath = emulator?.ApplicationPath;
                if (string.IsNullOrWhiteSpace(emulatorExePath))
                {
                    return null;
                }

                string mameRoot = Path.GetDirectoryName(emulatorExePath);
                if (string.IsNullOrWhiteSpace(mameRoot))
                {
                    return null;
                }

                return Path.Combine(mameRoot, "plugins", "hiscore", "hiscore.dat");
            }
            catch (Exception ex)
            {
                LogHelper.LogException(ex, "HighScoresService.GetHiscoreDatPath");
                return null;
            }
        }

        /// <summary>
        /// Returns the set of ROM names declared in the given hiscore.dat, parsed and
        /// cached per path. The cache is invalidated when the file's last-write time
        /// changes so a user updating hiscore.dat is picked up without a restart.
        /// </summary>
        private HashSet<string> GetSupportedRomNames(string hiscoreDatPath)
        {
            DateTime lastWriteUtc;
            try
            {
                lastWriteUtc = File.GetLastWriteTimeUtc(hiscoreDatPath);
            }
            catch (Exception ex)
            {
                LogHelper.LogException(ex, "HighScoresService.GetSupportedRomNames (stat)");
                return null;
            }

            lock (cacheLock)
            {
                if (hiscoreCacheByPath.TryGetValue(hiscoreDatPath, out CachedHiscoreData cached)
                    && cached.LastWriteTimeUtc == lastWriteUtc)
                {
                    return cached.RomNames;
                }
            }

            HashSet<string> romNames = ParseHiscoreDat(hiscoreDatPath);
            if (romNames == null)
            {
                return null;
            }

            lock (cacheLock)
            {
                hiscoreCacheByPath[hiscoreDatPath] = new CachedHiscoreData
                {
                    LastWriteTimeUtc = lastWriteUtc,
                    RomNames = romNames
                };
            }

            return romNames;
        }

        /// <summary>
        /// Parses hiscore.dat and returns the set of ROM names it declares.
        ///
        /// Format (per the file header): entries are separated by a colon. A ROM name
        /// is a line of the form "&lt;romname&gt;:" (optionally followed by a ";" comment).
        /// Lines beginning with ";" are comments; lines of the form
        /// "cpu:address:length:first:last" are memory descriptors (they contain multiple
        /// colons and their leading token is numeric/hex, not a ROM name). Several ROM
        /// names may precede a single shared memory block, each on its own line.
        /// </summary>
        private static HashSet<string> ParseHiscoreDat(string hiscoreDatPath)
        {
            try
            {
                HashSet<string> romNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string rawLine in File.ReadLines(hiscoreDatPath))
                {
                    string line = rawLine?.Trim();
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    // Comment line
                    if (line.StartsWith(";"))
                    {
                        continue;
                    }

                    // Strip any trailing inline comment (e.g. "knockout:  ;******...")
                    int commentIndex = line.IndexOf(';');
                    if (commentIndex >= 0)
                    {
                        line = line.Substring(0, commentIndex).Trim();
                        if (line.Length == 0)
                        {
                            continue;
                        }
                    }

                    // A ROM-name declaration ends with ':' and contains exactly one ':'
                    // (the trailing one). Memory descriptor lines like
                    // "0:4688:3:00:00" contain multiple colons and are not ROM names.
                    if (!line.EndsWith(":"))
                    {
                        continue;
                    }

                    string name = line.Substring(0, line.Length - 1).Trim();
                    if (name.Length == 0 || name.Contains(":"))
                    {
                        // empty, or still has colons -> a data line that happened to end
                        // in ':' is not a valid single rom name; skip.
                        continue;
                    }

                    romNames.Add(name);
                }

                return romNames;
            }
            catch (Exception ex)
            {
                LogHelper.LogException(ex, "HighScoresService.ParseHiscoreDat");
                return null;
            }
        }

        /// <summary>
        /// Req 5/7: open BigBox's native High Scores view for the current selection by
        /// invoking BigBox's built-in "View High Scores" input action. Wrapped in
        /// try/catch so it never throws to its caller; returns true on success and
        /// false (after logging) if the action could not be invoked, so the caller
        /// can route to the error display.
        /// </summary>
        public bool OpenHighScores()
        {
            try
            {
                // BigBox has no exported API to open the high scores view directly;
                // instead it exposes a built-in "View High Scores" input action bound
                // (by default) to the H key on the game detail view. Invoking that
                // input action is the supported way to open the native high scores
                // view, and keeping the exact mechanism here isolates it so it can
                // evolve without touching the state machine or view model.
                SendKeys.SendWait("H");
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.LogException(ex, "HighScoresService.OpenHighScores");
                return false;
            }
        }

        #region singleton implementation
        public static HighScoresService Instance => instance;

        private static readonly HighScoresService instance = new HighScoresService();

        static HighScoresService()
        {
        }

        private HighScoresService()
        {
        }
        #endregion
    }
}
