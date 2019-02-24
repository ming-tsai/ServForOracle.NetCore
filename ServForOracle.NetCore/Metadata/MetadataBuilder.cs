﻿using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using ServForOracle.NetCore.Cache;
using ServForOracle.NetCore.Extensions;
using ServForOracle.NetCore.OracleAbstracts;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ServForOracle.NetCore.Metadata
{
    internal class MetadataBuilder
    {
        private const string COLLECTION = "COLLECTION";

        public DbConnection OracleConnection { get; private set; }
        private readonly ServForOracleCache Cache;
        private readonly ILogger Logger;

        public MetadataBuilder(DbConnection connection, ServForOracleCache cache, ILogger logger)
        {
            if(connection is null || string.IsNullOrWhiteSpace(connection.ConnectionString))
            {
                throw new ArgumentNullException(nameof(connection));
            }

            Cache = cache;
            Logger = logger;
            OracleConnection = connection;
        }

        public virtual async Task<MetadataOracleObject<T>> GetOrRegisterMetadataOracleObjectAsync<T>(OracleUdtInfo udtInfo)
        {
            GetTypeAndCachedMetadata<T>(out var type, out var metadata);

            if (metadata == null)
            {
                if (udtInfo != null)
                {
                    return await RegisterAsync(type, OracleConnection, udtInfo) as MetadataOracleObject<T>;
                }
                else
                {
                    return await RegisterAsync(type, OracleConnection) as MetadataOracleObject<T>;
                }
            }
            else
            {
                return metadata as MetadataOracleObject<T>;
            }
        }

        public virtual MetadataOracleObject<T> GetOrRegisterMetadataOracleObject<T>(OracleUdtInfo udtInfo)
        {
            GetTypeAndCachedMetadata<T>(out var type, out var metadata);

            if (metadata == null)
            {
                if (udtInfo != null)
                {
                    return Register(type, OracleConnection, udtInfo) as MetadataOracleObject<T>;
                }
                else
                {
                    return Register(type, OracleConnection) as MetadataOracleObject<T>;
                }
            }
            else
            {
                return metadata as MetadataOracleObject<T>;
            }
        }

        private void GetTypeAndCachedMetadata<T>(out Type type, out MetadataOracle metadata)
        {
            type = typeof(T);
            metadata = Cache.GetMetadata(type.FullName);
        }

        private async Task<object> RegisterAsync(Type type, DbConnection con)
        {
            var udtInfo = GetUDTInfo(type);
            return await RegisterAsync(type, con, udtInfo);
        }

        /// <see cref="MetadataOracleObject{T}.MetadataOracleObject(ServForOracleCache, MetadataOracleTypeDefinition, UdtPropertyNetPropertyMap[], bool)"
        private async Task<object> RegisterAsync(Type type, DbConnection con, OracleUdtInfo udtInfo)
        {
            var metadataGenericType = typeof(MetadataOracleObject<>).MakeGenericType(type);
            var typeMetadata = await GetOrCreateOracleTypeMetadataAsync(con, udtInfo);

            var (_, props, fuzzyMatch) = Cache.PresetGetValueOrDefault(type);
            var metadata = metadataGenericType.CreateInstance(Cache, typeMetadata, props, fuzzyMatch);

            Cache.SaveMetadata(type.FullName, metadata as MetadataOracle);

            return metadata;
        }

        private object Register(Type type, DbConnection con)
        {
            var udtInfo = GetUDTInfo(type);
            return Register(type, con, udtInfo);
        }

        /// <see cref="MetadataOracleObject{T}.MetadataOracleObject(ServForOracleCache, MetadataOracleTypeDefinition, UdtPropertyNetPropertyMap[], bool)"
        private object Register(Type type, DbConnection con, OracleUdtInfo udtInfo)
        {
            var metadataGenericType = typeof(MetadataOracleObject<>).MakeGenericType(type);
            var typeMetadata = GetOrCreateOracleTypeMetadata(con, udtInfo);

            var (_, props, fuzzyMatch) = Cache.PresetGetValueOrDefault(type);
            var metadata = metadataGenericType.CreateInstance(Cache, typeMetadata, props, fuzzyMatch);

            Logger?.LogDebug("Saving cache for the type {typeName}", type.FullName);
            Cache.SaveMetadata(type.FullName, metadata as MetadataOracle);

            return metadata;
        }

        private OracleUdtInfo GetUDTInfo(Type type)
        {
            OracleUdtInfo udtInfo;
            if (type.IsCollection())
            {
                var underType = type.GetCollectionUnderType();
                udtInfo = underType.GetCustomAttribute<OracleUdtAttribute>()?.UDTInfo
                    ?? Cache.PresetGetValueOrDefault(underType).Info;
            }
            else
            {
                udtInfo = type.GetCustomAttribute<OracleUdtAttribute>()?.UDTInfo
                    ?? Cache.PresetGetValueOrDefault(type).Info;
            }

            if (udtInfo == null)
            {
                var exception = new ArgumentException($"The type {type.FullName} needs to have the {nameof(OracleUdtAttribute)}" +
                    $" attribute set or pass the {nameof(OracleUdtInfo)} class to the execute method.");
                Logger?.LogError(exception, "Error finding the {typeName} in the Cache", type.FullName);
                throw exception;
            }

            return udtInfo;
        }

        private async Task<MetadataOracleTypeDefinition> GetOrCreateOracleTypeMetadataAsync(DbConnection connection, OracleUdtInfo udtInfo)
        {
            if (OracleConnection.State != ConnectionState.Open)
            {
                await OracleConnection.OpenAsync().ConfigureAwait(false);
            }

            var exists = Cache.GetTypeDefinition(udtInfo.FullObjectName);
            if (exists != null)
                return exists;

            var cmd = CreateCommand(connection, udtInfo);

            var properties = await ExecuteReaderAndLoadTypeDefinitionAsync(cmd).ConfigureAwait(false);
            return CreateAndSaveMetadata(udtInfo, properties);
        }

        private MetadataOracleTypeDefinition GetOrCreateOracleTypeMetadata(DbConnection connection, OracleUdtInfo udtInfo)
        {
            if (OracleConnection.State != ConnectionState.Open)
            {
                OracleConnection.Open();
            }

            var exists = Cache.GetTypeDefinition(udtInfo.FullObjectName);
            if (exists != null)
                return exists;

            var cmd = CreateCommand(connection, udtInfo);

            var properties = ExecuteReaderAndLoadTypeDefinition(cmd);
            return CreateAndSaveMetadata(udtInfo, properties);
        }

        private DbCommand CreateCommand(DbConnection connection, OracleUdtInfo udtInfo)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText =
            "select ata.attr_no, ata.attr_name, ata.attr_type_owner, ata.attr_type_name, aty.typecode "
            + "from all_type_attrs ata "
            + "left join all_types aty "
            + "on aty.owner = ata.attr_type_owner "
            + "and aty.type_name = ata.attr_type_name "
            + "where ata.owner = "
                + "upper(:1) and ata.type_name = upper(:2)";
            cmd.Parameters.Add(new OracleParameter(":1", udtInfo.ObjectSchema));
            cmd.Parameters.Add(new OracleParameter(":2", udtInfo.ObjectName));
            return cmd;
        }

        private MetadataOracleTypeDefinition CreateAndSaveMetadata(OracleUdtInfo udtInfo, List<MetadataOracleTypePropertyDefinition> properties)
        {
            var typedef = new MetadataOracleTypeDefinition
            {
                Properties = properties,
                UDTInfo = udtInfo
            };

            Logger?.LogInformation("Saving the metadata for the type {udtName}", udtInfo.FullObjectName);
            Cache.SaveTypeDefinition(typedef);

            return typedef;
        }

        private List<MetadataOracleTypePropertyDefinition> ExecuteReaderAndLoadTypeDefinition(DbCommand cmd)
        {
            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            var reader = cmd.ExecuteReader();
            var properties = new List<MetadataOracleTypePropertyDefinition>();

            while (reader.Read())
            {
                if (reader.IsDBNull(2))
                {
                    var property = new MetadataOracleTypePropertyDefinition
                    {
                        Order = reader.GetInt32(0),
                        Name = reader.GetString(1)
                    };

                    properties.Add(property);
                }
                else
                {
                    var property = new MetadataOracleTypeSubTypeDefinition
                    {
                        Order = reader.GetInt32(0),
                        Name = reader.GetString(1)
                    };

                    if (reader.GetString(4) == COLLECTION)
                    {
                        property.MetadataOracleType = GetOrCreateOracleTypeMetadata(cmd.Connection, GetOracleCollectionUnderlyingType(cmd.Connection, reader.GetString(2), reader.GetString(3)));
                    }
                    else
                    {
                        property.MetadataOracleType = GetOrCreateOracleTypeMetadata(cmd.Connection, new OracleUdtInfo(reader.GetString(2), reader.GetString(3)));
                    }

                    properties.Add(property);
                }
            }

            return properties;
        }

        private async Task<List<MetadataOracleTypePropertyDefinition>> ExecuteReaderAndLoadTypeDefinitionAsync(DbCommand cmd)
        {
            var reader = await cmd.ExecuteReaderAsync();
            var properties = new List<MetadataOracleTypePropertyDefinition>();

            while (await reader.ReadAsync())
            {
                if (await reader.IsDBNullAsync(2))
                {
                    var property = new MetadataOracleTypePropertyDefinition
                    {
                        Order = reader.GetInt32(0),
                        Name = reader.GetString(1)
                    };

                    properties.Add(property);
                }
                else
                {
                    var property = new MetadataOracleTypeSubTypeDefinition
                    {
                        Order = reader.GetInt32(0),
                        Name = reader.GetString(1)
                    };

                    if (reader.GetString(4) == COLLECTION)
                    {
                        property.MetadataOracleType = await GetOrCreateOracleTypeMetadataAsync(cmd.Connection, await GetOracleCollectionUnderlyingTypeAsync(cmd.Connection, reader.GetString(2), reader.GetString(3))).ConfigureAwait(false);
                    }
                    else
                    {
                        property.MetadataOracleType = await GetOrCreateOracleTypeMetadataAsync(cmd.Connection, new OracleUdtInfo(reader.GetString(2), reader.GetString(3))).ConfigureAwait(false);
                    }

                    properties.Add(property);
                }
            }

            return properties;
        }

        private OracleUdtInfo GetOracleCollectionUnderlyingType(DbConnection con, string schema, string collectionName)
        {
            //TODO make recursive
            var cmd = CreateDbCommandCollectionUnderType(con, schema, collectionName);

            var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                return new OracleUdtInfo(reader.GetString(0), reader.GetString(1), schema, collectionName);
            }
            else
            {
                return null;
            }
        }

        private async Task<OracleUdtInfo> GetOracleCollectionUnderlyingTypeAsync(DbConnection con, string schema, string collectionName)
        {
            //TODO make recursive
            var cmd = CreateDbCommandCollectionUnderType(con, schema, collectionName);

            var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                return new OracleUdtInfo(reader.GetString(0), reader.GetString(1), schema, collectionName);
            }
            else
            {
                return null;
            }
        }

        private DbCommand CreateDbCommandCollectionUnderType(DbConnection con, string schema, string collectionName)
        {
            var cmd = con.CreateCommand();
            cmd.CommandText =
                "select act.elem_type_owner, act.elem_type_name, aty.typecode "
                + "from ALL_COLL_TYPES act "
                + "join all_types aty "
                + "on aty.owner = act.owner "
                + "and aty.type_name = act.type_name "
                + "where act.owner = upper(:0) and act.type_name = upper(:1)";

            cmd.Parameters.Add(new OracleParameter(":1", schema));
            cmd.Parameters.Add(new OracleParameter(":2", collectionName));
            return cmd;
        }
    }
}