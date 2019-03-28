﻿using Microsoft.Extensions.Logging;
using ServForOracle.NetCore.Cache;
using ServForOracle.NetCore.Metadata;
using ServForOracle.NetCore.OracleAbstracts;
using ServForOracle.NetCore.UnitTests.Config;
using ServForOracle.NetCore.Wrapper;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ServForOracle.NetCore.UnitTests
{
    public class ServiceForOracleTests
    {

        #region Constructor

        [Theory, CustomAutoData]
        public void Constructor_ThreeParameters_ConnectionString(ILogger<ServiceForOracle> logger, ServForOracleCache cache, string connectionString)
        {
            var service = new ServiceForOracle(logger, cache, connectionString);
        }

        [Theory, CustomAutoData]
        public void Constructor_ThreeParameters_DbFactory(ILogger<ServiceForOracle> logger, ServForOracleCache cache, IDbConnectionFactory factory)
        {
            var service = new ServiceForOracle(logger, cache, factory);
        }

        [Theory, CustomAutoData]
        internal void Constructor_FourParameters(ILogger<ServiceForOracle> logger, IDbConnectionFactory factory, MetadataOracleCommon common, IMetadataBuilderFactory builderFactory)
        {
            var service = new ServiceForOracle(logger, factory, builderFactory, common);
        }

        [Theory, CustomAutoData]
        internal void Constructor_FiveParameters(ILogger<ServiceForOracle> logger, IDbConnectionFactory factory, MetadataOracleCommon common, IMetadataBuilderFactory builderFactory, IOracleRefCursorWrapperFactory wrapperFactory)
        {
            var service = new ServiceForOracle(logger, factory, builderFactory, wrapperFactory, common);
        }

        [Theory, CustomAutoData]
        internal void Constructor_FourParameters_NullParameter_ThrowsArgumentNull(ILogger<ServiceForOracle> logger, IDbConnectionFactory factory, MetadataOracleCommon common, IMetadataBuilderFactory builderFactory, IOracleRefCursorWrapperFactory wrapperFactory)
        {
            Assert.Throws<ArgumentNullException>("factory", () => new ServiceForOracle(logger, null, builderFactory, wrapperFactory, common));
            Assert.Throws<ArgumentNullException>("builderFactory", () => new ServiceForOracle(logger, factory, null, wrapperFactory, common));
            Assert.Throws<ArgumentNullException>("common", () => new ServiceForOracle(logger, factory, builderFactory, wrapperFactory, null));
            Assert.Throws<ArgumentNullException>("wrapperFactory", () => new ServiceForOracle(logger, factory, builderFactory, null, common));
        }

        #endregion Constructor
    }
}
