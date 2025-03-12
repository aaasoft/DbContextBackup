using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DbContextBackup.Excel;

public class XlsxDbContextBackupContext : DbContextBackupContext
{
    private Quick.Excel.XSSFExcelProvider excelProvider = new Quick.Excel.XSSFExcelProvider();

    public XlsxDbContextBackupContext(
        Action<int, string> progressNotify = null,
        Action<string> stateNotify = null,
        DbContextBackupContextTextResource textResource = null) : base(progressNotify, stateNotify, textResource)
    { }

    public override void Backup(DbContext dbContext, Stream backupStream, Func<string, string> tableNameProcessor = null, Type[] backupClasses = null)
    {
        var tablesDict = new Dictionary<string, Quick.Excel.table>();
        Quick.Excel.table currentTable = null;
        IProperty[] currentTableProperties = null;
        innerBackup(dbContext, tableNameProcessor, backupClasses, entityType =>
        {
            currentTableProperties = entityType.GetProperties().ToArray();
            currentTable = new Quick.Excel.table();
            tablesDict[entityType.GetTableName()] = currentTable;
            var headRow = new Quick.Excel.tr();
            headRow.AddRange(currentTableProperties.Select(t => new Quick.Excel.th(t.GetColumnName())));
            currentTable.Add(headRow);
        }, row =>
        {
            var dataRow = new Quick.Excel.tr();
            foreach (var prop in currentTableProperties)
            {
                string cellValue = null;
                if (row.TryGetValue(prop.GetColumnName(), out var value))
                {
                    if (value != null && value is not DBNull)
                        cellValue = value.ToString();
                }
                dataRow.Add(new Quick.Excel.td(cellValue));
            }
            currentTable.Add(dataRow);
        });
        excelProvider.ExportTable(tablesDict, backupStream);
    }

    public override void Restore(DbContext dbContext, Stream backupStream)
    {
        StateNotifyAction?.Invoke(TextResource.RestoringData);
        var entityTypeDict = dbContext.Model.GetEntityTypes().ToDictionary(t => t.GetTableName(), t => t);
        var tableDict = excelProvider.ImportTable(backupStream);
        var totalLength = tableDict.Sum(t => t.Value.Count);
        IEntityType currentEntityType = null;
        long position = 0;
        Action updateProgress = () =>
        {
            ProgressNotifyAction?.Invoke(
            Convert.ToInt32(position * 100 / totalLength),
            currentEntityType == null ?
                null : GetEntityTypeDisplayName(currentEntityType));
        };

        foreach (var tableInfo in tableDict)
        {
            updateProgress();

            var tableName = tableInfo.Key;
            var table = tableInfo.Value;
            //如果表名不存在
            if(!entityTypeDict.TryGetValue(tableName,out currentEntityType))
            {
                position += table.Count;
                continue;
            }
            //如果表里面没有数据
            if (table.Count < 2)
            {
                position += table.Count;
                continue;
            }
            var propertyDict = currentEntityType.GetProperties().ToDictionary(t => t.GetColumnName(), t => t);
            var headRow = table[0];
            var headDict = new Dictionary<int, string>();
            for (var i = 0; i < headRow.Count; i++)
            {
                headDict[i] = headRow[i].value;
            }
            position ++;

            for(var i=1;i<table.Count;i++)
            {
                position++;
                updateProgress();

                var dataRow = table[i];
                var jsonObj =new JsonObject();
                for (var j = 0; j < dataRow.Count; j++)
                {
                    var columnName = headDict[j];
                    var fieldValueStr = dataRow[j].value;
                    object fieldValue = fieldValueStr;
                    var property = propertyDict[columnName];
                    if (fieldValue == null) { }
                    else if (property.ClrType == typeof(byte) || property.ClrType == typeof(byte?))
                        fieldValue = byte.Parse(fieldValueStr);
                    else if (property.ClrType == typeof(short) || property.ClrType == typeof(short?))
                        fieldValue = short.Parse(fieldValueStr);
                    else if (property.ClrType == typeof(int) || property.ClrType == typeof(int?))
                        fieldValue = int.Parse(fieldValueStr);
                    else if (property.ClrType == typeof(long) || property.ClrType == typeof(long?))
                        fieldValue = long.Parse(fieldValueStr);
                    else if (property.ClrType == typeof(float) || property.ClrType == typeof(float?))
                        fieldValue = float.Parse(fieldValueStr);
                    else if (property.ClrType == typeof(double) || property.ClrType == typeof(double?))
                        fieldValue = double.Parse(fieldValueStr);
                    else if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                        fieldValue = decimal.Parse(fieldValueStr);
                    else if (property.ClrType == typeof(bool) || property.ClrType == typeof(bool?))
                    {
                        switch (fieldValueStr)
                        {
                            case "0":
                                fieldValue = false;
                                break;
                            case "1":
                                fieldValue = true;
                                break;
                            default:
                                fieldValue = bool.Parse(fieldValueStr);
                                break;
                        }
                    }
                    jsonObj[columnName] = JsonValue.Create(fieldValue);
                }
                object item = null;
                try
                {                    
                    item = jsonObj.Deserialize(currentEntityType.ClrType);
                }
                catch (Exception ex)
                {
                    throw new SerializationException($"将数据[{jsonObj}]反序列化为类型[{currentEntityType.ClrType.FullName}]时失败。", ex);
                }
                try
                {
                    dbContext.Add(item);
                }
                catch (Exception ex)
                {
                    throw new SerializationException($"将类型[{currentEntityType.ClrType.FullName}]的数据[{jsonObj}]写入数据库时失败。", ex);
                }
            }
        }
        StateNotifyAction?.Invoke(TextResource.SavingChanges);
        dbContext.SaveChanges();
    }
}