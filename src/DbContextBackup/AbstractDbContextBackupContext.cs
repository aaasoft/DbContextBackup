using System;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DbContextBackup;

public abstract class AbstractDbContextBackupContext
{
    private static readonly Encoding dbBackupDataEncoding = Encoding.UTF8;
    protected Action<int, string> progressNotify;
    protected Action<string> stateNotify;
    protected DbContextBackupContextTextResource textResource;

    public AbstractDbContextBackupContext(
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

    protected string getEntityTypeDisplayName(IEntityType entityType)
    {
        var tableDisplayName = entityType.ClrType
            .GetCustomAttribute<CommentAttribute>()?.Comment;
        if (string.IsNullOrEmpty(tableDisplayName))
            tableDisplayName = entityType.DisplayName();
        tableDisplayName += $"({entityType.GetTableName()})";
        return tableDisplayName;
    }

    public void Backup(DbContext dbContext, string backupFile, Func<string, string> tableNameProcessor = null)
    {
        using (var stream = File.OpenWrite(backupFile))
            Backup(dbContext, stream, tableNameProcessor);
    }

    protected abstract void OnBackupClassChanged(Type type);
    protected abstract void BackupRow(Dictionary<string, object> row);

    public virtual void Backup(DbContext dbContext, Stream backupStream, Func<string, string> tableNameProcessor = null)
    {
        dbContext.Database.EnsureCreated();

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            connection.Open();

        var entityTypes = dbContext.Model.GetEntityTypes().ToArray();
        var i = 1;
        foreach (var entityType in entityTypes)
        {
            progressNotify?.Invoke(i * 100 / entityTypes.Length, $"({i}/{entityTypes.Length}) {getEntityTypeDisplayName(entityType)})");
            i++;
            var clazz = entityType.ClrType;
            var tableName = entityType.GetTableName();
            if (tableNameProcessor != null)
                tableName = tableNameProcessor(tableName);
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"select * from {tableName}";
                try
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.HasRows)
                            continue;
                        OnBackupClassChanged(clazz);

                        var fieldCount = reader.FieldCount;
                        var fieldList = new List<string>();
                        for (var fieldOrdinal = 0; fieldOrdinal < fieldCount; fieldOrdinal++)
                        {
                            var fieldName = reader.GetName(fieldOrdinal);
                            fieldList.Add(fieldName);
                        }
                        var rowDict = new Dictionary<string, object>();
                        while (reader.Read())
                        {
                            rowDict.Clear();
                            for (var fieldOrdinal = 0; fieldOrdinal < fieldCount; fieldOrdinal++)
                            {
                                var fieldName = fieldList[fieldOrdinal];
                                var fieldValue = reader.GetValue(fieldOrdinal);
                                if (fieldValue == null || fieldValue is DBNull)
                                    continue;
                                rowDict[fieldName] = fieldValue;
                            }
                            BackupRow(rowDict);
                        }
                    }
                }
                catch { }
            }
        }
    }

    public void Restore(DbContext dbContext, string backupFile)
    {
        using (var stream = File.OpenRead(backupFile))
            Restore(dbContext, stream);
    }

    public abstract void Restore(DbContext dbContext, Stream backupStream);
}
