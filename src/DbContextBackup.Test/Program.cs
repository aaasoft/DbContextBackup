using DbContextBackup.Test.Model;

Console.WriteLine("Creating Database and fill data...");
using (var dbContext = new MyDbContext())
{
    dbContext.Database.EnsureDeleted();
    dbContext.Database.EnsureCreated();

    dbContext.Users.Add(new User() { Id = "U001", Name = "User01" });
    dbContext.Users.Add(new User() { Id = "U002", Name = "User02" });
    dbContext.Books.Add(new Book() { Id = "B001", Name = "The Lord of the Rings : The Fellowship of the Ring", ISBN = "9780618260584", ReadCount = 99 });
    dbContext.Books.Add(new Book() { Id = "B002", Name = "The Lord of the Rings : Two Towers", ISBN = "9780618260585", IsRead = true });

    dbContext.SaveChanges();
}
Console.WriteLine("CreatDatabase and fill data done.");

var fileExtion = "xlsx";
//var fileExtion = "d3b";
var backupContext = new DbContextBackup.Excel.ExcelDbContextBackupContext(
    (process, text) => Console.WriteLine($"[Progress][{process}%] {text}"),
    state => Console.WriteLine($"[State] {state}")
    );
var backupFile = $"backup_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.{fileExtion}";
Console.WriteLine("Backuping...");
using (var dbContext = new MyDbContext())
{
    backupContext.Backup(dbContext, backupFile);
}
Console.WriteLine("Backup done.");

Console.WriteLine("Restoring...");
using (var dbContext = new MyDbContext())
{
    dbContext.Database.EnsureDeleted();
    dbContext.Database.EnsureCreated();
    backupContext.Restore(dbContext, backupFile);
}
Console.WriteLine("Restore done.");
