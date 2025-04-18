﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DbContextBackup.Test.Model
{
    [DisplayName("Book Model")]
    public class Book
    {
        [Key]
        [MaxLength(100)]
        public string Id { get; set; }
        public string Name { get; set; }
        public string ISBN { get; set; }
        public int ReadCount { get; set; }
        public bool? IsRead { get; set; }
    }
}
