# DbContextBackup
Backup and restore data from database with DbContext.

Prepare:
```csharp
//Backup file format: d3b
var backupContext = new DbContextBackup.D3b.D3bDbContextBackupContext(
    (process, text) => Console.WriteLine($"[Progress][{process}%] {text}"),
    state => Console.WriteLine($"[State] {state}")
    );

//Backup file format: xlsx
var backupContext = new DbContextBackup.Excel.XlsxDbContextBackupContext(
    (process, text) => Console.WriteLine($"[Progress][{process}%] {text}"),
    state => Console.WriteLine($"[State] {state}")
    );
```

Backup:
```csharp
using (var dbContext = new MyDbContext())
{
    backupContext.Backup(dbContext, backupFile);
}
```

Restore:
```csharp
using (var dbContext = new MyDbContext())
{
    backupContext.Restore(dbContext, backupFile);
}
```