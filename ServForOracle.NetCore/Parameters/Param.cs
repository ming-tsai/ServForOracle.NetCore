﻿using Oracle.ManagedDataAccess.Client;
using ServForOracle.NetCore.Metadata;
using System;
using System.Data;

namespace ServForOracle.NetCore.Parameters
{
    public abstract class Param
    {
        public ParameterDirection Direction { get; private set; }
        protected internal Param(Type type, object value, ParameterDirection direction)
        {
            Type = type;
            Value = value;
            Direction = direction;
        }
        public virtual Type Type { get; }
        public virtual object Value { get; protected set; }
        internal abstract void SetOutputValue(object value);

        public static Param Create<T>(T value, ParameterDirection direction)
        {
            var type = typeof(T);
            if(type.IsValueType || type == typeof(string))
            {
                return new ParamCLRType<T>(value, direction);
            }
            else
            {
                return new ParamObject<T>(value, direction);
            }
        }

        public static Param Input<T>(T value) => Create(value, ParameterDirection.Input);
        public static Param Output<T>() => Create(default(T), ParameterDirection.Output);
        public static Param InputOutput<T>(T value) => Create(value, ParameterDirection.InputOutput);
    }
}
