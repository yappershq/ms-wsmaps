using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;

namespace YappersHQ.WSMaps;

internal sealed class DownloadCycler
{
    private readonly InterfaceBridge _bridge;
    private readonly ILogger _logger;
    private readonly List<MapEntry> _maps;
    private readonly Action<bool> _onCycleComplete;
    private readonly string _maplistPath;

    private readonly double _cycleTimeoutSeconds;

    private readonly Queue<MapEntry> _downloadQueue = new();
    private MapEntry? _currentCycleMap;
    private string? _returnMap;
    private Guid _timeoutTimer;
    private bool _firstActivate = true;

    public bool IsCycling { get; private set; }

    public DownloadCycler(
        InterfaceBridge bridge,
        ILogger logger,
        List<MapEntry> maps,
        string maplistPath,
        Action<bool> onCycleComplete,
        double cycleTimeoutSeconds = 600.0)
    {
        _bridge = bridge;
        _logger = logger;
        _maps = maps;
        _maplistPath = maplistPath;
        _onCycleComplete = onCycleComplete;
        _cycleTimeoutSeconds = cycleTimeoutSeconds;
    }

    public void StartMissingDownloads(string? defaultMap = null)
    {
        foreach (var map in _maps)
        {
            var state = _bridge.SteamApi.GetItemState(map.WorkshopId);
            var installed = (state & WorkshopItemState.ItemStateInstalled) != 0;

            if (installed && !string.IsNullOrEmpty(map.MapName))
            {
                _logger.LogInformation("Workshop map {Id} ({MapName}) already installed, skipping",
                    map.WorkshopId, map.MapName);
                continue;
            }

            if (installed)
                _logger.LogInformation("Workshop map {Id} installed but name unknown, queuing", map.WorkshopId);
            else
                _logger.LogInformation("Workshop map {Id} not installed, queuing (state: {State})",
                    map.WorkshopId, state);

            _downloadQueue.Enqueue(map);
        }

        if (_downloadQueue.Count == 0)
        {
            _logger.LogInformation("All workshop maps are already installed");
            _onCycleComplete(!string.IsNullOrEmpty(defaultMap));

            if (!string.IsNullOrEmpty(defaultMap))
            {
                _logger.LogInformation("Changing to default map {Map}", defaultMap);
                ChangeToMap(defaultMap);
            }
            return;
        }

        _returnMap = defaultMap ?? _bridge.ModSharp.GetMapName();
        _logger.LogInformation("{Count} workshop maps need downloading", _downloadQueue.Count);
        IsCycling = true;
        CycleNext();
    }

    public void ForceDownloadAll()
    {
        _returnMap = _bridge.ModSharp.GetMapName();
        _downloadQueue.Clear();
        _currentCycleMap = null;

        foreach (var map in _maps)
            _downloadQueue.Enqueue(map);

        IsCycling = true;
        CycleNext();
    }

    public void OnMapLoaded(string? defaultMap = null)
    {
        if (_firstActivate)
        {
            _firstActivate = false;
            StartMissingDownloads(defaultMap);
            return;
        }

        if (!IsCycling)
            return;

        CancelTimeout();

        if (_currentCycleMap is not null)
        {
            var bspName = _bridge.ModSharp.GetMapName();
            if (!string.IsNullOrEmpty(bspName))
            {
                _logger.LogInformation("Resolved workshop map {Id} -> {MapName}",
                    _currentCycleMap.WorkshopId, bspName);
                _currentCycleMap.MapName = bspName;
                ConfigExporter.SaveMaplist(_maps, _maplistPath, _logger);
            }
            _currentCycleMap = null;
        }

        CycleNext();
    }

    public void OnDownloadFailed(ulong sharedFileId)
    {
        if (!IsCycling || _currentCycleMap is null || _currentCycleMap.WorkshopId != sharedFileId)
            return;

        _logger.LogWarning("Skipping failed workshop map {Id}", sharedFileId);
        CancelTimeout();
        _currentCycleMap = null;
        CycleNext();
    }

    public void Cleanup()
    {
        CancelTimeout();
    }

    private void CycleNext()
    {
        if (_downloadQueue.Count > 0)
        {
            var next = _downloadQueue.Dequeue();
            _currentCycleMap = next;
            _logger.LogInformation("Cycling to workshop map {Id}, {Remaining} remaining",
                next.WorkshopId, _downloadQueue.Count);
            _bridge.ModSharp.ServerCommand($"host_workshop_map {next.WorkshopId}");
            StartTimeout(next);
            return;
        }

        IsCycling = false;
        _currentCycleMap = null;
        ConfigExporter.SaveMaplist(_maps, _maplistPath, _logger);

        var returnMap = _returnMap;
        _returnMap = null;
        _onCycleComplete(!string.IsNullOrEmpty(returnMap));

        _logger.LogInformation("Workshop map download cycle complete");

        if (!string.IsNullOrEmpty(returnMap))
        {
            _logger.LogInformation("Returning to map {Map}", returnMap);
            ChangeToMap(returnMap);
        }
    }

    private void ChangeToMap(string mapName)
    {
        var workshop = _maps.Find(m =>
            string.Equals(m.MapName, mapName, StringComparison.OrdinalIgnoreCase));

        if (workshop is not null)
            _bridge.ModSharp.ServerCommand($"host_workshop_map {workshop.WorkshopId}");
        else
            _bridge.ModSharp.ServerCommand($"changelevel {mapName}");
    }

    private void StartTimeout(MapEntry map)
    {
        CancelTimeout();
        var workshopId = map.WorkshopId;
        _timeoutTimer = _bridge.ModSharp.PushTimer(() =>
        {
            if (!IsCycling || _currentCycleMap is null || _currentCycleMap.WorkshopId != workshopId)
                return;

            _logger.LogWarning("Workshop map {Id} timed out after {Seconds}s, skipping",
                workshopId, _cycleTimeoutSeconds);
            _currentCycleMap = null;
            CycleNext();
        }, _cycleTimeoutSeconds);
    }

    private void CancelTimeout()
    {
        if (_bridge.ModSharp.IsValidTimer(_timeoutTimer))
        {
            _bridge.ModSharp.StopTimer(_timeoutTimer);
            _timeoutTimer = Guid.Empty;
        }
    }
}
