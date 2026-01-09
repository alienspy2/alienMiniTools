using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;

namespace RemoteKM.Server;

internal static class TaskSchedulerService
{
    private const string TaskName = "RemoteKM Server Autostart";
    private const string TaskDescription = "Starts RemoteKM Server at user logon with administrator privileges";

    internal static bool IsTaskRegistered()
    {
        using var ts = new TaskService();
        return ts.GetTask(TaskName) != null;
    }

    internal static void RegisterTask()
    {
        var exePath = GetExecutablePath();

        using var ts = new TaskService();

        if (ts.GetTask(TaskName) != null)
        {
            ts.RootFolder.DeleteTask(TaskName);
        }

        var td = ts.NewTask();
        td.RegistrationInfo.Description = TaskDescription;
        td.RegistrationInfo.Author = "RemoteKM";

        td.Principal.RunLevel = TaskRunLevel.Highest;
        td.Principal.LogonType = TaskLogonType.InteractiveToken;

        var trigger = new LogonTrigger
        {
            UserId = WindowsIdentity.GetCurrent().Name,
            Delay = TimeSpan.FromSeconds(5)
        };
        td.Triggers.Add(trigger);

        td.Actions.Add(new ExecAction(exePath, null, Path.GetDirectoryName(exePath)));

        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        td.Settings.AllowDemandStart = true;

        ts.RootFolder.RegisterTaskDefinition(
            TaskName,
            td,
            TaskCreation.CreateOrUpdate,
            null,
            null,
            TaskLogonType.InteractiveToken);
    }

    internal static void UnregisterTask()
    {
        using var ts = new TaskService();
        if (ts.GetTask(TaskName) != null)
        {
            ts.RootFolder.DeleteTask(TaskName);
        }
    }

    internal static bool ToggleRegistration()
    {
        if (IsTaskRegistered())
        {
            UnregisterTask();
            return false;
        }

        RegisterTask();
        return true;
    }

    private static string GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            return processPath;
        }

        return Path.Combine(AppContext.BaseDirectory, "RemoteKM.Server.exe");
    }
}
