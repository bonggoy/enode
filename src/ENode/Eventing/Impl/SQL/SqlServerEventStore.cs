﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using ECommon.Components;
using ECommon.Dapper;
using ECommon.Serializing;
using ECommon.Utilities;
using ENode.Configurations;
using ENode.Infrastructure;

namespace ENode.Eventing.Impl.SQL
{
    public class SqlServerEventStore : IEventStore
    {
        #region Private Variables

        private readonly string _connectionString;
        private readonly string _eventTable;
        private readonly string _primaryKeyName;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IEventSerializer _eventSerializer;
        private readonly IOHelper _ioHelper;

        #endregion

        #region Constructors

        public SqlServerEventStore()
        {
            var setting = ENodeConfiguration.Instance.Setting.SqlServerEventStoreSetting;
            Ensure.NotNull(setting, "SqlServerEventStoreSetting");
            Ensure.NotNull(setting.ConnectionString, "SqlServerEventStoreSetting.ConnectionString");
            Ensure.NotNull(setting.TableName, "SqlServerEventStoreSetting.TableName");
            Ensure.NotNull(setting.PrimaryKeyName, "SqlServerEventStoreSetting.PrimaryKeyName");

            _connectionString = setting.ConnectionString;
            _eventTable = setting.TableName;
            _primaryKeyName = setting.PrimaryKeyName;
            _jsonSerializer = ObjectContainer.Resolve<IJsonSerializer>();
            _eventSerializer = ObjectContainer.Resolve<IEventSerializer>();
            _ioHelper = ObjectContainer.Resolve<IOHelper>();
        }

        #endregion

        #region Public Methods

        public void BatchAppend(IEnumerable<DomainEventStream> eventStreams)
        {
            _ioHelper.TryIOAction(() =>
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    var transaction = connection.BeginTransaction();
                    try
                    {
                        foreach (var eventStream in eventStreams)
                        {
                            connection.Insert(ConvertTo(eventStream), _eventTable, transaction);
                        }
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }, "BatchAppendEvents");
        }
        public EventAppendResult Append(DomainEventStream eventStream)
        {
            var record = ConvertTo(eventStream);

            return _ioHelper.TryIOFunc(() =>
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    try
                    {
                        connection.Insert(record, _eventTable);
                        return EventAppendResult.Success;
                    }
                    catch (SqlException ex)
                    {
                        if (ex.Number == 2627)
                        {
                            if (ex.Message.Contains(_primaryKeyName))
                            {
                                return EventAppendResult.DuplicateEvent;
                            }
                        }
                        throw;
                    }
                }
            }, "AppendEvents");
        }
        public DomainEventStream Find(string aggregateRootId, int version)
        {
            var record = _ioHelper.TryIOFunc(() =>
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    return connection.QueryList<StreamRecord>(new { AggregateRootId = aggregateRootId, Version = version }, _eventTable).SingleOrDefault();
                }
            }, "FindEventByVersion");

            if (record != null)
            {
                return ConvertFrom(record);
            }
            return null;
        }
        public DomainEventStream Find(string aggregateRootId, string commandId)
        {
            var record = _ioHelper.TryIOFunc(() =>
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    return connection.QueryList<StreamRecord>(new { AggregateRootId = aggregateRootId, CommandId = commandId }, _eventTable).SingleOrDefault();
                }
            }, "FindEventByCommandId");

            if (record != null)
            {
                return ConvertFrom(record);
            }
            return null;
        }
        public IEnumerable<DomainEventStream> QueryAggregateEvents(string aggregateRootId, int aggregateRootTypeCode, int minVersion, int maxVersion)
        {
            var records = _ioHelper.TryIOFunc(() =>
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    var sql = string.Format("SELECT * FROM [{0}] WHERE AggregateRootId = @AggregateRootId AND Version >= @MinVersion AND Version <= @MaxVersion", _eventTable);
                    return connection.Query<StreamRecord>(sql,
                    new
                    {
                        AggregateRootId = aggregateRootId,
                        MinVersion = minVersion,
                        MaxVersion = maxVersion
                    });
                }
            }, "QueryAggregateEvents");

            var streams = new List<DomainEventStream>();
            foreach (var record in records)
            {
                streams.Add(ConvertFrom(record));
            }
            return streams;
        }
        public IEnumerable<DomainEventStream> QueryByPage(int pageIndex, int pageSize)
        {
            var records = _ioHelper.TryIOFunc(() =>
            {
                using (var connection = GetConnection())
                {
                    connection.Open();
                    return connection.QueryPaged<StreamRecord>(null, _eventTable, "Sequence", pageIndex, pageSize);
                }
            }, "QueryByPage");

            var streams = new List<DomainEventStream>();
            foreach (var record in records)
            {
                streams.Add(ConvertFrom(record));
            }
            return streams;
        }

        #endregion

        #region Private Methods

        private SqlConnection GetConnection()
        {
            return new SqlConnection(_connectionString);
        }
        private DomainEventStream ConvertFrom(StreamRecord record)
        {
            return new DomainEventStream(
                record.CommandId,
                record.AggregateRootId,
                record.AggregateRootTypeCode,
                record.Version,
                record.Timestamp,
                _eventSerializer.Deserialize<IDomainEvent>(_jsonSerializer.Deserialize<IDictionary<int, string>>(record.Events)));
        }
        private StreamRecord ConvertTo(DomainEventStream eventStream)
        {
            return new StreamRecord
            {
                CommandId = eventStream.CommandId,
                AggregateRootId = eventStream.AggregateRootId,
                AggregateRootTypeCode = eventStream.AggregateRootTypeCode,
                Version = eventStream.Version,
                Timestamp = eventStream.Timestamp,
                Events = _jsonSerializer.Serialize(_eventSerializer.Serialize(eventStream.Events))
            };
        }

        #endregion

        class StreamRecord
        {
            public int AggregateRootTypeCode { get; set; }
            public string AggregateRootId { get; set; }
            public int Version { get; set; }
            public string CommandId { get; set; }
            public DateTime Timestamp { get; set; }
            public string Events { get; set; }
        }
    }
}
