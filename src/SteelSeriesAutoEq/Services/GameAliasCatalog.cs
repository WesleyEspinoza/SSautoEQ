using SteelSeriesAutoEq.Models;

namespace SteelSeriesAutoEq.Services;

/// <summary>
/// Curated process/window → profile keyword map for games whose exe names
/// don't resemble Sonar config titles (e.g. cs2.exe → "CS2 Pro Preset").
/// </summary>
public static class GameAliasCatalog
{
    public sealed record GameAlias(
        string DisplayName,
        IReadOnlyList<string> ProcessNames,
        IReadOnlyList<string> WindowHints,
        IReadOnlyList<string> ProfileKeywords);

    public static IReadOnlyList<GameAlias> All { get; } =
    [
        new("Counter-Strike 2",
            ["cs2", "cs2.exe", "csgo", "csgo.exe"],
            ["counterstrike2", "counterstrike", "counter-strike 2"],
            ["cs2", "counterstrike2", "counterstrike"]),

        new("Valorant",
            ["valorant", "valorant.exe", "valorant-win64-shipping", "valorant-win64-shipping.exe"],
            ["valorant"],
            ["valorant"]),

        new("Fortnite",
            ["fortniteclient-win64-shipping", "fortniteclient-win64-shipping.exe", "fortnite"],
            ["fortnite"],
            ["fortnite"]),

        new("Apex Legends",
            ["r5apex", "r5apex.exe", "r5apex_dx12", "r5apex_dx12.exe"],
            ["apexlegends", "apex legends"],
            ["apexlegends", "apex"]),

        new("Overwatch",
            ["overwatch", "overwatch.exe", "overwatch 2"],
            ["overwatch"],
            ["overwatch"]),

        new("League of Legends",
            ["league of legends", "league of legends.exe", "leagueclient", "leagueclient.exe", "leagueclientux"],
            ["leagueoflegends"],
            ["leagueoflegends", "league"]),

        new("Rocket League",
            ["rocketleague", "rocketleague.exe"],
            ["rocketleague"],
            ["rocketleague"]),

        new("Call of Duty",
            ["cod", "cod.exe", "modernwarfare", "modernwarfare.exe", "codmw", "blackops"],
            ["callofduty", "warzone", "modernwarfare"],
            ["callofduty", "warzone", "modernwarfare", "blackops"]),

        new("Minecraft",
            ["minecraft", "minecraft.exe", "minecraftlauncher", "minecraftlauncher.exe", "javaw", "javaw.exe", "minecraft.windows"],
            ["minecraft"],
            ["minecraft"]),

        new("Grand Theft Auto V",
            ["gta5", "gta5.exe", "playgtav", "playgtav.exe", "gtavlauncher"],
            ["grandtheftautov", "gta v", "gtav"],
            ["gtav", "gta5", "grandtheftauto"]),

        new("Rainbow Six Siege",
            ["rainbowsix", "rainbowsix.exe", "rainbowsix_vulkan", "rainbowsix_vulkan.exe"],
            ["rainbowsix", "rainbow six", "siege"],
            ["rainbowsix", "rainbow6", "siege"]),

        new("Destiny 2",
            ["destiny2", "destiny2.exe"],
            ["destiny2", "destiny 2"],
            ["destiny2", "destiny"]),

        new("Elden Ring",
            ["eldenring", "eldenring.exe"],
            ["eldenring", "elden ring"],
            ["eldenring"]),

        new("Dota 2",
            ["dota2", "dota2.exe"],
            ["dota2", "dota 2"],
            ["dota2", "dota"]),

        new("PUBG",
            ["tslgame", "tslgame.exe"],
            ["pubg", "battlegrounds"],
            ["pubg", "battlegrounds"]),

        new("Roblox",
            ["robloxplayerbeta", "robloxplayerbeta.exe", "roblox", "roblox.exe"],
            ["roblox"],
            ["roblox"]),

        new("Escape from Tarkov",
            ["escapefromtarkov", "escapefromtarkov.exe", "escapefromtarkov_be"],
            ["tarkov", "escapefromtarkov"],
            ["tarkov", "escapefromtarkov"]),

        new("Helldivers 2",
            ["helldivers2", "helldivers2.exe"],
            ["helldivers"],
            ["helldivers"]),

        new("Marvel Rivals",
            ["marvelrivals", "marvelrivals.exe", "marvel-win64-shipping"],
            ["marvelrivals", "marvel rivals"],
            ["marvelrivals", "marvel"]),

        new("Rust",
            ["rustclient", "rustclient.exe"],
            ["rust"],
            ["rust"]),

        new("Team Fortress 2",
            ["tf_win64", "tf_win64.exe", "hl2", "hl2.exe"],
            ["teamfortress", "team fortress"],
            ["teamfortress", "tf2"]),

        new("Warframe",
            ["warframe", "warframe.exe", "warframe.x64"],
            ["warframe"],
            ["warframe"]),

        new("Path of Exile",
            ["pathofexile", "pathofexile.exe", "pathofexile_x64", "pathofexileSteam"],
            ["pathofexile"],
            ["pathofexile", "poe"]),

        new("Diablo IV",
            ["diablo iv", "diablo iv.exe", "diabloiv"],
            ["diabloiv", "diablo 4", "diablo iv"],
            ["diabloiv", "diablo4", "diablo"]),

        new("World of Warcraft",
            ["wow", "wow.exe", "wowclassic"],
            ["worldofwarcraft", "warcraft"],
            ["worldofwarcraft", "wow"]),

        new("Final Fantasy XIV",
            ["ffxiv_dx11", "ffxiv_dx11.exe", "ffxiv"],
            ["finalfantasy", "ffxiv"],
            ["ffxiv", "finalfantasy"]),

        new("Lost Ark",
            ["lostark", "lostark.exe"],
            ["lostark"],
            ["lostark"]),

        new("Black Desert",
            ["blackdesert64", "blackdesert64.exe", "blackdesert"],
            ["blackdesert"],
            ["blackdesert"]),

        new("Hunt: Showdown",
            ["huntgame", "huntgame.exe"],
            ["huntshowdown", "hunt: showdown"],
            ["huntshowdown", "hunt"]),

        new("Dead by Daylight",
            ["deadbydaylight", "deadbydaylight-win64-shipping", "deadbydaylight-egs-shipping"],
            ["deadbydaylight"],
            ["deadbydaylight", "dbd"]),

        new("The Finals",
            ["discovery", "discovery.exe"],
            ["thefinals", "the finals"],
            ["thefinals", "finals"]),

        new("Delta Force",
            ["deltaforceclient", "deltaforceclient-win64-shipping"],
            ["deltaforce"],
            ["deltaforce"]),

        new("Battlefield",
            ["bf2042", "bf2042.exe", "bfv", "bf1"],
            ["battlefield"],
            ["battlefield", "bf2042"]),

        new("Cyberpunk 2077",
            ["cyberpunk2077", "cyberpunk2077.exe"],
            ["cyberpunk"],
            ["cyberpunk"]),

        new("Red Dead Redemption 2",
            ["rdr2", "rdr2.exe", "playrdr2"],
            ["reddead", "rdr2"],
            ["reddead", "rdr2"]),

        new("Baldur's Gate 3",
            ["bg3", "bg3.exe", "bg3_dx11"],
            ["baldursgate", "baldur"],
            ["baldursgate", "bg3"]),

        new("Palworld",
            ["palworld-win64-shipping", "palworld"],
            ["palworld"],
            ["palworld"]),

        new("Once Human",
            ["oncehuman", "once_human"],
            ["oncehuman"],
            ["oncehuman"]),

        new("StarCraft II",
            ["sc2", "sc2.exe", "sc2_x64"],
            ["starcraft"],
            ["starcraft", "sc2"]),

        new("Hearthstone",
            ["hearthstone", "hearthstone.exe"],
            ["hearthstone"],
            ["hearthstone"]),
    ];

    public static GameAlias? FindAlias(ForegroundAppInfo app)
    {
        var processBase = TextNormalizer.Normalize(TextNormalizer.StripExtension(app.ExecutableName));
        var processFull = TextNormalizer.Normalize(app.ExecutableName);
        var windowNorm = TextNormalizer.Normalize(app.WindowTitle);

        foreach (var alias in All)
        {
            foreach (var process in alias.ProcessNames)
            {
                var p = TextNormalizer.Normalize(process);
                if (string.IsNullOrEmpty(p))
                {
                    continue;
                }

                if (p == processBase || p == processFull)
                {
                    return alias;
                }
            }

            foreach (var hint in alias.WindowHints)
            {
                var h = TextNormalizer.Normalize(hint);
                if (string.IsNullOrEmpty(h) || h.Length < 3)
                {
                    continue;
                }

                if (windowNorm == h ||
                    windowNorm.Contains(h, StringComparison.Ordinal) ||
                    h.Contains(windowNorm, StringComparison.Ordinal) && windowNorm.Length >= 4)
                {
                    return alias;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Pick the best Sonar profile for an alias (keyword in name, prefer starts-with).
    /// </summary>
    public static SonarProfile? FindBestProfile(GameAlias alias, IReadOnlyList<SonarProfile> profiles)
    {
        SonarProfile? best = null;
        var bestScore = -1.0;

        foreach (var profile in profiles)
        {
            var name = profile.NormalizedName;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            foreach (var keywordRaw in alias.ProfileKeywords)
            {
                var keyword = TextNormalizer.Normalize(keywordRaw);
                if (string.IsNullOrEmpty(keyword) || keyword.Length < 2)
                {
                    continue;
                }

                if (!name.Contains(keyword, StringComparison.Ordinal))
                {
                    continue;
                }

                // Score: starts with keyword >> contains; shorter names win ties;
                // longer keywords beat shorter ones (cs2 > cs).
                var score = (double)keyword.Length;
                if (name.StartsWith(keyword, StringComparison.Ordinal))
                {
                    score += 100;
                }

                score += 20.0 / Math.Max(name.Length, 1);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = profile;
                }
            }
        }

        return best;
    }
}
