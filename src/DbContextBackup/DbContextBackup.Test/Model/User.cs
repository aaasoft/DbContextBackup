using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DbContextBackup.Test.Model
{
    [DisplayName("User Model")]
    public class User
    {
        [Key]
        [MaxLength(100)]
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
