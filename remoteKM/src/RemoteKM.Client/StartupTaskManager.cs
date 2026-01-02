using System.IO;
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
        return ts.GetTask(TaskName) != null;
    }

    internal static void SetEnabled(bool enabled)
    {
        using var ts = new TaskService();
        if (!enabled)
        {
            if (ts.GetTask(TaskName) == null)
            {
                return;
            }

            ts.RootFolder.DeleteTask(TaskName, false);
            return;
        }

        var exePath = Application.ExecutablePath;
        var workingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;

        var td = ts.NewTask();
        td.RegistrationInfo.Description = TaskDescription;
        td.Principal.RunLevel = TaskRunLevel.Highest;
        td.Triggers.Add(new LogonTrigger());
        td.Actions.Add(new ExecAction(exePath, null, workingDirectory));

        ts.RootFolder.RegisterTaskDefinition(
            TaskName,
            td,
            TaskCreation.CreateOrUpdate,
            null,
            null,
            TaskLogonType.InteractiveToken);
    }
}
