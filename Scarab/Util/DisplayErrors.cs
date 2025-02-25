using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MessageBox.Avalonia.Enums;
using Scarab.Extensions;
using Scarab.Models;
using Scarab.Services;
using Scarab.ViewModels;
using Scarab.Views.Windows;

namespace Scarab.Util;

public static class DisplayErrors
{
    public static async Task DisplayHashMismatch(HashMismatchException e)
    {
        await MessageBoxUtil.GetMessageBoxStandardWindow
        (
            title: Resources.MLVM_DisplayHashMismatch_Msgbox_Title,
            text: string.Format(Resources.MLVM_DisplayHashMismatch_Msgbox_Text, e.Name, e.Actual, e.Expected),
            icon: Icon.Error
        ).Show();
    }

    public static async Task DisplayGenericError(string action, string name, Exception e)
    {
        Trace.TraceError(e.ToString());
    
        string additionalInfo = string.Empty;
        if (e is ReadableError readableException) 
            additionalInfo = $" {readableException.Message}";
            
        await DisplayGenericError(string.Format(Resources.MVVM_ExceptionOccur + additionalInfo, action, name), e);
        
    }
    
    public static async Task DisplayGenericError(string errorText, Exception? e = null)
    {
        if (e != null)
            Trace.TraceError(e.ToString());

        await new ErrorPopup()
        {
            DataContext = new ErrorPopupViewModel(errorText, e)
        }.ShowDialog(AvaloniaUtils.GetMainWindow());
    }

    public static async Task DisplayNetworkError(string name, HttpRequestException e)
    {
        Trace.WriteLine($"Failed to download {name}, {e}");

        await MessageBoxUtil.GetMessageBoxStandardWindow
        (
            title: Resources.MLVM_DisplayNetworkError_Msgbox_Title,
            text: string.Format(Resources.MLVM_DisplayNetworkError_Msgbox_Text, name),
            icon: Icon.Error
        ).Show();
    }
    
    // asks user for confirmation on whether or not they want to uninstall/disable mod.
    // returns whether or not user presses yes on the message box
    public static async Task<bool> DisplayHasDependentsWarning(string modName, IEnumerable<ModItem> dependents)
    {
        var dependentsString = string.Join(", ", dependents.Select(x => x.Name));
        var result = await MessageBoxUtil.GetMessageBoxStandardWindow
        (
            title: Resources.MVVM_DependentsWarning_Header,
            text: string.Format(Resources.MVVM_DependentsWarning_Body, modName, dependentsString),
            icon: Icon.Stop,
            @enum: ButtonEnum.YesNo
        ).Show();

        // return whether or not yes was clicked. Also don't remove mod when box is closed with the x
        return result.HasFlag(ButtonResult.Yes) && !result.HasFlag(ButtonResult.None);
    }

    // asks user for confirmation on whether or not they want to install dependencies when enabling the mod.
    // returns whether or not user presses yes on the message box
    public static async Task<bool> DisplayHasNotInstalledDependenciesWarning(string modName, IEnumerable<ModItem> dependencies)
    {
        var dependenciesString = string.Join(", ", dependencies.Select(x => x.Name));
        var result = await MessageBoxUtil.GetMessageBoxStandardWindow
        (
            title: Resources.MVVM_DependenciesNotInstalledWarning_Header,
            text: string.Format(Resources.MVVM_DependenciesNotInstalledWarning_Body, dependenciesString, modName),
            icon: Icon.Stop,
            @enum: ButtonEnum.YesNo
        ).Show();

        // return whether or not yes was clicked. Also don't remove mod when box is closed with the x
        return result.HasFlag(ButtonResult.Yes) && !result.HasFlag(ButtonResult.None);
    }

    public static Task<bool> DisplayUninstallDependenciesConfirmation(List<SelectableItem<ModItem>> options, bool hasExternalMods)
    {
        var window = new UninstallDependenciesConfirmationWindow
        {
            DataContext = new UninstallDependenciesConfirmationWindowViewModel(options, hasExternalMods)
        };
        
        return window.ShowDialog<bool>(AvaloniaUtils.GetMainWindow());
    }

    public static async Task<bool> DisplayAreYouSureWarning(string warningText)
    {
        var result = await MessageBoxUtil.GetMessageBoxStandardWindow
        (
            title: Resources.MVVM_AreYouSure,
            text: warningText,
            icon: Icon.Stop,
            @enum: ButtonEnum.YesNo
        ).Show();

        // return whether or not yes was clicked. Also don't remove mod when box is closed with the x
        return result.HasFlag(ButtonResult.Yes) && !result.HasFlag(ButtonResult.None);
    }

    public static async Task DoActionAfterConfirmation(bool shouldAskForConfirmation, Func<Task<bool>> warningPopupDisplayer, Func<Task> action)
    {
        if (shouldAskForConfirmation)
        {
            if (await warningPopupDisplayer())
            {
                await action();
            }
        }
        else
        {
            await action();
        }
    }

    private enum IOErrorType
    {
        LockedFile, 
        WriteProtected,
        Other
    }
    
    public static async Task HandleIOExceptionWhenDownloading(Exception e, string action, ModItem? item = null)
    {
        var errorType = e switch
        {
            _ when e.Message.Contains("The process cannot access the file") => IOErrorType.LockedFile,
            _ when e.Message.Contains("The media is write protected") => IOErrorType.WriteProtected,
            _ => IOErrorType.Other
        };
        
        string additionalText = "";

        additionalText = errorType switch
        {
            IOErrorType.LockedFile => GiveMoreDetailsOnLockedFileError(e),
            IOErrorType.WriteProtected => GiveMoreDetailsOnWriteProtectedError(e),
            _ => ""
        };

        if (item != null)
        {
            item.CallOnPropertyChanged(nameof(ModItem.InstallingButtonAccessible));
        }

        await DisplayGenericError(
            $"Unable to {action} {item?.Name}.\n" +
            $"{Resources.MVVM_SystemIOException_GeneralReason}.\n" +
            additionalText, e);
    }

    private static string GetFileNameFromError(string message, string start)
    {
        int indexOfErrorStart = message.IndexOf(start, StringComparison.Ordinal);
        string filePath = message[indexOfErrorStart..].Split('\'')[1];
        return filePath;
    }

    private static string GiveMoreDetailsOnWriteProtectedError(Exception e)
    {
        var additionalText = $"{Resources.MVVM_SystemIOException_ProtectedLocation}.\n";

        try
        {
            string file = GetFileNameFromError(e.Message, "The media is write protected.");
            additionalText += $"{Resources.MVVM_SystemIOException_ScarabCantAccessFile} {file}\n";
        }
        catch (Exception)
        {
            // ignored as its not a requirement
        }

        additionalText += $"\n{Resources.MVVM_RunAsAdmin}";
        
        return additionalText;
    }
    
    private static string GiveMoreDetailsOnLockedFileError(Exception e)
    {
        var additionalText = $"{Resources.MVVM_SystemIOException_GeneralFileLock}\n";
        try
        {
            string filePath = GetFileNameFromError(e.Message, "The process cannot access the file");
            if (OperatingSystem.IsWindows())
            {

                var processes = FileAccessLookup.WhoIsLocking(filePath);
                if (processes.Count > 0)
                {
                    var listOfProcesses = $"{string.Join("\n-", processes.Select(x => x.ProcessName))}";
                    Trace.WriteLine($"Following processes is locking the file {listOfProcesses}");
                    additionalText =
                        $"\n{Resources.MVVM_SystemIOException_LockingProcessesList}\n{listOfProcesses}";
                }

            }
        }
        catch (Exception)
        {
            //ignored as its not a requirement
        }

        return additionalText;
    }
}

/// <summary>
/// An generic error that should display additional information to the user
/// </summary>
public class ReadableError : Exception
{
    public ReadableError(string additionalText) : base(additionalText) { }
}