using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DbContextBackup;

public abstract class AbstractDbContextBackupContext
{
    protected Action<int, string> ProgressNotifyAction;
    protected Action<string> StateNotifyAction;
    protected DbContextBackupContextTextResource TextResource;

    public AbstractDbContextBackupContext(
        Action<int, string> progressNotify = null,
        Action<string> stateNotify = null,
        DbContextBackupContextTextResource textResource = null)
    {
        this.ProgressNotifyAction = progressNotify;
        this.StateNotifyAction = stateNotify;
        if (textResource == null)
            textResource = new DbContextBackupContextTextResource();
        this.TextResource = textResource;
    }

    protected string GetEntityTypeDisplayName(IEntityType entityType)
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

    public abstract void Backup(DbContext dbContext, Stream backupStream, Func<string, string> tableNameProcessor = null);

    protected virtual void Backup(
        DbContext dbContext,
        Stream backupStream,
        Func<string, string> tableNameProcessor,
        Action<Type> onBackupClassChangedAction,
        Action<Dictionary<string, object>> onBackupOneRowAction)
    {
        dbContext.Database.EnsureCreated();

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            connection.Open();

        var entityTypes = dbContext.Model.GetEntityTypes().ToArray();
        var i = 1;
        foreach (var entityType in entityTypes)
        {
            ProgressNotifyAction?.Invoke(i * 100 / entityTypes.Length, $"({i}/{entityTypes.Length}) {GetEntityTypeDisplayName(entityType)})");
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
                        onBackupClassChangedAction?.Invoke(clazz);

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
                            onBackupOneRowAction?.Invoke(rowDict);
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
