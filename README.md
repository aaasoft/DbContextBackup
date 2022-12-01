# DbContextBackup
Backup and restore data from database with DbContext.

Prepare:
```csharp
var backupContext = new DbContextBackup.DbContextBackupContext(
    (process, text) => Console.WriteLine($"[Progress][{process}%] {text}"),
    state => Console.WriteLine($"[State] {state}")
    );
```

Prepare:
```csharp
var backupContext = new DbContextBackup.DbContextBackupContext(
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