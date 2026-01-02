using System;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32.TaskScheduler;

namespace RemoteKM.Client;

internal static class StartupTaskManager
{
    internal const string TaskName = "remoteKMClient";
    private const string TaskDescription = "RemoteKM Client auto-start task";

    internal static bool IsEnabled()
    {
        using var ts = new TaskService();
        var enabled = ts.GetTask(TaskName) != null;
        Console.WriteLine($"StartupTaskManager: IsEnabled={enabled}");
        return enabled;
    }

    internal static void SetEnabled(bool enabled)
    {
        using var ts = new TaskService();
        Console.WriteLine($"StartupTaskManager: SetEnabled={enabled}");
        if (!enabled)
        {
            if (ts.GetTask(TaskName) == null)
            {
                Console.WriteLine("StartupTaskManager: task not found for delete.");
                return;
            }

            ts.RootFolder.DeleteTask(TaskName, false);
            Console.WriteLine("StartupTaskManager: task deleted.");
            return;
        }

        var exePath = Application.ExecutablePath;
        var workingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;
        var userId = GetCurrentUserId();
        Console.WriteLine($"StartupTaskManager: exePath={exePath} workDir={workingDirectory} user={userId}");

        var td = ts.NewTask();
        td.RegistrationInfo.Description = TaskDescription;
        td.Principal.RunLevel = TaskRunLevel.Highest;
        td.Principal.LogonType = TaskLogonType.InteractiveToken;
        td.Settings.RunOnlyIfLoggedOn = true;
        td.Settings.StartWhenAvailable = true;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            td.Principal.UserId = userId;
        }
        var trigger = new LogonTrigger();
        if (!string.IsNullOrWhiteSpace(userId))
        {
            trigger.UserId = userId;
        }
        trigger.Delay = TimeSpan.FromSeconds(5);
        td.Triggers.Add(trigger);
        td.Actions.Add(new ExecAction(exePath, null, workingDirectory));

        ts.RootFolder.RegisterTaskDefinition(
            TaskName,
            td,
            TaskCreation.CreateOrUpdate,
            string.IsNullOrWhiteSpace(userId) ? null : userId,
            null,
            TaskLogonType.InteractiveToken);
        Console.WriteLine("StartupTaskManager: task registered.");
    }

    private static string GetCurrentUserId()
    {
        try
        {
            return WindowsIdentity.GetCurrent().Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
