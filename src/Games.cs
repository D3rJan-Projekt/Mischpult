using System;
using System.Collections.Generic;

namespace Mischpult
{
    /// <summary>
    /// Erkennt Spiele am Installationsort (Steam, Epic, GOG, Riot, Ubisoft, EA, Xbox ...) oder am Programmnamen.
    /// Launcher, Absturz-Melder, Installer und Anti-Cheat-Dienste zaehlen nicht.
    /// </summary>
    internal static class GameDetector
    {
        private static readonly string[] PathMarkers =
        {
            @"\steamapps\common\", @"\epic games\", @"\gog galaxy\games\", @"\gog games\", @"\riot games\",
            @"\ubisoft game launcher\games\", @"\ea games\", @"\origin games\", @"\electronic arts\",
            @"\rockstar games\", @"\xboxgames\", @"\amazon games\library\", @"\itch\apps\",
            @"\bethesda.net launcher\games\", @"\battlestate games\", @"\wargaming.net\", @"\hoyoplay\games\",
            @"\genshin impact\", @"\minecraft", @"\.minecraft\", @"\games\", @"\spiele\",
        };

        private static readonly HashSet<string> KnownExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "valorant-win64-shipping.exe", "fortniteclient-win64-shipping.exe", "cs2.exe", "csgo.exe",
            "league of legends.exe", "r5apex.exe", "r5apex_dx12.exe", "overwatch.exe", "gta5.exe", "gta5_enhanced.exe",
            "rdr2.exe", "rocketleague.exe", "minecraft.windows.exe", "dota2.exe", "eldenring.exe", "cyberpunk2077.exe",
            "witcher3.exe", "destiny2.exe", "tslgame.exe", "escapefromtarkov.exe", "rainbowsix.exe", "rainbowsix_vulkan.exe",
            "cod.exe", "modernwarfare.exe", "bf2042.exe", "wow.exe", "wowclassic.exe", "hearthstone.exe", "diablo iv.exe",
            "sc2_x64.exe", "palworld-win64-shipping.exe", "helldivers2.exe", "robloxplayerbeta.exe", "fallguys_client_game.exe",
            "deadbydaylight-win64-shipping.exe", "rustclient.exe", "marvel-win64-shipping.exe", "eurotrucks2.exe", "amtrucks.exe",
            "factorio.exe", "terraria.exe", "stardew valley.exe", "among us.exe", "genshinimpact.exe", "zenlesszonezero.exe",
            "starrail.exe", "warframe.x64.exe", "pathofexile_x64.exe", "pathofexile.exe", "lostark.exe", "bg3.exe", "bg3_dx11.exe",
            "hogwartslegacy.exe", "forzahorizon5.exe", "forzahorizon4.exe", "flightsimulator.exe", "ts4_x64.exe", "osu!.exe",
            "fc25.exe", "fc24.exe", "thefinals.exe", "discovery.exe", "wuthering waves.exe", "client-win64-shipping.exe",
        };

        private static readonly string[] NotGameParts =
        {
            "launcher", "crash", "report", "setup", "unins", "install", "update", "patch", "redist", "dxsetup",
            "easyanticheat", "eac_", "beservice", "battleye", "anticheat", "vanguard", "helper", "overlay", "webhelper",
            "steam.exe", "steamservice", "epicgames", "galaxyclient", "upc.exe", "uplay", "eadesktop", "origin.exe",
            "battle.net", "riotclient", "leagueclient", "socialclub", "browser", "cefsharp", "webview", "wallpaper",
            "obs64", "obs32", "discord", "spotify", "nvidia", "radeonsoftware", "amdrs", "msedge", "chrome", "firefox",
        };

        public static bool IsGame(string key, string path)
        {
            if (string.IsNullOrEmpty(key) || key.StartsWith("#")) return false;
            string k = key.ToLowerInvariant();
            foreach (var part in NotGameParts) if (k.Contains(part)) return false;
            if (KnownExe.Contains(k)) return true;
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.Replace('/', '\\').ToLowerInvariant();
            if (p.Contains(@"\windows\") || p.Contains(@"\windowsapps\microsoft.")) return false;
            foreach (var m in PathMarkers) if (p.Contains(m)) return true;
            return false;
        }
    }
}
