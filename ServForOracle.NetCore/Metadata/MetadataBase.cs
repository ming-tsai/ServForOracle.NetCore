﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace ServForOracle.NetCore.Metadata
{
    internal class MetadataBase
    {
        public static ConcurrentBag<MetadataOracleTypeDefinition> OracleUDTs { get; private set; }
        public static ConcurrentDictionary<Type, MetadataOracle> TypeDefinitionsOracleUDT { get; private set; }
        public static ConcurrentDictionary<Type, (OracleUdtInfo Info, UdtPropertyNetPropertyMap[] Props, bool FuzzyMatch)> PresetUDTs { get; private set; }

        static MetadataBase()
        {
            OracleUDTs = new ConcurrentBag<MetadataOracleTypeDefinition>();
            TypeDefinitionsOracleUDT = new ConcurrentDictionary<Type, MetadataOracle>();
            PresetUDTs = new ConcurrentDictionary<Type, (OracleUdtInfo Info, UdtPropertyNetPropertyMap[] Props, bool fuzzyMatch)>();
        }

        internal static void AddOracleUDTPresets(Type Type, OracleUdtInfo Info, UdtPropertyNetPropertyMap[] Props, bool fuzzyNameMatch = true)
        {
            PresetUDTs.TryAdd(Type, (Info, Props, fuzzyNameMatch));
        }
    }
}
