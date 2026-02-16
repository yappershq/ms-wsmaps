using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Sharp.Shared;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;

namespace YappersHQ.WSMaps;

internal sealed class InterfaceBridge
{
    public static InterfaceBridge Instance { get; private set; } = null!;

    public string SharpPath { get; }
    public string RootPath { get; }
    public string DllPath { get; }
    public string DataPath { get; }
    public string ConfigPath { get; }
    public string ModuleIdentity { get; }

    public Version GameVersion { get; }
    public Version Version { get; }
    public FileVersionInfo FileVersion { get; }
    public DateTime FileTime { get; }
    public IConVarManager ConVarManager { get; }
    public IModSharp ModSharp { get; }
    public ISteamApi SteamApi { get; }
    public ILoggerFactory LoggerFactory { get; }
    public WSMapsModule MSModule { get; }

    private readonly ILogger<InterfaceBridge> _logger;

    public InterfaceBridge(
        string dllPath,
        string sharpPath,
        Version version,
        WSMapsModule module,
        ISharedSystem sharedSystem
    )
    {
        Instance = this;

        SharpPath = sharpPath;
        DllPath = dllPath;
        RootPath = Path.GetFullPath(Path.Combine(sharpPath, ".."));
        DataPath = Path.GetFullPath(Path.Combine(sharpPath, "data"));
        ConfigPath = Path.GetFullPath(Path.Combine(sharpPath, "configs"));
        ModuleIdentity = Path.GetFileNameWithoutExtension(dllPath);
        GameVersion = GetGameVersion(sharpPath);
        Version = version;
        MSModule = module;
        ConVarManager = sharedSystem.GetConVarManager();
        ModSharp = sharedSystem.GetModSharp();
        SteamApi = sharedSystem.GetModSharp().GetSteamGameServer();
        LoggerFactory = sharedSystem.GetLoggerFactory();
        FileVersion = FileVersionInfo.GetVersionInfo(Path.Combine(dllPath, "WSMaps.dll"));
        FileTime = GetSelfDBuildTime(dllPath);

        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(ConfigPath);

        _logger = sharedSystem.GetLoggerFactory().CreateLogger<InterfaceBridge>();
    }

    private static Version GetGameVersion(string root)
    {
        const string prefix = "PatchVersion=";

        var patch = Path.Combine(root, "..", "csgo", "steam.inf");

        if (!File.Exists(patch))
        {
            throw new FileNotFoundException("Could not found steam.inf");
        }

        try
        {
            var text = File.ReadAllLines(patch, Encoding.UTF8);

            foreach (var line in text)
            {
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var pv = line.Replace(prefix, "", StringComparison.OrdinalIgnoreCase).TrimEnd();

                    return Version.Parse(pv);
                }
            }

            throw new InvalidDataException("Invalid steam.inf");
        }
        catch (Exception e)
        {
            throw new InvalidDataException("Could not read steam.inf", e);
        }
    }

    private DateTime GetSelfDBuildTime(string dllPath)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (
                    attr.Key.Equals("BuildTime", StringComparison.OrdinalIgnoreCase)
                    && attr.Value is not null
                )
                {
                    return DateTime.Parse(attr.Value);
                }
            }

            throw new TypeAccessException("Could not found BuildTime In [AssemblyMetadata]");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to get timestamp");

            return File.GetLastWriteTime(Path.Combine(dllPath, "WSMaps.dll"));
        }
    }
}
