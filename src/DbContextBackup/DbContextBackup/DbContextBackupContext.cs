using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.ComponentModel;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DbContextBackup
{
    public class DbContextBackupContext
    {
        private const string DB_DATA_ENTRY_NAME = "DATA";
        private static readonly Encoding dbBackupDataEncoding = Encoding.UTF8;
        private Action<int, string> progressNotify;
        private Action<string> stateNotify;
        private DbContextBackupContextTextResource textResource;

        public DbContextBackupContext(
            Action<int, string> progressNotify = null,
            Action<string> stateNotify = null,
            DbContextBackupContextTextResource textResource = null)
        {
            this.progressNotify = progressNotify;
            this.stateNotify = stateNotify;
            if (textResource == null)
                textResource = new DbContextBackupContextTextResource();
            this.textResource = textResource;
        }

        private string getEntityTypeDisplayName(IEntityType entityType)
        {
            var tableDisplayName = entityType.ClrType
                .GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
            if (string.IsNullOrEmpty(tableDisplayName))
                tableDisplayName = entityType.DisplayName();
            tableDisplayName += $"({entityType.GetTableName()})";
            return tableDisplayName;
        }

        public void Backup(DbContext dbContext, string backupFile)
        {
            using (var stream = File.OpenWrite(backupFile))
                Backup(dbContext, stream);
        }

        public void Backup(DbContext dbContext, Stream backupStream)
        {
            dbContext.Database.EnsureCreated();

            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                connection.Open();

            using (var zipArchive = new ZipArchive(backupStream, ZipArchiveMode.Create, true))
            {
                var dataEntry = zipArchive.CreateEntry(DB_DATA_ENTRY_NAME);
                using (var stream = dataEntry.Open())
                using (var writer = new StreamWriter(stream, dbBackupDataEncoding))
                {
                    var entityTypes = dbContext.Model.GetEntityTypes().ToArray();
                    var i = 1;
                    foreach (var entityType in entityTypes)
                    {
                        progressNotify?.Invoke(i * 100 / entityTypes.Length, $"({i}/{entityTypes.Length}) {getEntityTypeDisplayName(entityType)})");
                        i++;
                        var clazz = entityType.ClrType;
                        var tableName = entityType.GetTableName();
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = $"select * from {tableName}";
                            try
                            {
                                using (var reader = cmd.ExecuteReader())
                                {
                                    if (!reader.HasRows)
                                        continue;
                                    writer.WriteLine($"#{clazz.FullName}");
                                    var fieldCount = reader.FieldCount;
                                    var fieldList = new List<string>();
                                    for (var fieldOrdinal = 0; fieldOrdinal < fieldCount; fieldOrdinal++)
                                    {
                                        var fieldName = reader.GetName(fieldOrdinal);
                                        fieldList.Add(fieldName);
                                    }
                                    while (reader.Read())
                                    {
                                        var jObj = new JsonObject();
                                        for (var fieldOrdinal = 0; fieldOrdinal < fieldCount; fieldOrdinal++)
                                        {
                                            var fieldName = fieldList[fieldOrdinal];
                                            var fieldValue = reader.GetValue(fieldOrdinal);
                                            if (fieldValue == null || fieldValue is DBNull)
                                                continue;
                                            jObj.Add(fieldName, JsonValue.Create(fieldValue));
                                        }
                                        writer.WriteLine(jObj.ToJsonString());
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        public void Restore(DbContext dbContext, string backupFile)
        {
            using (var stream = File.OpenRead(backupFile))
                Restore(dbContext, stream);
        }

        public void Restore(DbContext dbContext, Stream backupStream)
        {
            //读取元信息
            using (ZipArchive zipArchive = new ZipArchive(backupStream, ZipArchiveMode.Read, true))
            {
                var dataEntry = zipArchive.GetEntry(DB_DATA_ENTRY_NAME);
                if (dataEntry == null)
                    throw new ApplicationException(textResource.DatabaseBackupFileHasNoData);

                var totalLength = dataEntry.Length;
                //开始导入
                using (var stream = dataEntry.Open())
                using (var reader = new StreamReader(stream, dbBackupDataEncoding))
                {
                    stateNotify?.Invoke(textResource.DeletingTableSchema);
                    dbContext.Database.EnsureDeleted();

                    stateNotify?.Invoke(textResource.CreatingTableSchema);
                    dbContext.Database.EnsureCreated();

                    stateNotify?.Invoke(textResource.RestoringData);

                    Dictionary<string, IEntityType> clazzDict = new Dictionary<string, IEntityType>();
                    foreach (var entityType in dbContext.Model.GetEntityTypes())
                        clazzDict[entityType.ClrType.FullName] = entityType;

                    IEntityType currentEntityType = null;
                    long position = 0;
                    Action updateProgress = () =>
                    {
                        progressNotify?.Invoke(
                           Convert.ToInt32(position * 100 / totalLength),
                           currentEntityType == null ?
                               null : getEntityTypeDisplayName(currentEntityType));
                    };

                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        position += reader.CurrentEncoding.GetByteCount(line) + 1;
                        if (string.IsNullOrEmpty(line))
                            continue;
                        if (line.StartsWith("#"))
                        {
                            var currentTable = line.Substring(1);
                            IEntityType entityType = null;
                            if (clazzDict.ContainsKey(currentTable))
                                entityType = clazzDict[currentTable];
                            currentEntityType = entityType;
                            updateProgress();
                            if (currentEntityType == null)
                                continue;
                        }
                        else if (line.StartsWith("{"))
                        {
                            if (currentEntityType == null)
                                continue;
                            updateProgress();

                            object item = null;
                            try
                            {
                                item = JsonSerializer.Deserialize(line, currentEntityType.ClrType);
                            }
                            catch (Exception ex)
                            {
                                throw new SerializationException($"将数据[{line}]反序列化为类型[{currentEntityType.ClrType.FullName}]时失败。", ex);
                            }
                            try
                            {
                                dbContext.Add(item);
                            }
                            catch (Exception ex)
                            {
                                throw new SerializationException($"将类型[{currentEntityType.ClrType.FullName}]的数据[{line}]写入数据库时失败。", ex);
                            }
                        }
                    }
                    stateNotify?.Invoke(textResource.SavingChanges);
                    dbContext.SaveChanges();
                }
            }
        }

        /// <summary>
        /// 更新结构
        /// </summary>
        /// <param name="dbContext"></param>
        public void UpdateSchema(DbContext dbContext)
        {
            using (var ms = new MemoryStream())
            {
                //备份
                Backup(dbContext, ms);
                //还原
                ms.Position = 0;
                Restore(dbContext, ms);
            }
        }
    }
}