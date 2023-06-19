﻿#if OS_WINDOWS
using System.Diagnostics;

namespace SystemMonitor.Agent.Windows
{
    public class ProcessInfo
    {
        public long GetMemoryAllocated()
        {
            var process = Process.GetCurrentProcess();
            var used = process.PrivateMemorySize64;
            return used;
        }
    }
}
#endif
