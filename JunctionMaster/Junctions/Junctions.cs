using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Junctions
{
    public class Junction
    {
        public string Source { get; set; }
        public string Target { get; set; }
        public bool Hidden { get; set; }
        public Junction(string source, string target)
        {
            Source = source;
            Target = target;
        }

        public override string ToString()
        {
            return Source + " to " + Target;
        }
    }

    public abstract class AsyncQuery
    {
        protected static string CD = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        protected Process Process;
        protected Thread ExecutionThread;
        protected SynchronizationContext SyncContext;
        protected string Filename, Arguments;
        public event EventHandler QueryCompleted;
        public AsyncQuery()
        {
            Process = new Process();
            Process.StartInfo.RedirectStandardInput = true;
            Process.StartInfo.RedirectStandardOutput = true;
            Process.StartInfo.UseShellExecute = false;
            Process.StartInfo.CreateNoWindow = true;
            Process.Exited += Process_Exited;
            SyncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            ExecutionThread.Start();
        }

        protected void Init(string filename, string arguments)
        {
            Process.StartInfo.FileName = "UtfRedirect.exe";
            Process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            Filename = filename;
            Arguments = arguments;
            ExecutionThread = new Thread(Worker);
        }

        private void Worker()
        {
            Thread.CurrentThread.Name = Filename + " " + Arguments;
            Process.Start();
            Process.StandardInput.WriteLine(Filename);
            Process.StandardInput.WriteLine(Arguments);
            ProcessOutput();
        }

        protected abstract void ProcessOutput();

        void Process_Exited(object sender, EventArgs e)
        {
            if (QueryCompleted != null) QueryCompleted(this, null);
        }
    }

    public class BatchRunner
    {
        const string EndSignal = "BatchRunner: Task Complete";
        SynchronizationContext SC;
        FileStream FS;
        StreamWriter SW;
        string OutputPath, SaveTo;
        int CodePage;
        
        public BatchRunner(string path, string saveTo = null, int encoding = 0)
        {
            OutputPath = path;
            CodePage = encoding;
            FS = new FileStream(path, FileMode.Create, FileAccess.Write);
            SW = new StreamWriter(FS, Encoding.GetEncoding(encoding));
            SaveTo = saveTo;
            if (encoding != 0) SW.WriteLine("chcp " + encoding);
            if (SynchronizationContext.Current == null)
                SC = new SynchronizationContext();
            else
                SC = SynchronizationContext.Current;
        }

        int OutputNumber = 0;
        int TotalOutputs;
        private string NextOutputFileName()
        {
            OutputNumber++;
            return SaveTo.Remove(SaveTo.LastIndexOf('.'))+'_'+ OutputNumber.ToString().PadLeft(3, '0') + Path.GetExtension(SaveTo);            
        }

        public void AddCommand(string command, bool suppressOutput = false)
        {
            if (FS != null)
            {
                if (SaveTo != null && !suppressOutput) command += " > " + NextOutputFileName();
                SW.WriteLine(command);
            }
            else
                throw new Exception("The file has been already finalized!");
        }

        public void Echo(string text)
        {
            if (FS != null)
            {
                SW.WriteLine("echo " + text);
            }
            else
                throw new Exception("The file has been already finalized!");
        }

        public void FinalizeFile(bool pause = false)
        {
            if (FS != null)
            {
                if (pause) AddCommand("PAUSE", true);
                Echo(EndSignal);
                TotalOutputs = OutputNumber;
                SW.Flush();
                SW.Close();
                SW.Dispose();
                SW = null;
                FS.Dispose();
                FS = null;
            }
        }

        public void Run()
        {
            FinalizeFile();
            Thread WorkerThread = new Thread(Worker);
            WorkerThread.Start();
        }

        public static void RunFile(string file, EventHandler callback)
        {
            Process P = new Process();
            P.StartInfo.FileName = "PsExec.exe";
            P.StartInfo.Arguments = "-s cmd.exe /C \"" + file + "\"";
            P.StartInfo.RedirectStandardOutput = true;
            P.StartInfo.UseShellExecute = false;
            P.Start();
            string line;
            while (true)
            {
                line = P.StandardOutput.ReadLine();
                if (line == null)
                    Thread.Sleep(100);
                else
                    if (line.Contains(EndSignal)) break;
            }
            P.Kill();
            P.Dispose();
            if (callback != null) callback(null, null);
        }

        private void Worker()
        {
            Process P = new Process();
            P.StartInfo.FileName = "PsExec.exe";
            P.StartInfo.Arguments = "-s cmd.exe /C \"" + OutputPath + "\"";
            P.StartInfo.RedirectStandardOutput = true;
            P.StartInfo.UseShellExecute = false;
            P.Start();
            string line;
            while (true)
            {
                line = P.StandardOutput.ReadLine();
                if (line == null) 
                    Thread.Sleep(100);
                else 
                    if (line.Contains(EndSignal)) break;
            }
            SC.Post(TaskCompleteCallback, null);
            P.Kill();
            P.Dispose();
        }

        void TaskCompleteCallback(object o)
        {
            List<string> lines = new List<string>();
            if (SaveTo != null)
            {
                OutputNumber = 0;
                for (int i = 0; i < TotalOutputs; i++)
                    lines.AddRange(File.ReadAllLines(NextOutputFileName(), Encoding.GetEncoding(CodePage)));
            }
            if (TaskCompleted != null) TaskCompleted(this, new TaskCompletedEventArgs(lines.ToArray()));
        }

        public class TaskCompletedEventArgs
        {
            public string[] Output;
            public TaskCompletedEventArgs(string[] output)
            {
                Output = output;
            }
        }
        public delegate void TaskCompletedEventHandler(object sender, TaskCompletedEventArgs e);
        public event TaskCompletedEventHandler TaskCompleted;
    }

    public class DirectoryMover
    {
        private const int CodePage = 1250;
        private const string RenameTail = "-NoJunction";
        public char SourceVolumeOnTargetOS { get; set; }
        public char DestinationVolumeOnTargetOS { get; set; }
        public string DestinationPath { get; set; }
        public ObservableCollection<string> Directories { get; private set; }
        public DirectoryMover()
        {
            Directories = new ObservableCollection<string>();
        }

        string Path;
        bool Execute;
        public void Move(string path, bool execute)
        {
            Path = path;
            BatchRunner BR = new BatchRunner(path + "\\JunctionQuery.bat", path + "\\Junctions.txt", CodePage);
            foreach(string dir in Directories)
            {
                BR.AddCommand("dir \""+dir+"\" /AL /S");
            }
            BR.TaskCompleted += JunctionQueryCompleted;
            Execute = execute;
            BR.Run();
        }

        void JunctionQueryCompleted(object sender, BatchRunner.TaskCompletedEventArgs e)
        {
            string dateTail = DateTime.Now.ToString("_yyyy-MM-dd HH-mm-ss");
            List<Junction> junctions = new List<Junction>();
            Regex junctionRegex = new Regex(@"<JUNCTION>\s*([^[]*)\[([^]]*)]");
            string dir = null;
            foreach (string line in e.Output)
            {
                if (line.StartsWith(" Directory of "))
                {
                    dir = line.Substring(14);
                }
                else if (line.Contains("<JUNCTION>"))
                {
                    Match M = junctionRegex.Match(line);
                    junctions.Add(new Junction(dir + "\\" + M.Groups[1].Value.TrimEnd(), M.Groups[2].Value));
                }
            }

            foreach (Junction J in junctions)
            {
                try
                {
                    DirectoryInfo DI = new DirectoryInfo(J.Source);
                    J.Hidden = DI.Attributes.HasFlag(FileAttributes.Hidden);
                }
                catch { }
            }

            BatchRunner mover = new BatchRunner(Path + "\\MoveJunctions" + dateTail + ".bat", Path + "\\MoveLog.txt", CodePage);
            BatchRunner recovery = new BatchRunner(Path + "\\RecoverJunctions" + dateTail + ".bat",null,CodePage);
            BatchRunner finalizer = new BatchRunner(Path + "\\FinalizeJunctions" + dateTail + ".bat", null, CodePage);

            mover.Echo("Moving directories...");
            string[] destDirs = new string[Directories.Count];
            int i = 0;
            foreach (string d in Directories)
            {
                destDirs[i] = DestinationPath + d.Substring(3);
                mover.AddCommand("robocopy \"" + d + "\" \"" + destDirs[i] + "\" /MIR /XJ /COPYALL /ZB");
                i++;
            }

            mover.Echo("Renaming directories...");
            bool hidden, system;
            foreach (string d in Directories)
            {
                hidden = false;
                system = false;
                try
                {
                    DirectoryInfo DI = new DirectoryInfo(d);
                    hidden = DI.Attributes.HasFlag(FileAttributes.Hidden);
                    system = DI.Attributes.HasFlag(FileAttributes.System);
                }
                catch { }
                if (system || hidden)
                {
                    mover.AddCommand("attrib " + (system ? "-S " : "") + (hidden ? "-H " : "") + "\"" + d + "\"", true);
                    recovery.AddCommand("attrib " + (system ? "-S " : "") + (hidden ? "-H " : "") + "\"" + d + "\"");
                }
                mover.AddCommand("rename \"" + d + "\" \"" + d.Substring(d.LastIndexOf('\\') + 1)+RenameTail + "\"", true);
                mover.AddCommand("attrib +H \"" + d + RenameTail + "\"", true);
                recovery.AddCommand("attrib -H \"" + d + RenameTail + "\"");
                recovery.AddCommand("rmdir \"" + d + "\" /Q");
                recovery.AddCommand("rename \"" + d + RenameTail + "\" \"" + d.Substring(d.LastIndexOf('\\') + 1) + "\"", true);
                if (hidden || system)
                {
                    recovery.AddCommand("attrib " + (system ? "+S " : "") + (hidden ? "+H " : "") + "\"" + d + "\"");
                }
                finalizer.AddCommand("attrib -H \"" + SourceVolumeOnTargetOS + d.Substring(1) + RenameTail + "\"");
                finalizer.AddCommand("rmdir \"" + SourceVolumeOnTargetOS + d.Substring(1) + RenameTail + "\" /S /Q");
            }
            recovery.FinalizeFile();
            finalizer.FinalizeFile();

            string destinationPathForTargetOS = DestinationVolumeOnTargetOS + DestinationPath.Substring(1);
            mover.Echo("Making softlinks...");
            foreach (string d in Directories)
            {
                mover.AddCommand("mklink /J \"" + d + "\" \"" + destinationPathForTargetOS + d.Substring(3) + "\"", true);
                hidden = false;
                system = false;
                try
                {
                    DirectoryInfo DI = new DirectoryInfo(d);
                    hidden = DI.Attributes.HasFlag(FileAttributes.Hidden);
                    system = DI.Attributes.HasFlag(FileAttributes.System);
                    if (system || hidden)
                    {
                        mover.AddCommand("attrib " + (system ? "+S " : "") + (hidden ? "+H " : "") + "\"" + d + "\" /L", true);
                    }
                }
                catch { }
            }

            mover.Echo("Making junctions...");
            foreach (Junction j in junctions)
            {
                string source = DestinationPath + j.Source.Substring(3);
                string target = j.Target;
                mover.AddCommand("mklink /J \"" + source + "\" \"" + target + "\"", true);
                if (j.Hidden) mover.AddCommand("attrib +H \"" + source + "\"", true);
            }
            mover.TaskCompleted += mover_TaskCompleted;
            if (Execute)
                mover.Run();
            else
                mover.FinalizeFile();
        }

        public event EventHandler Completed;
        void mover_TaskCompleted(object sender, BatchRunner.TaskCompletedEventArgs e)
        {
            if (Completed != null) Completed(this, new EventArgs());
        }
    }
}
