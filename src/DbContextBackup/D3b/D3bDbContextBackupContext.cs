using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DbContextBackup.D3b
{
    public class D3bDbContextBackupContext : DbContextBackupContext
    {
        private const string DB_DATA_ENTRY_NAME = "DATA";
        private static readonly Encoding dbBackupDataEncoding = Encoding.UTF8;

        public D3bDbContextBackupContext(
            Action<int, string> progressNotify = null,
            Action<string> stateNotify = null,
            DbContextBackupContextTextResource textResource = null) : base(progressNotify, stateNotify, textResource)
        { }

        public override void Backup(DbContext dbContext, Stream backupStream, Func<string, string> tableNameProcessor = null, Type[] backupClasses = null)
        {
            using (var zipArchive = new ZipArchive(backupStream, ZipArchiveMode.Create, true))
            {
                var dataEntry = zipArchive.CreateEntry(DB_DATA_ENTRY_NAME);
                using (var stream = dataEntry.Open())
                using (var writer = new StreamWriter(stream, dbBackupDataEncoding))
                {
                    innerBackup(dbContext, tableNameProcessor, backupClasses, entityType =>
                    {
                        writer.WriteLine($"#{entityType.ClrType.FullName}");
                    }, row =>
                    {
                        var jObj = new JsonObject();
                        foreach (var item in row)
                        {
                            var fieldName = item.Key;
                            var fieldValue = item.Value;
                            if (fieldValue == null || fieldValue is DBNull)
                                continue;
                            jObj[fieldName] = JsonValue.Create(fieldValue);
                        }
                        writer.WriteLine(jObj.ToJsonString());
                    });
                }
            }
        }


        public override void Check(DbContext dbContext, Stream backupStream, Action<object> modelCheckAction = null)
        {
            //读取元信息
            using (ZipArchive zipArchive = new ZipArchive(backupStream, ZipArchiveMode.Read, true))
            {
                var dataEntry = zipArchive.GetEntry(DB_DATA_ENTRY_NAME);
                if (dataEntry == null)
                    throw new ApplicationException(TextResource.DatabaseBackupFileHasNoData);

                var totalLength = dataEntry.Length;
                //开始导入
                using (var stream = dataEntry.Open())
                using (var reader = new StreamReader(stream, dbBackupDataEncoding))
                {
                    Dictionary<string, IEntityType> clazzDict = new Dictionary<string, IEntityType>();
                    foreach (var entityType in dbContext.Model.GetEntityTypes())
                        clazzDict[entityType.ClrType.FullName] = entityType;

                    IEntityType currentEntityType = null;
                    long position = 0;
                    Action updateProgress = () =>
                    {
                        ProgressNotifyAction?.Invoke(
                        Convert.ToInt32(position * 100 / totalLength),
                        currentEntityType == null ?
                            null : GetEntityTypeDisplayName(currentEntityType));
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
                            modelCheckAction?.Invoke(item);
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
                }
            }
        }
    }
}