using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DbContextBackup;

public abstract class DbContextBackupContext
{
    protected Action<int, string> ProgressNotifyAction;
    protected Action<string> StateNotifyAction;
    protected DbContextBackupContextTextResource TextResource;

    public DbContextBackupContext(
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

    public void Backup(DbContext dbContext, string backupFile, Func<string, string> tableNameProcessor = null, Type[] backupClasses = null)
    {
        using (var stream = File.OpenWrite(backupFile))
            Backup(dbContext, stream, tableNameProcessor, backupClasses);
    }

    public abstract void Backup(DbContext dbContext, Stream backupStream, Func<string, string> tableNameProcessor = null, Type[] backupClasses = null);

    protected void innerBackup(
        DbContext dbContext,
        Func<string, string> tableNameProcessor,
        Type[] backupClasses,
        Action<IEntityType> onBackupClassChangedAction,
        Action<Dictionary<string, object>> onBackupOneRowAction)
    {
        dbContext.Database.EnsureCreated();

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            connection.Open();

        var backupClassHashSet = backupClasses.ToHashSet();
        var entityTypes = dbContext.Model.GetEntityTypes()
            .Where(t =>
            {
                if (backupClasses == null)
                    return true;
                return backupClassHashSet.Contains(t.ClrType);
            }).ToArray();
        var i = 1;
        foreach (var entityType in entityTypes)
        {
            ProgressNotifyAction?.Invoke(i * 100 / entityTypes.Length, $"({i}/{entityTypes.Length}) {GetEntityTypeDisplayName(entityType)})");
            i++;
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
                        onBackupClassChangedAction?.Invoke(entityType);

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

    public abstract void Check(DbContext dbContext, Stream backupStream, Action<object> modelCheckAction = null);

    public void Check(DbContext dbContext, string backupFile, Action<object> modelCheckAction = null)
    {
        using (var stream = File.OpenRead(backupFile))
            Check(dbContext, stream, modelCheckAction);
    }

    public virtual void Restore(DbContext dbContext, Stream backupStream, Action<object> modelCheckAction = null)
    {
        StateNotifyAction?.Invoke(TextResource.RestoringData);
        Check(dbContext, backupStream, model =>
        {
            modelCheckAction?.Invoke(model);
            try
            {
                dbContext.Add(model);
            }
            catch (Exception ex)
            {
                var typeName = model.GetType().FullName;
                var dataJson = JsonSerializer.Serialize(model, new JsonSerializerOptions() { WriteIndented = true });
                throw new DbUpdateException($"将类型[{typeName}]的数据[{dataJson}]写入数据库时失败。", ex);
            }
            dbContext.Add(model);
        });
        StateNotifyAction?.Invoke(TextResource.SavingChanges);
        dbContext.SaveChanges();
    }

    public void Restore(DbContext dbContext, string backupFile, Action<object> modelCheckAction = null)
    {
        using (var stream = File.OpenRead(backupFile))
            Restore(dbContext, stream, modelCheckAction);
    }

}
