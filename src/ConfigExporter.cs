using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace YappersHQ.WSMaps;

internal static class ConfigExporter
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static JsonSerializerOptions JsonOptions => s_jsonOptions;

    public static void SaveMaplist(List<MapEntry> maps, string path, ILogger logger)
    {
        try
        {
            var json = JsonSerializer.Serialize(maps, s_jsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            logger.LogInformation("Saved maplist with resolved map names to {Path}", path);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to save maplist.json");
        }
    }

    public static void GenerateGamemodes(List<MapEntry> maps, string path, ILogger logger)
    {
        var resolved = maps.FindAll(m => !string.IsNullOrEmpty(m.MapName));

        if (resolved.Count == 0)
        {
            logger.LogWarning("No workshop maps with resolved names, cannot generate gamemodes_server.txt");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("\"GameModes_Server.txt\"");
        sb.AppendLine("{");
        sb.AppendLine("    \"mapgroups\"");
        sb.AppendLine("    {");
        sb.AppendLine("        \"workshop\"");
        sb.AppendLine("        {");
        sb.AppendLine("            \"name\"    \"workshop\"");
        sb.AppendLine("            \"maps\"");
        sb.AppendLine("            {");

        foreach (var map in resolved)
            sb.AppendLine($"                \"{map.MapName}\"    \"\"");

        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        try
        {
            File.WriteAllText(path, sb.ToString());
            logger.LogInformation("Generated gamemodes_server.txt with {Count} maps at {Path}",
                resolved.Count, path);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to write gamemodes_server.txt");
        }
    }

    public static void GenerateMapManagerList(List<MapEntry> maps, string path, ILogger logger)
    {
        var resolved = maps.FindAll(m => !string.IsNullOrEmpty(m.MapName));

        if (resolved.Count == 0)
        {
            logger.LogWarning("No workshop maps with resolved names. Run ms_wsmaps_download first");
            return;
        }

        var entries = resolved.ConvertAll(m => new
        {
            m.MapName,
            m.WorkshopId,
            IsWorkshopMap = true
        });

        try
        {
            var json = JsonSerializer.Serialize(entries, s_jsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            logger.LogInformation("Generated maplist.jsonc with {Count} maps at {Path}",
                resolved.Count, path);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to write maplist.jsonc");
        }
    }
}
