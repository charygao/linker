﻿using System.Diagnostics;
using System.IO;

namespace linker.libs
{
    public sealed class CommandHelper
    {
        public static string Windows(string arg, string[] commands, bool readResult = true)
        {
            return Execute("cmd.exe", arg, commands, readResult);
        }
        public static string Linux(string arg, string[] commands, bool readResult = true)
        {
            return Execute("/bin/bash", arg, commands, readResult);
        }
        public static string Osx(string arg, string[] commands, bool readResult = true)
        {
            return Execute("/bin/bash", arg, commands, readResult);
        }

        public static Process Execute(string fileName, string arg)
        {
            Process proc = new Process();
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.FileName = fileName;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.Arguments = arg;
            proc.StartInfo.Verb = "runas";
            proc.Start();

            //Process proc = Process.Start(fileName, arg);
            return proc;
        }

        public static string Execute(string fileName, string arg, string[] commands, bool readResult = true)
        {
            using Process proc = new Process();
            proc.StartInfo.WorkingDirectory = Path.GetFullPath(Path.Join("./"));
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.FileName = fileName;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.Arguments = arg;
            proc.StartInfo.Verb = "runas";
            proc.Start();

            if (commands.Length > 0)
            {
                for (int i = 0; i < commands.Length; i++)
                {
                    proc.StandardInput.WriteLine(commands[i]);
                }
            }
            proc.StandardInput.AutoFlush = true;
            if (readResult)
            {
                proc.StandardInput.WriteLine("exit");
                proc.StandardInput.Close();
                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                proc.Close();
                proc.Dispose();

                return output;
            }
            proc.StandardOutput.Read();
            proc.Close();
            proc.Dispose();
            return string.Empty;
        }
    }
}
