using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace DbContextBackup
{
    public class DbContextBackupContextTextResource
    {
        public string SavingChanges { get; set; } = "Saving changes...";
        public string RestoringData { get; set; } = "Restoring data...";
        public string DatabaseBackupFileHasNoData { get; set; } = "Database backup file has no data.";
    }
}
