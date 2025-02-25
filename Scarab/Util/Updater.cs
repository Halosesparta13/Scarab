﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MessageBox.Avalonia.DTO;
using MessageBox.Avalonia.Models;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.Avalonia;
using Scarab.Util;

namespace Scarab.Services;

public static class Updater
{
    /// <summary>
    /// Runs the code to check for update. If its windows it uses NetSparkle, otherwise it does it manually
    /// </summary>
    public static async Task CheckUpToDate()
    {
        Version? current_version = Assembly.GetExecutingAssembly().GetName().Version;

        Debug.WriteLine($"Current version of installer is {current_version}");

         // if (MainWindowViewModel._Debug) return;

        if (OperatingSystem.IsWindows())
            HandleWindowsUpdate();
        else
            await HandleManualUpdate(current_version);
    }

    /// <summary>
    /// Automatically download updated from github and replace the current exe using NetSparkleUpdater
    /// </summary>
    private static void HandleWindowsUpdate()
    {
        try
        {
            var _sparkle = new SparkleUpdater("https://raw.githubusercontent.com/TheMulhima/Scarab/master/appcast.xml",
                new DSAChecker(SecurityMode.Unsafe)) // use unsafe because I cant be bothered with signing the appcast and stuff
            {
                UIFactory = new UIFactory(null)
                {
                    HideSkipButton = true,
                    AdditionalReleaseNotesHeaderHTML = """
                    <style> 
                    html {background: #282828; background-color: #282828; color: #dedede;}
                    </style>
                    """,
                    ReleaseNotesHTMLTemplate = """
                    <div style="border-color: #000000; border-width: 3px ">
                        <div style="font-size: 20px; padding: 5px; padding-top: 4px; padding-bottom: 0;">
                            {0} ({1})
                        </div>
                        <div style="padding: 5px; font-size: 16px;">
                            {2}
                        </div>
                    </div>
                    """,
                },
                ShowsUIOnMainThread = true, // required for avalonia
                ClearOldInstallers = RemoveOldAUs,
                TmpDownloadFilePath = Settings.GetOrCreateDirPath(), // download to appdata folder which we have full perms in
                // run installer with exe name and path so scarab is replaced correctly
                CustomInstallerArguments = Environment.GetCommandLineArgs()[0], // send the full exe path to the installer so it can replace it correctly
                SecurityProtocolType = SecurityProtocolType.Tls12, // required by github
                // GitHub doesn't support CheckServerFileName, if server is checked, it returns a UUID without any file extension which is not windows friendly
                CheckServerFileName = false,
                RelaunchAfterUpdate = true,
            };

            _sparkle.DownloadHadError += OnDownloadError;
            _sparkle.StartLoop(true, true);
        }
        catch (Exception e)
        {
            Trace.WriteLine(e);
            OnDownloadError(null, "", e);
        }
    }

    /// <summary>
    /// Show a popup when the update fails or cancelled, Will open the update link on the browser and close the app
    /// </summary>
    private static void OnDownloadError(AppCastItem? item, string path, Exception exception)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var links = GetUpdateLinks();
            
            await DisplayErrors.DisplayGenericError(Resources.MWVM_UpdateDownloadError_Message, exception);

            var updateLink = (await links)?.updateLink ?? "https://github.com/TheMulhima/Scarab/releases/latest";

            Process.Start(new ProcessStartInfo(updateLink) { UseShellExecute = true });

            ((IClassicDesktopStyleApplicationLifetime?)Application.Current?.ApplicationLifetime)?.Shutdown();
        });
    }

    /// <summary>
    /// Removes old AutoUpdaters from settings path
    /// </summary>
    private static void RemoveOldAUs()
    {
        try
        {
            if (File.Exists(Path.Combine(Settings.GetOrCreateDirPath(), "Scarab.AU.exe")))
                File.Delete(Path.Combine(Settings.GetOrCreateDirPath(), "Scarab.AU.exe"));
        }
        catch (Exception e)
        {
            Trace.WriteLine(e);
        }
    }

    /// <summary>
    /// Get the latest release info, update link and changelog from the UpdateLinks.json file in static-resources.
    /// It was done like that so the update links and such can change without forcing app update
    /// </summary>
    /// <returns></returns>
    private static async Task<(string latestReleaseInfo, string updateLink, string changelog)?> GetUpdateLinks()
    {
        try
        {
            const string LatestReleaseLinkJson =
                "https://raw.githubusercontent.com/TheMulhima/Scarab/static-resources/UpdateLinks.json";
            string? latestRelease, updateLink, changelog;
            
            var hc = new HttpClient();
            hc.DefaultRequestHeaders.Add("User-Agent", "Scarab");
            
            CancellationTokenSource cts = new CancellationTokenSource(Timeout);
            var links = await hc.GetStringAsync(new Uri(LatestReleaseLinkJson), cts.Token);
            
            JsonDocument linksDoc = JsonDocument.Parse(links);
            if (!linksDoc.RootElement.TryGetProperty(nameof(latestRelease), out JsonElement latestReleaseLinkElem)) 
                return null;
            if (!linksDoc.RootElement.TryGetProperty(nameof(updateLink), out JsonElement updateLinkElem)) 
                return null;
            if (!linksDoc.RootElement.TryGetProperty(nameof(changelog), out JsonElement changeLogElem)) 
                return null;
            
            latestRelease = latestReleaseLinkElem.GetString();
            updateLink = updateLinkElem.GetString();
            changelog = changeLogElem.GetString();
            if (latestRelease is null || updateLink is null || changelog is null)
                return null;
            
            return (latestRelease, updateLink, changelog);
        }
        catch (Exception e) 
        {
            Trace.WriteLine(e);
            return null;
        }
    }

    /// <summary>
    /// Manually check latest release tag from github and prompt download if version is higher on github
    /// </summary>
    private static async Task HandleManualUpdate(Version? current_version)
    {
        try
        {
            var hc = new HttpClient();
            hc.DefaultRequestHeaders.Add("User-Agent", "Scarab");
            
            var links = await GetUpdateLinks();
            if (links is null)
                return;
            
            var cts = new CancellationTokenSource(Timeout);
            var latestRepoInfo = await hc.GetStringAsync(new Uri(links.Value.latestReleaseInfo), cts.Token);

            JsonDocument doc = JsonDocument.Parse(latestRepoInfo);
            if (!doc.RootElement.TryGetProperty("tag_name", out JsonElement tag_elem))
                return;
            string? tag = tag_elem.GetString();
            if (tag is null)
                return;
            if (tag.StartsWith("v"))
                tag = tag[1..];
            if (!Version.TryParse(tag.Length == 1 ? tag + ".0.0.0" : tag, out Version? version))
                return;
            if (version <= current_version)
                return;
            
            string? res = await MessageBoxUtil.GetMessageBoxCustomWindow
            (
                new MessageBoxCustomParams {
                    ButtonDefinitions = new []
                    {
                        new ButtonDefinition { IsDefault = true, IsCancel = true, Name = Resources.MWVM_OutOfDate_GetLatest },
                        new ButtonDefinition { Name = Resources.MWVM_OutOfDate_ContinueAnyways },
                    },
                    ContentTitle = Resources.MWVM_OutOfDate_Title,
                    ContentMessage = string.Format(Resources.MWVM_OutOfDate_Message, version),
                    SizeToContent = SizeToContent.WidthAndHeight
                }
            ).Show();
            if (res == Resources.MWVM_OutOfDate_GetLatest)
            {
                Process.Start(new ProcessStartInfo(links.Value.updateLink) { UseShellExecute = true });
                
                ((IClassicDesktopStyleApplicationLifetime?) Application.Current?.ApplicationLifetime)?.Shutdown();
            }
            else
            {
                Trace.WriteLine($"Installer out of date! Version {current_version} with latest {version}!");
            }
        }
        catch (Exception e)
        {
            Trace.WriteLine(e);
        }
    }
    
    const int Timeout = 15_000;
}