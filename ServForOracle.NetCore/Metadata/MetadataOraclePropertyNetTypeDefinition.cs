﻿using System;
using System.Reflection;

namespace ServForOracle.NetCore.Metadata
{
    internal class MetadataOraclePropertyNetTypeDefinition : MetadataOracleTypePropertyDefinition
    {
        public MetadataOraclePropertyNetTypeDefinition(MetadataOracleTypePropertyDefinition baseMetadata)
        {
            if (baseMetadata is null)
            {
                throw new ArgumentNullException(nameof(baseMetadata));
            }

            Name = baseMetadata.Name;
            Order = baseMetadata.Order;
        }

        public virtual PropertyInfo NETProperty { get; set; }
        public virtual MetadataOracleNetTypeDefinition PropertyMetadata { get; set; }
    }
}