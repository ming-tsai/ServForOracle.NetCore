﻿using Microsoft.Extensions.Logging;
using ServForOracle.NetCore.Cache;

namespace ServForOracle.NetCore.Config
{
    public class ConfigurePresetMappings
    {
        private readonly ILogger<ConfigurePresetMappings> _logger;
        private readonly ServForOracleCache _cache;

        public ConfigurePresetMappings(ILogger<ConfigurePresetMappings> logger, ServForOracleCache cache)
        {
            _logger = logger;
            _cache = cache;
        }

        public void AddOracleUDT(params PresetMap[] presets)
        {
            if (presets != null)
            {
                foreach (var p in presets)
                {
                    _cache.AddOracleUDTPresets(p.Type, p.Info, p.ReplacedProperties);
                }
            }
            else
            {
                _logger?.LogWarning("the presets object is null");
            }
        }
    }
}
