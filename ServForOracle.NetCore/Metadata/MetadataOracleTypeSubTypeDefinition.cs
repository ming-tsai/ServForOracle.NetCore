﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ServForOracle.NetCore.Metadata
{
    internal class MetadataOracleTypeSubTypeDefinition: MetadataOracleTypePropertyDefinition
    {
        /// <summary>
        /// Reference to the type metadata
        /// </summary>
        public MetadataOracleTypeDefinition MetadataOracleType { get; set; }
    }
}
