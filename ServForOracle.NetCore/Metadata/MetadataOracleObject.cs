﻿using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ServForOracle.NetCore.Extensions;
using System.Threading.Tasks;
using System.Data;

namespace ServForOracle.NetCore.Metadata
{
    internal class MetadataOracleObject<T> : MetadataOracleObject
    {
        private readonly Regex regex;
        private readonly string ConstructorString;
        private readonly Type Type;
        internal readonly MetadataOracleNetTypeDefinition OracleTypeNetMetadata;
        internal Dictionary<string, string> ObjectSubTypes = new Dictionary<string, string>();

        public MetadataOracleObject(MetadataOracleTypeDefinition metadataOracleType, UdtPropertyNetPropertyMap[] customProperties)
        {
            Type = typeof(T);
            if (Type.IsCollection())
            {
                OracleTypeNetMetadata = new MetadataOracleNetTypeDefinition(Type.GetCollectionUnderType(), metadataOracleType,
                    customProperties ?? new UdtPropertyNetPropertyMap[] { });
            }
            else
            {
                OracleTypeNetMetadata = new MetadataOracleNetTypeDefinition(Type, metadataOracleType,
                    customProperties ?? new UdtPropertyNetPropertyMap[] { });
            }

            regex = new Regex(Regex.Escape("$"));

            //var constructor = new StringBuilder();
            ConstructorString = GenerateConstructor(metadataOracleType.UDTInfo.FullObjectName,
                metadataOracleType.Properties.ToArray());

            //constructor.Append(';');
            //ConstructorString = constructor.ToString();
        }

        private string GenerateConstructor(string objectName, MetadataOracleTypePropertyDefinition[] properties)
        {
            var constructor = new StringBuilder($"{objectName}(");
            //constructor.Append($"{objectName}(");

            for (var counter = 0; counter < properties.Count(); counter++)
            {
                constructor.Append(properties[counter].Name);
                constructor.Append("=>$");
                if (properties[counter] is MetadataOracleTypeSubTypeDefinition subType)
                {
                    //constructor.Append("$");
                    ObjectSubTypes.Add(properties[counter].Name, GenerateConstructor(subType.MetadataOracleType.UDTInfo.FullObjectName, subType.MetadataOracleType.Properties.ToArray()));
                    //GenerateConstructor(constructor, subType.MetadataOracleType.UDTInfo.FullObjectName, subType.MetadataOracleType.Properties.ToArray());
                }
                else
                {
                    //constructor.Append('$');
                }

                if (counter + 1 < properties.Count())
                {
                    constructor.Append(',');
                }
            }

            constructor.Append(");");
            return constructor.ToString();
        }

        private string BuildConstructor(object value, string parameterName, MetadataOracleNetTypeDefinition metadata, ref int startNumber,
            string constructor)
        {
            var workedTypes = new Dictionary<string, string>();
            var dependencies = new StringBuilder();
            int dependenciesCounter = 0;
            foreach(var prop in metadata.Properties.Where(c => c.PropertyMetadata != null).OrderBy(c => c.Order))
            {
                var workedName = parameterName + dependenciesCounter++;
                dependencies.AppendLine(BuildQueryConstructor(value, workedName, ref startNumber, prop.PropertyMetadata));
                workedTypes.Add(prop.Name, workedName);
            }

            foreach (var prop in metadata.Properties.OrderBy(c => c.Order))
            {
                if (prop.NETProperty != null)
                {
                    if (prop.PropertyMetadata != null)
                    {
                        constructor = regex.Replace(constructor, $":{startNumber++}", 1);
                    }
                    else
                    {
                        workedTypes.TryGetValue(prop.Name, out var subtype);
                        constructor = regex.Replace(constructor, subtype);
                    }
                }
                else
                {
                    constructor = regex.Replace(constructor, "null", 1);
                }

            }

            dependencies.AppendLine(constructor);
            return dependencies.ToString(); ;
        }

        public (string Constructor, int LastNumber) BuildQueryConstructorString(T value, string name, int startNumber)
        {
            var baseString = BuildQueryConstructor(value, name, ref startNumber, OracleTypeNetMetadata);

            return (baseString.ToString(), startNumber);
        }

        private string BuildQueryConstructor(object value, string name, ref int startNumber, MetadataOracleNetTypeDefinition metadata)
        {
            var baseString = new StringBuilder();
            if (Type.IsCollection())
            {
                if (value != null)
                {
                    foreach (var v in value as IEnumerable)
                    {
                        var baseConstructor = BuildConstructor(v, name, metadata, ref startNumber, ConstructorString);
                        baseString.AppendLine($"{name}.extend;");
                        baseString.AppendLine($"{name}({name}.last) := {baseConstructor}");
                    }
                }
            }
            else
            {
                var baseConstructor = BuildConstructor(value, name, OracleTypeNetMetadata, ref startNumber, ConstructorString);
                baseString.AppendLine($"{name} := {baseConstructor}");
            }

            return baseString.ToString();
        }

        public OracleParameter[] GetOracleParameters(T value, int startNumber)
        {
            var parameters = new List<OracleParameter>();

            if (Type.IsCollection() && value is IEnumerable list)
            {
                parameters.AddRange(ProcessCollectionParameters(list, OracleTypeNetMetadata, startNumber, out int _));
            }
            else
            {
                parameters.AddRange(ProcessOracleParameter(value, OracleTypeNetMetadata, startNumber, out int _));
            }

            return parameters.ToArray();
        }

        private IEnumerable<OracleParameter> ProcessOracleParameter(object value,
            MetadataOracleNetTypeDefinition metadata,
            int startNumber, out int newNumber)
        {
            var propertiesParameters = new List<OracleParameter>();
            foreach (var prop in metadata.Properties.Where(c => c.NETProperty != null).OrderBy(c => c.Order))
            {
                if (prop.PropertyMetadata != null)
                {
                    if (prop.NETProperty.PropertyType.IsCollection())
                    {
                        propertiesParameters.AddRange(ProcessCollectionParameters(prop.NETProperty.GetValue(value) as IEnumerable, prop.PropertyMetadata, startNumber, out startNumber));
                    }
                    else
                    {
                        propertiesParameters.AddRange(ProcessOracleParameter(prop.NETProperty.GetValue(value), prop.PropertyMetadata,
                            startNumber, out startNumber));
                    }
                }
                else
                {
                    propertiesParameters.Add(
                        GetOracleParameter(
                            type: prop.NETProperty.PropertyType,
                            direction: ParameterDirection.Input,
                            name: $":{startNumber++}",
                            value: value != null ? prop.NETProperty.GetValue(value) : null
                        ));
                }
            }
            newNumber = startNumber;
            return propertiesParameters;
        }

        private IEnumerable<OracleParameter> ProcessCollectionParameters(IEnumerable value,
            MetadataOracleNetTypeDefinition metadata, int startNumber, out int lastNumber)
        {
            var rowsParameters = new List<OracleParameter>();
            if (value != null)
            {
                foreach (var temp in value)
                {
                    rowsParameters.AddRange(ProcessOracleParameter(temp, metadata, startNumber, out startNumber));
                }
            }
            lastNumber = startNumber;
            return rowsParameters;
        }


        private string QueryBuilder(MetadataOracleNetTypeDefinition metadata, string tableName, string basePropertyName = null)
        {
            var select = new StringBuilder();
            var first = true;
            foreach (var prop in metadata.Properties.Where(c => c.NETProperty != null))
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    select.Append(",");
                }

                if (prop.PropertyMetadata != null)
                {
                    if (prop.NETProperty.PropertyType.IsCollection())
                    {
                        select.Append("cursor(select ");
                        select.Append(QueryBuilder(prop.PropertyMetadata, "d"));
                        select.Append($" from table(value({tableName}).{prop.Name}) d) {prop.Name}");
                    }
                    else
                    {
                        select.Append(QueryBuilder(prop.PropertyMetadata, tableName, prop.Name));
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(basePropertyName))
                    {
                        select.Append($"value({tableName}).{basePropertyName}.{prop.Name} {basePropertyName}{prop.Name}");
                    }
                    else
                    {
                        select.Append($"value({tableName}).{prop.Name} {prop.Name}");
                    }
                }
            }

            return select.ToString();
        }

        private string GetRefCursorCollectionQuery(int startNumber, string fieldName)
        {
            var query = new StringBuilder($"open :{startNumber} for select ");

            query.Append(QueryBuilder(OracleTypeNetMetadata, "c"));
            query.Append($" from table({fieldName}) c;");

            return query.ToString();
        }

        private string GetRefCursorObjectQuery(int startNumber, string fieldName)
        {
            var query = new StringBuilder($"open :{startNumber} for select ");
            query.Append(QueryBuilder(OracleTypeNetMetadata, fieldName));
            query.Append(" from dual;");

            return query.ToString();
        }

        public override string GetRefCursorQuery(int startNumber, string fieldName)
        {
            if (Type.IsCollection())
            {
                return GetRefCursorCollectionQuery(startNumber, fieldName);
            }
            else
            {
                return GetRefCursorObjectQuery(startNumber, fieldName);
            }
        }

        public override async Task<object> GetValueFromRefCursorAsync(Type type, OracleRefCursor refCursor)
        {
            dynamic instance = type.CreateInstance();
            int counter = 0;
            var reader = refCursor.GetDataReader();

            if (type.IsCollection())
            {
                var subType = type.GetCollectionUnderType();
                while (await reader.ReadAsync())
                {
                    instance.Add(ReadObjectInstance(subType, reader, OracleTypeNetMetadata, ref counter));
                }

                return type.IsArray ? Enumerable.ToArray(instance) : Enumerable.AsEnumerable(instance);
            }
            else
            {
                while (await reader.ReadAsync())
                {
                    ReadObjectInstance(type, reader, OracleTypeNetMetadata, ref counter);
                }

                return (T)instance;
            }
        }

        public override object GetValueFromRefCursor(Type type, OracleRefCursor refCursor)
        {
            dynamic instance = type.CreateInstance();
            int counter = 0;
            var reader = refCursor.GetDataReader();

            if (type.IsCollection())
            {
                var subType = type.GetCollectionUnderType();
                while (reader.Read())
                {
                    counter = 0;
                    instance.Add(ReadObjectInstance(subType, reader, OracleTypeNetMetadata, ref counter));
                }

                return type.IsArray ? Enumerable.ToArray(instance) : Enumerable.AsEnumerable(instance);
            }
            else
            {
                while (reader.Read())
                {
                    instance = ReadObjectInstance(type, reader, OracleTypeNetMetadata, ref counter);
                }

                return (T)instance;
            }
        }

        private dynamic ReadObjectInstance(Type type, OracleDataReader reader, MetadataOracleNetTypeDefinition metadata, ref int count)
        {
            var instance = type.CreateInstance();
            foreach (var prop in metadata.Properties.Where(c => c.NETProperty != null).OrderBy(c => c.Order))
            {
                if (prop.PropertyMetadata != null)
                {
                    prop.NETProperty.SetValue(instance, ReadObjectInstance(prop.NETProperty.PropertyType,
                        reader, prop.PropertyMetadata, ref count));
                }
                else
                {
                    prop.NETProperty.SetValue(instance,
                        ConvertOracleParameterToBaseType(prop.NETProperty.PropertyType, reader.GetOracleValue(count++)));
                }
            }

            return instance;

        }

        public string GetDeclareLine(Type type, string parameterName, OracleUdtInfo udtInfo)
        {
            var dependenciesCounter = 0;
            var declareLine = new StringBuilder();
            foreach(var prop in OracleTypeNetMetadata.Properties.Where(c => c.PropertyMetadata != null).OrderBy(c => c.Order))
            {
                declareLine.AppendLine(GetDeclareLine(prop.NETProperty.PropertyType, parameterName + dependenciesCounter++, prop.PropertyMetadata.UDTInfo));
            }

            if (type.IsCollection())
                declareLine.AppendLine($"{parameterName} {udtInfo.FullCollectionName} := {udtInfo.FullCollectionName}();");
            else
                declareLine.AppendLine($"{parameterName} {udtInfo.FullObjectName};");

            return declareLine;
        }
    }

    internal abstract class MetadataOracleObject : MetadataOracle
    {
        public abstract Task<object> GetValueFromRefCursorAsync(Type type, OracleRefCursor refCursor);
        public abstract object GetValueFromRefCursor(Type type, OracleRefCursor refCursor);
        public abstract string GetRefCursorQuery(int startNumber, string fieldName);
    }
}
