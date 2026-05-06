using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoSaver
{
    public class BackupProgressData
    {

        public double Percentage { get; set; }

        public double Speed { get; set; }
        public string TimeElapsed { get; set; }

        public int CurrentFileNumber { get; set; }

        public int TotalFiles { get; set; }

        public long CopiedBytes { get; set; }

        public long TotalBytes { get; set; }

    }
}
