using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sharp.Shared;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Types;

namespace YappersHQ.WSMaps;

public sealed class WSMapsModule : IModSharpModule, ISteamListener, IGameListener
{
    public string DisplayName   => "Workshop Maps Downloader";
    public string DisplayAuthor => "Prefix";

    private readonly ILogger<WSMapsModule> _logger;
    private readonly InterfaceBridge bridge;

    private List<MapEntry> _workshopMaps = [];
    private DownloadCycler _cycler = null!;
    private string? _defaultMap;
    private bool _randomMap;
    private bool _emptyMapSwitcher;
    private Guid _switcherTimer;

    private const double SwitcherIntervalSeconds = 900.0;

    private string MaplistPath => Path.Combine(bridge.SharpPath, "configs", "wsmaps", "maplist.json");
    private string ConfigPath  => Path.Combine(bridge.SharpPath, "configs", "wsmaps", "config.json");

    public WSMapsModule(ISharedSystem sharedSystem,
        string                 dllPath,
        string                 sharpPath,
        Version                version,
        IConfiguration         coreConfiguration,
        bool                   hotReload)
    {
        _logger = sharedSystem.GetLoggerFactory().CreateLogger<WSMapsModule>();
        bridge = new(dllPath, sharpPath, version, this, sharedSystem);
    }

    public bool Init()
    {
        if (!File.Exists(MaplistPath))
        {
            _logger.LogWarning("maplist.json not found at {Path}, no workshop maps to manage", MaplistPath);
        }
        else
        {
            try
            {
                var json = File.ReadAllText(MaplistPath);
                var allMaps = JsonSerializer.Deserialize<List<MapEntry>>(json, ConfigExporter.JsonOptions);

                if (allMaps is null)
                {
                    _logger.LogWarning("Failed to deserialize maplist.json");
                }
                else
                {
                    _workshopMaps = allMaps.FindAll(m => m.WorkshopId > 0);
                    _logger.LogInformation("Loaded {Count} workshop maps from maplist.json", _workshopMaps.Count);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to parse maplist.json");
            }
        }

        if (File.Exists(ConfigPath))
        {
            try
            {
                var configJson = File.ReadAllText(ConfigPath);
                var root = JsonSerializer.Deserialize<JsonElement>(configJson, ConfigExporter.JsonOptions);

                if (root.TryGetProperty("DefaultMap", out var mapProp) && mapProp.ValueKind == JsonValueKind.String)
                {
                    var value = mapProp.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        _defaultMap = value;
                        _logger.LogInformation("Default map configured: {DefaultMap}", _defaultMap);
                    }
                }

                if (root.TryGetProperty("RandomMap", out var randomProp)
                    && randomProp.ValueKind == JsonValueKind.True)
                {
                    _randomMap = true;
                    _logger.LogInformation("Random default map enabled");
                }

                if (root.TryGetProperty("EmptyMapSwitcher", out var switcherProp)
                    && switcherProp.ValueKind == JsonValueKind.True)
                {
                    _emptyMapSwitcher = true;
                    _logger.LogInformation("Empty map switcher enabled (interval: {Seconds}s)",
                        SwitcherIntervalSeconds);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to parse config.json");
            }
        }

        _cycler = new DownloadCycler(bridge, _logger, _workshopMaps, MaplistPath, ApplyWorkshopMapgroup);
        return true;
    }

    public void PostInit()
    {
        bridge.ModSharp.InstallSteamListener(this);
        bridge.ModSharp.InstallGameListener(this);

        bridge.ConVarManager.CreateServerCommand(
            "ms_wsmaps_download",
            OnForceDownloadCommand,
            "Force re-download all workshop maps",
            ConVarFlags.Release);

        bridge.ConVarManager.CreateServerCommand(
            "ms_wsmaps_gamemodes",
            OnGamemodesCommand,
            "Generate gamemodes_server.txt with workshop maps",
            ConVarFlags.Release);

        bridge.ConVarManager.CreateServerCommand(
            "ms_wsmaps_maplist",
            OnMaplistCommand,
            "Generate maplist.jsonc for MapManager with workshop maps",
            ConVarFlags.Release);

        if (_emptyMapSwitcher)
            ScheduleEmptyMapSwitch();
    }

    public void Shutdown()
    {
        CancelSwitcherTimer();
        _cycler.Cleanup();
        bridge.ModSharp.RemoveSteamListener(this);
        bridge.ModSharp.RemoveGameListener(this);
        bridge.ConVarManager.ReleaseCommand("ms_wsmaps_download");
        bridge.ConVarManager.ReleaseCommand("ms_wsmaps_gamemodes");
        bridge.ConVarManager.ReleaseCommand("ms_wsmaps_maplist");
    }

#region IGameListener

    int IGameListener.ListenerVersion => IGameListener.ApiVersion;
    int IGameListener.ListenerPriority => 0;

    void IGameListener.OnServerActivate()
    {
        if (_workshopMaps.Count == 0)
            return;

        _cycler.OnMapLoaded(ResolveDefaultMap());
    }

#endregion

#region ISteamListener

    int ISteamListener.ListenerVersion => ISteamListener.ApiVersion;
    int ISteamListener.ListenerPriority => 0;

    void ISteamListener.OnItemInstalled(ulong publishedFileId)
    {
        _logger.LogInformation("Workshop map {Id} installed", publishedFileId);
    }

    void ISteamListener.OnDownloadItemResult(ulong sharedFileId, SteamApiResult result)
    {
        if (result == SteamApiResult.Success)
        {
            _logger.LogInformation("Workshop map {Id} download succeeded", sharedFileId);
            return;
        }

        _logger.LogError("Workshop map {Id} download failed: {Result}", sharedFileId, result);
        _cycler.OnDownloadFailed(sharedFileId);
    }

#endregion

#region Commands

    private ECommandAction OnForceDownloadCommand(StringCommand command)
    {
        if (_workshopMaps.Count == 0)
        {
            _logger.LogWarning("No workshop maps configured");
            return ECommandAction.Stopped;
        }

        _logger.LogInformation("Force downloading all {Count} workshop maps", _workshopMaps.Count);
        _cycler.ForceDownloadAll();
        return ECommandAction.Stopped;
    }

    private ECommandAction OnGamemodesCommand(StringCommand command)
    {
        var path = Path.Combine(bridge.RootPath, "csgo", "gamemodes_server.txt");
        ConfigExporter.GenerateGamemodes(_workshopMaps, path, _logger);
        return ECommandAction.Stopped;
    }

    private ECommandAction OnMaplistCommand(StringCommand command)
    {
        var path = Path.Combine(bridge.SharpPath, "configs", "mapmanager", "maplist.jsonc");
        ConfigExporter.GenerateMapManagerList(_workshopMaps, path, _logger);
        return ECommandAction.Stopped;
    }

#endregion

    private void ScheduleEmptyMapSwitch()
    {
        _switcherTimer = bridge.ModSharp.PushTimer(() =>
        {
            if (!_cycler.IsCycling)
            {
                var clients = bridge.ModSharp.GetIServer().GetGameClients(true, true);

                if (clients.Count == 0)
                {
                    var currentMap = bridge.ModSharp.GetMapName();

                    if (!string.IsNullOrEmpty(currentMap))
                    {
                        _logger.LogInformation("Empty server, reloading map {Map} to prevent desync", currentMap);

                        var isWorkshop = _workshopMaps.Exists(m =>
                            string.Equals(m.MapName, currentMap, StringComparison.OrdinalIgnoreCase));

                        bridge.ModSharp.ServerCommand(isWorkshop
                            ? $"ds_workshop_changelevel {currentMap}"
                            : $"changelevel {currentMap}");
                    }
                }
            }

            ScheduleEmptyMapSwitch();
        }, SwitcherIntervalSeconds);
    }

    private void CancelSwitcherTimer()
    {
        if (bridge.ModSharp.IsValidTimer(_switcherTimer))
        {
            bridge.ModSharp.StopTimer(_switcherTimer);
            _switcherTimer = Guid.Empty;
        }
    }

    private string? ResolveDefaultMap()
    {
        if (!_randomMap && string.IsNullOrWhiteSpace(_defaultMap))
            return null;

        if (_randomMap)
        {
            _randomMap = false;
            _defaultMap = null;

            var resolved = _workshopMaps.FindAll(m => !string.IsNullOrEmpty(m.MapName));

            if (resolved.Count == 0)
            {
                _logger.LogWarning("RandomMap enabled but no workshop maps have resolved names yet");
                return null;
            }

            var pick = resolved[Random.Shared.Next(resolved.Count)];
            _logger.LogInformation("Random default map selected: {MapName}", pick.MapName);
            return pick.MapName;
        }

        var setting = _defaultMap;
        _defaultMap = null;
        return setting;
    }

    private void ApplyWorkshopMapgroup()
    {
        var path = Path.Combine(bridge.RootPath, "csgo", "gamemodes_server.txt");
        ConfigExporter.GenerateGamemodes(_workshopMaps, path, _logger);
        bridge.ModSharp.ServerCommand("mapgroup workshop");
        _logger.LogInformation("Set mapgroup to workshop");
    }
}
