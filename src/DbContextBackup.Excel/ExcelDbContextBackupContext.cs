using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DbContextBackup.Excel;

public class ExcelDbContextBackupContext : DbContextBackupContext
{
    private Quick.Excel.XSSFExcelProvider excelProvider = new Quick.Excel.XSSFExcelProvider();

    public ExcelDbContextBackupContext(
        Action<int, string> progressNotify = null,
        Action<string> stateNotify = null,
        DbContextBackupContextTextResource textResource = null) : base(progressNotify, stateNotify, textResource)
    { }

    public override void Backup(DbContext dbContext, Stream backupStream, Func<string, string> tableNameProcessor = null)
    {
        var tablesDict = new Dictionary<string, Quick.Excel.table>();
        Quick.Excel.table currentTable = null;
        IProperty[] currentTableProperties = null;
        innerBackup(dbContext, tableNameProcessor, entityType =>
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
        throw new NotImplementedException();
    }
}