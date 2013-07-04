using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Launcher
{
    class Program
    {
        static void Main(string[] args)
        {
            string CD = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Process P = new Process();
            P.StartInfo.FileName = "psexec.exe";
            P.StartInfo.Arguments = "-i -s \"" + CD + "\\JunctionMaster.exe\"";
            P.Start();
        }
    }
}
