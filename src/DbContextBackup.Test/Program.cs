using DbContextBackup.Test.Model;

Console.WriteLine("Creating Database and fill data...");
using(var dbContext = new MyDbContext())
{
    if (dbContext.Database.EnsureCreated())
    {
        dbContext.Users.Add(new User() { Id = "U001", Name = "User01" });
        dbContext.Users.Add(new User() { Id = "U002", Name = "User02" });
        dbContext.Books.Add(new Book() { Id = "B001", Name = "The Lord of the Rings", ISBN = "9780618260584" });
    }
    dbContext.SaveChanges();
}
Console.WriteLine("CreatDatabase and fill data done.");

var backupContext = new DbContextBackup.D3b.D3bDbContextBackupContext(
    (process, text) => Console.WriteLine($"[Progress][{process}%] {text}"),
    state => Console.WriteLine($"[State] {state}")
    );
var backupFile = $"backup_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.d3b";
Console.WriteLine("Backuping...");
using (var dbContext = new MyDbContext())
{
    backupContext.Backup(dbContext, backupFile);
}
Console.WriteLine("Backup done.");

Console.WriteLine("Restoring...");
using (var dbContext = new MyDbContext())
{
    backupContext.Restore(dbContext, backupFile);
}
Console.WriteLine("Restore done.");
