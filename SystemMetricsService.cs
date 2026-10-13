using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ZPulse.Services;

public record MetricData(
    float CpuPercentage,
    float MemoryPercentage,
    double UsedMemoryGb,
    double TotalMemoryGb,
    float DiskPercentage,
    string DiskDetails
);

public class SystemMetricsService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint DateTimeLow;
        public uint DateTimeHigh;
        public ulong Value => ((ulong)DateTimeHigh << 32) | DateTimeLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private FILETIME _prevIdle;
    private FILETIME _prevKernel;
    private FILETIME _prevUser;
    private bool _hasPrevTimes;

    private PerformanceCounter? _cpuCounter;
    private bool _cpuCounterFailed;

    private PerformanceCounter? _diskTimeCounter;
    private bool _diskCounterFailed;

    public SystemMetricsService()
    {
        // 1. Inicializa o contador de CPU idêntico ao do Gerenciador de Tarefas do Windows 10/11 (% Processor Utility)
        try
        {
            _cpuCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
            _cpuCounter.NextValue(); // aquece primeira leitura
        }
        catch
        {
            try
            {
                // Fallback para % Processor Time
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch
            {
                _cpuCounterFailed = true;
                _cpuCounter = null;
            }
        }

        // 2. Inicializa leitura base de CPU via GetSystemTimes para fallback
        if (GetSystemTimes(out _prevIdle, out _prevKernel, out _prevUser))
        {
            _hasPrevTimes = true;
        }

        // 3. Tenta inicializar o contador de atividade de disco (% Disk Time)
        try
        {
            _diskTimeCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
            _diskTimeCounter.NextValue(); // aquece primeira leitura
        }
        catch
        {
            _diskCounterFailed = true;
            _diskTimeCounter = null;
        }
    }

    public MetricData ReadMetrics()
    {
        float cpu = ReadCpuUsage();
        var (memPercent, usedGb, totalGb) = ReadMemoryUsage();
        var (diskPercent, diskDetails) = ReadDiskUsage();

        return new MetricData(cpu, memPercent, usedGb, totalGb, diskPercent, diskDetails);
    }

    private float ReadCpuUsage()
    {
        // 1. Prioridade: Métrica de Utilidade de Processador oficial do Gerenciador de Tarefas
        if (!_cpuCounterFailed && _cpuCounter != null)
        {
            try
            {
                float val = _cpuCounter.NextValue();
                return (float)Math.Clamp(Math.Round(val, 0), 0.0, 100.0);
            }
            catch
            {
                _cpuCounterFailed = true;
            }
        }

        // 2. Fallback de alta performance: Win32 GetSystemTimes
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return 0f;

        if (!_hasPrevTimes)
        {
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _hasPrevTimes = true;
            return 0f;
        }

        ulong idleDiff = idle.Value - _prevIdle.Value;
        ulong kernelDiff = kernel.Value - _prevKernel.Value;
        ulong userDiff = user.Value - _prevUser.Value;
        ulong totalDiff = kernelDiff + userDiff;

        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;

        if (totalDiff == 0) return 0f;

        double usage = (1.0 - (double)idleDiff / totalDiff) * 100.0;
        return (float)Math.Clamp(usage, 0.0, 100.0);
    }

    private (float percent, double usedGb, double totalGb) ReadMemoryUsage()
    {
        var memStatus = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memStatus))
        {
            double total = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            double avail = memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
            double used = total - avail;
            float percent = (float)memStatus.dwMemoryLoad;
            return (percent, Math.Round(used, 1), Math.Round(total, 1));
        }

        return (0f, 0, 0);
    }

    private (float percent, string details) ReadDiskUsage()
    {
        // 1. Atividade ativa (% Disk Time) como no Gerenciador de Tarefas
        if (!_diskCounterFailed && _diskTimeCounter != null)
        {
            try
            {
                float activeTime = _diskTimeCounter.NextValue();
                float clamped = Math.Clamp(activeTime, 0f, 100f);
                return (clamped, "Atividade");
            }
            catch
            {
                _diskCounterFailed = true;
            }
        }

        // 2. Fallback resiliente: Espaço ocupado no drive principal C:
        try
        {
            var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            if (systemDrive.IsReady)
            {
                double total = systemDrive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                double free = systemDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                double used = total - free;
                float percent = (float)((used / total) * 100.0);
                return ((float)Math.Round(percent, 0), $"{Math.Round(free, 0)} GB livres");
            }
        }
        catch { }

        return (0f, "Disco");
    }

    public void Dispose()
    {
        _cpuCounter?.Dispose();
        _diskTimeCounter?.Dispose();
    }
}
